<#
.SYNOPSIS
    Publica la versión actual del código y actualiza el servicio de Windows CFDIManager.

.DESCRIPTION
    Para cuando el servicio ya está instalado (ver Instalar-Servicio.ps1). Este script:
      1. Compila y publica CFDIManager.Web en Release a una carpeta temporal.
      2. Pide permisos de administrador (UAC) para detener el servicio.
      3. Copia la publicación a la carpeta del servicio sin tocar appsettings.Production.json
         ni la carpeta Storage (datos del servidor).
      4. Arranca el servicio y comprueba que responde.

    Se puede ejecutar desde una consola normal; Windows pedirá confirmar con UAC:

        pwsh -File .\herramientas\Actualizar-Servicio.ps1
#>
param(
    [string]$Carpeta = 'C:\CFDIManager',
    [int]$Puerto = 5000,
    # Uso interno: la parte que corre ya como administrador.
    [string]$Publicacion
)

$ErrorActionPreference = 'Stop'
$servicio = 'CFDIManager'

if (-not $Publicacion) {
    # 1. Publicar (no requiere administrador)
    $raiz = Split-Path $PSScriptRoot -Parent
    $Publicacion = Join-Path ([IO.Path]::GetTempPath()) 'CFDIManager-publicacion'
    if (Test-Path $Publicacion) { Remove-Item $Publicacion -Recurse -Force }

    Write-Host 'Publicando CFDIManager.Web (Release)...'
    dotnet publish (Join-Path $raiz 'CFDIManager.Web\CFDIManager.Web.csproj') -c Release -o $Publicacion -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'La publicación falló; el servicio no se tocó.' }
    if (Test-Path (Join-Path $Publicacion 'Storage')) { throw 'La publicación trae una carpeta Storage; revisa el .csproj.' }

    # 2. El resto como administrador, en otra ventana; su salida queda en un log.
    $log = Join-Path $Publicacion 'actualizar.log'
    Start-Process pwsh -Verb RunAs -Wait -ArgumentList @(
        '-NoProfile', '-File', "`"$PSCommandPath`"",
        '-Carpeta', "`"$Carpeta`"", '-Puerto', $Puerto, '-Publicacion', "`"$Publicacion`"")

    if (Test-Path $log) { Get-Content $log }
    else { Write-Host 'No se pudo leer el resultado (¿se canceló el UAC?).' -ForegroundColor Red }
    return
}

$log = Join-Path $Publicacion 'actualizar.log'
function Registrar([string]$texto) { $texto | Out-File $log -Append -Encoding utf8 }

try {
    # 3. Detener, copiar y arrancar
    Stop-Service $servicio -Force
    (Get-Service $servicio).WaitForStatus('Stopped', '00:01:00')

    robocopy $Publicacion $Carpeta /E /XD Storage /XF appsettings.Production.json actualizar.log /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy terminó con código $LASTEXITCODE." }
    Registrar "Archivos copiados a $Carpeta."

    Start-Service $servicio

    # 4. Prueba
    $ok = $false
    for ($i = 0; $i -lt 40 -and -not $ok; $i++) {
        Start-Sleep -Seconds 3
        try {
            $r = Invoke-WebRequest "http://localhost:$Puerto/cuenta/login" -UseBasicParsing -TimeoutSec 5
            $ok = $r.StatusCode -eq 200
        } catch { }
    }

    if ($ok) { Registrar "OK: el servicio está $((Get-Service $servicio).Status) y responde en http://localhost:$Puerto" }
    else { Registrar "ATENCIÓN: el servicio está $((Get-Service $servicio).Status) pero no respondió en 2 minutos. Revisa el Visor de eventos." }
}
catch {
    Registrar "ERROR: $_"
    Start-Service $servicio -ErrorAction SilentlyContinue
}
