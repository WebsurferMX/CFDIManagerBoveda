<#
.SYNOPSIS
    Vuelve a tomar las capturas del manual de usuario (CFDIManager.Web\wwwroot\ayuda\img) con una empresa de
    demostración y datos ficticios, para que nunca salgan datos de clientes (el repositorio es público).

.DESCRIPTION
    1. Compila la app de escritorio y publica la web en una carpeta temporal.
    2. Genera 98 CFDI ficticios de los últimos tres meses (generar-demo.mjs) para la empresa de prueba del SAT
       EKU9003173C9 (ESCUELA KEMPER URGATE); clientes, proveedores y empleados también son RFC de prueba del SAT.
    3. Arranca la app en modo Local con una carpeta de datos nueva (PostgreSQL de este equipo), a 1440 × 900.
    4. capturas.mjs da de alta la empresa, indexa los XML y toma cada captura en tema claro (CDP de WebView2).
    5. Detiene todo, borra la carpeta temporal y deja la configuración de la app como estaba.

    Requiere Node.js y un PostgreSQL instalado (o -Pgsql). No debe estar abierta CFDI Manager Escritorio.
    Si una pantalla cambia de nombres o botones, ajusta capturas.mjs; para agregar una captura, un paso más ahí y
    su referencia en CFDIManager.Web\Ayuda\Manual.md.

.EXAMPLE
    pwsh -File .\herramientas\Capturas-Manual\Capturas-Manual.ps1
#>
param(
    [string]$Pgsql
)

$ErrorActionPreference = "Stop"
$aqui = $PSScriptRoot
$raiz = (Resolve-Path (Join-Path $aqui "..\..")).Path
$destino = Join-Path $raiz "CFDIManager.Web\wwwroot\ayuda\img"
# Ruta larga (no %TEMP%, que puede venir con nombres cortos como CFDIMA~1): la app muestra la ruta larga y
# capturas.mjs la reemplaza en pantalla por C:\CFDIManagerDatos.
$trabajo = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Temp\CFDIManagerCapturas"
$web = Join-Path $trabajo "web"
$datos = Join-Path $trabajo "datos"
$demo = Join-Path $trabajo "demo"
$configuracion = Join-Path $env:LOCALAPPDATA "CFDIManagerEscritorio\configuracion.json"
$respaldoConfiguracion = Join-Path $trabajo "configuracion.json"

if (Get-Process CFDIManagerEscritorio -ErrorAction SilentlyContinue) { throw "Cierra CFDI Manager Escritorio (bandeja → Salir) antes de tomar las capturas." }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw "Falta Node.js (node) para manejar la app." }
if (-not $Pgsql) {
    $Pgsql = Get-ChildItem "$env:ProgramFiles\PostgreSQL" -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "bin\pg_ctl.exe") } |
        Sort-Object { [int]($_.Name -replace '\D', '') } -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Pgsql) { throw "No se encontró PostgreSQL; indícalo con -Pgsql." }

if (Test-Path $trabajo) { Remove-Item $trabajo -Recurse -Force }
New-Item -ItemType Directory -Force $trabajo | Out-Null

Write-Host "Compilando..."
dotnet build (Join-Path $raiz "CFDIManager.Escritorio\CFDIManager.Escritorio.csproj") -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Falló la compilación de la app de escritorio." }
dotnet publish (Join-Path $raiz "CFDIManager.Web\CFDIManager.Web.csproj") -c Release -r win-x64 --self-contained false -o $web --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación de la web." }

node (Join-Path $aqui "generar-demo.mjs") $demo

# Configuración de prueba (la del usuario se respalda y se repone al final).
if (Test-Path $configuracion) { Copy-Item $configuracion $respaldoConfiguracion }
New-Item -ItemType Directory -Force (Split-Path $configuracion) | Out-Null
@{ Modo = "Local"; CarpetaDatos = $datos; CerrarABandeja = $false; IniciarConWindows = $false
   Maximizada = $false; Ancho = 1440; Alto = 900; Izquierda = 0; Arriba = 0; Zoom = 1.0 } |
    ConvertTo-Json | Set-Content $configuracion

$password = "Capturas-" + [guid]::NewGuid().ToString("N").Substring(0, 8) + "A1"
$env:CFDI_ESCRITORIO_WEB = $web
$env:CFDI_ESCRITORIO_PGSQL = $Pgsql
$env:CFDI_ESCRITORIO_ADMIN_PASSWORD = $password
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=9333"
$env:CAPTURAS_DATOS = $datos

$app = $null
try {
    Write-Host "Arrancando la app en modo Local..."
    $app = Start-Process (Join-Path $raiz "CFDIManager.Escritorio\bin\Debug\net10.0-windows\CFDIManagerEscritorio.exe") -PassThru
    $lista = $false
    for ($i = 0; $i -lt 90 -and -not $lista; $i++) {
        Start-Sleep 2
        try { $lista = (Invoke-WebRequest http://127.0.0.1:5180/cuenta/login -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch {}
    }
    if (-not $lista) { throw "La app no arrancó; revisa $datos\Logs." }
    Start-Sleep 3

    New-Item -ItemType Directory -Force $destino | Out-Null
    node (Join-Path $aqui "capturas.mjs") $destino (Join-Path $datos "Storage\XML\EKU9003173C9") $demo
    if ($LASTEXITCODE -ne 0) { Write-Warning "Algunas capturas fallaron (ver arriba)." }
}
finally {
    if ($app -and -not $app.HasExited) { $app.Kill(); $app.WaitForExit(10000) | Out-Null }
    $cluster = Join-Path $datos "PostgreSQL"
    if (Test-Path $cluster) { & (Join-Path $Pgsql "bin\pg_ctl.exe") stop -D $cluster -m fast 2>&1 | Out-Null }
    Get-Process CFDIManager.Web -ErrorAction SilentlyContinue | Where-Object Path -like "$trabajo*" | Stop-Process -Force

    if (Test-Path $respaldoConfiguracion) { Copy-Item $respaldoConfiguracion $configuracion -Force }
    else { Remove-Item $configuracion -ErrorAction SilentlyContinue }
    foreach ($v in "CFDI_ESCRITORIO_WEB", "CFDI_ESCRITORIO_PGSQL", "CFDI_ESCRITORIO_ADMIN_PASSWORD", "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "CAPTURAS_DATOS") {
        Remove-Item "env:$v" -ErrorAction SilentlyContinue
    }
    Start-Sleep 2
    Remove-Item $trabajo -Recurse -Force -ErrorAction SilentlyContinue
}

Get-ChildItem $destino -Filter *.png | Select-Object Name, @{ n = "KB"; e = { [math]::Round($_.Length / 1KB) } } | Format-Table -AutoSize
Write-Host "Capturas en $destino. Revísalas y haz commit de las que cambiaron."
