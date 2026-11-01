<#
.SYNOPSIS
    Instala (o reinstala) CFDI Manager publicado como servicio de Windows.

.DESCRIPTION
    Antes hay que publicar la app en la carpeta de destino:

        dotnet publish CFDIManager.Web\CFDIManager.Web.csproj -c Release -o C:\CFDIManager

    y copiar ahí la carpeta Storage (sin Llaves). Este script:
      1. Cierra la app de desarrollo si está corriendo (dos instancias duplicarían las tareas automáticas).
      2. Crea appsettings.Production.json con el puerto, la contraseña de PostgreSQL y la
         clave maestra (CFDI_CLAVE_MAESTRA), legible solo por SYSTEM y Administradores.
      3. Crea el servicio "CFDIManager" (inicio automático, se reinicia solo si falla).
      4. Abre el puerto en el Firewall para la red local.
      5. Arranca el servicio y comprueba que responde.

    Ejecutar en PowerShell como administrador, desde la carpeta del proyecto:

        powershell -ExecutionPolicy Bypass -File .\herramientas\Instalar-Servicio.ps1
#>
param(
    [string]$Carpeta = 'C:\CFDIManager',
    [int]$Puerto = 5000
)

$ErrorActionPreference = 'Stop'
$servicio = 'CFDIManager'
$exe = Join-Path $Carpeta 'CFDIManager.Web.exe'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Ejecuta este script en PowerShell como administrador.' }
if (-not (Test-Path $exe)) { throw "No existe $exe. Primero publica la app (ver la ayuda del script)." }
if (-not (Test-Path (Join-Path $Carpeta 'Storage\XML'))) { throw "Falta la carpeta Storage en $Carpeta." }

# 1. App de desarrollo
Get-Process CFDIManager.Web -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and -not $_.Path.StartsWith($Carpeta, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        Write-Host "Cerrando la app de desarrollo ($($_.Path))" -ForegroundColor Yellow
        Stop-Process -Id $_.Id -Force
    }

# Si el servicio ya existe se detiene para poder reescribir su configuración.
$existente = Get-Service $servicio -ErrorAction SilentlyContinue
if ($existente) {
    Write-Host 'El servicio ya existe; se reinstala.' -ForegroundColor Yellow
    if ($existente.Status -ne 'Stopped') { Stop-Service $servicio -Force }
    sc.exe delete $servicio | Out-Null
    Start-Sleep -Seconds 2
}

# 2. Configuración de producción (el servicio corre como SYSTEM y no ve las variables de tu usuario)
$password = [Environment]::GetEnvironmentVariable('CFDI_POSTGRES_PASSWORD', 'User')
if (-not $password) { $password = [Environment]::GetEnvironmentVariable('CFDI_POSTGRES_PASSWORD', 'Machine') }
if (-not $password) {
    $seguro = Read-Host 'Contraseña de PostgreSQL (usuario postgres)' -AsSecureString
    $password = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro))
}

# Clave maestra: la misma en todos los equipos para leer las contraseñas guardadas (CIEC, correo, SINUBE).
$claveMaestra = [Environment]::GetEnvironmentVariable('CFDI_CLAVE_MAESTRA', 'User')
if (-not $claveMaestra) { $claveMaestra = [Environment]::GetEnvironmentVariable('CFDI_CLAVE_MAESTRA', 'Machine') }
if (-not $claveMaestra) {
    Write-Host 'No existe CFDI_CLAVE_MAESTRA: las contraseñas guardadas solo se podrán leer en este equipo.' -ForegroundColor Yellow
}

$produccion = Join-Path $Carpeta 'appsettings.Production.json'
$configuracion = [ordered]@{
    Urls                   = "http://0.0.0.0:$Puerto"
    CFDI_POSTGRES_PASSWORD = $password
}
if ($claveMaestra) { $configuracion.CFDI_CLAVE_MAESTRA = $claveMaestra }
$configuracion | ConvertTo-Json | Set-Content -Path $produccion -Encoding UTF8

# Solo SYSTEM (S-1-5-18) y Administradores (S-1-5-32-544); por SID para no depender del idioma de Windows.
icacls $produccion /inheritance:r /grant:r '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
Write-Host "Configuración creada: $produccion (solo SYSTEM y Administradores)" -ForegroundColor Green

# 3. Servicio
New-Service -Name $servicio -BinaryPathName "`"$exe`"" -DisplayName 'CFDI Manager' `
    -Description "CFDI Manager Bóveda (Blazor). Carpeta: $Carpeta" -StartupType Automatic | Out-Null
sc.exe failure $servicio reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
Write-Host "Servicio '$servicio' creado (inicio automático)." -ForegroundColor Green

# 4. Firewall (perfiles de dominio y privado; no la red pública)
$regla = "CFDI Manager (TCP $Puerto)"
Get-NetFirewallRule -DisplayName $regla -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $regla -Direction Inbound -Protocol TCP -LocalPort $Puerto `
    -Action Allow -Profile Domain, Private | Out-Null
Write-Host "Firewall: puerto $Puerto abierto para la red local." -ForegroundColor Green

# 5. Arranque y prueba
Start-Service $servicio
Write-Host 'Esperando a que la app responda (la primera vez sincroniza Storage y puede tardar)...'
$ok = $false
for ($i = 0; $i -lt 60 -and -not $ok; $i++) {
    Start-Sleep -Seconds 3
    try {
        $r = Invoke-WebRequest "http://localhost:$Puerto/" -UseBasicParsing -TimeoutSec 5 -MaximumRedirection 5
        $ok = $r.StatusCode -eq 200
    } catch { }
}

if ($ok) {
    Write-Host "`nCFDI Manager está corriendo como servicio." -ForegroundColor Green
    Write-Host "  En este equipo:  http://localhost:$Puerto"
    Get-NetIPAddress -AddressFamily IPv4 |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        ForEach-Object { Write-Host "  En la red:       http://$($_.IPAddress):$Puerto" }
} else {
    Write-Host "`nEl servicio no respondió en 3 minutos. Estado: $((Get-Service $servicio).Status)" -ForegroundColor Red
    Write-Host 'Revisa el Visor de eventos > Registros de Windows > Aplicación (origen CFDIManager / .NET Runtime).'
}
