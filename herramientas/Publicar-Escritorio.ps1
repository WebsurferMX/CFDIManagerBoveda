<#
.SYNOPSIS
    Arma CFDI Manager Escritorio y su instalador (Velopack): la app WPF, la web (carpeta web) y PostgreSQL portátil
    (carpeta pgsql, tomada de un PostgreSQL instalado en este equipo y recortada a lo que usa la app).

    El instalador no lleva .NET ni WebView2: si faltan, los baja e instala Setup (.NET 10 Desktop y ASP.NET Core,
    WebView2 y Visual C++ 2015-2022, que necesita PostgreSQL).

.PARAMETER Version
    Versión a publicar (p. ej. 0.2.0). Por omisión, la <Version> de CFDIManager.Escritorio.csproj.

.PARAMETER Subir
    Publica la versión en GitHub Releases (WebsurferMX/CFDIManagerBoveda) con la etiqueta escritorio-v{Version};
    de ahí se actualizan las apps instaladas. Usa el acceso a GitHub con el que se hace git push en este equipo, o el
    token de la variable GITHUB_TOKEN si está definida.

.EXAMPLE
    pwsh -File .\herramientas\Publicar-Escritorio.ps1
    pwsh -File .\herramientas\Publicar-Escritorio.ps1 -Version 0.2.0 -Subir
#>
param(
    [string]$Version,
    [string]$Pgsql,
    [switch]$Subir
)

$ErrorActionPreference = "Stop"
$raiz = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$proyecto = Join-Path $raiz "CFDIManager.Escritorio\CFDIManager.Escritorio.csproj"
$carpeta = Join-Path $raiz "publish\Escritorio"
$versiones = Join-Path $raiz "publish\Releases"
$repositorio = "https://github.com/WebsurferMX/CFDIManagerBoveda"

if (-not $Version) {
    $Version = ([xml](Get-Content $proyecto)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versión no válida: '$Version' (se espera 0.2.0)." }

if (-not $Pgsql) {
    $Pgsql = Get-ChildItem "$env:ProgramFiles\PostgreSQL" -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "bin\pg_ctl.exe") } |
        Sort-Object { [int]($_.Name -replace '\D', '') } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Pgsql -or -not (Test-Path (Join-Path $Pgsql "bin\initdb.exe"))) { throw "No se encontró PostgreSQL; indícalo con -Pgsql." }
$token = $env:GITHUB_TOKEN
if ($Subir -and -not $token) {
    # Sin GITHUB_TOKEN: el mismo acceso con el que se hace git push (Administrador de credenciales de Git), sin preguntar.
    $env:GIT_TERMINAL_PROMPT = "0"
    $env:GCM_INTERACTIVE = "never"
    $credencial = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
    $token = ($credencial | Where-Object { $_ -like "password=*" } | Select-Object -First 1) -replace '^password=', ''
}
if ($Subir -and -not $token) {
    throw "No hay acceso a GitHub: haz un git push desde este equipo o define GITHUB_TOKEN con un token que pueda escribir releases."
}

# GitHub no deja publicar dos veces la misma versión: se revisa antes de compilar.
if ($Subir) {
    $etiqueta = "escritorio-v$Version"
    try {
        Invoke-RestMethod "https://api.github.com/repos/WebsurferMX/CFDIManagerBoveda/releases/tags/$etiqueta" `
            -Headers @{ Authorization = "Bearer $token"; "User-Agent" = "Publicar-Escritorio" } | Out-Null
        throw "La versión $Version ya está publicada ($etiqueta). Sube el número en <Version> de CFDIManager.Escritorio.csproj (p. ej. a la siguiente) o usa -Version."
    }
    catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
        # 404: la versión no existe todavía, se puede publicar.
    }
}

Write-Host "CFDI Manager Escritorio $Version"

# ---------- app y web (solo Windows x64, sin .NET dentro) ----------
if (Test-Path $carpeta) { Remove-Item $carpeta -Recurse -Force }

Write-Host "Publicando la app..."
dotnet publish $proyecto -c Release -r win-x64 --self-contained false -p:Version=$Version -o $carpeta --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación de CFDIManager.Escritorio." }

Write-Host "Publicando la web..."
dotnet publish (Join-Path $raiz "CFDIManager.Web\CFDIManager.Web.csproj") -c Release -r win-x64 --self-contained false `
    -o (Join-Path $carpeta "web") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación de CFDIManager.Web." }

# ---------- PostgreSQL portátil ----------
Write-Host "Copiando PostgreSQL desde $Pgsql..."
$destinoPg = Join-Path $carpeta "pgsql"

# bin: los programas que usa la app (y los de diagnóstico) con todas las DLL, menos wxWidgets (de StackBuilder).
$programas = "postgres.exe", "initdb.exe", "pg_ctl.exe", "pg_dump.exe", "pg_restore.exe", "psql.exe",
             "pg_isready.exe", "pg_controldata.exe", "pg_resetwal.exe"
New-Item -ItemType Directory -Force (Join-Path $destinoPg "bin") | Out-Null
Get-ChildItem (Join-Path $Pgsql "bin") -File |
    Where-Object { ($_.Extension -eq ".dll" -and $_.Name -notlike "wx*") -or $programas -contains $_.Name } |
    Copy-Item -Destination (Join-Path $destinoPg "bin")

# lib: solo las DLL que carga el servidor (sin bibliotecas de enlace .lib ni pgxs).
robocopy (Join-Path $Pgsql "lib") (Join-Path $destinoPg "lib") *.dll /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Falló la copia de pgsql\lib." }

# share: todo menos las traducciones; de esas, solo español.
robocopy (Join-Path $Pgsql "share") (Join-Path $destinoPg "share") /E /XD locale doc /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Falló la copia de pgsql\share." }
$espanol = Join-Path $Pgsql "share\locale\es"
if (Test-Path $espanol) {
    robocopy $espanol (Join-Path $destinoPg "share\locale\es") /E /NFL /NDL /NJH /NJS /NP | Out-Null
}

foreach ($requerido in "bin\postgres.exe", "bin\initdb.exe", "bin\pg_ctl.exe", "bin\pg_dump.exe", "lib\plpgsql.dll", "share\postgres.bki") {
    if (-not (Test-Path (Join-Path $destinoPg $requerido))) { throw "Falta pgsql\$requerido en el paquete." }
}

$mb = [math]::Round(((Get-ChildItem $carpeta -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 0)
Write-Host "Carpeta lista: $carpeta ($mb MB sin comprimir)"

# ---------- instalador y paquetes de actualización (Velopack) ----------
Push-Location $raiz
try {
    dotnet tool restore | Out-Null

    # Se empieza de cero y se baja de GitHub solo lo ya publicado: paquetes armados antes en este equipo (pruebas)
    # no deben mezclarse con lo publicado. Con la versión anterior a la mano, Velopack arma además el delta.
    if (Test-Path $versiones) { Remove-Item $versiones -Recurse -Force }
    New-Item -ItemType Directory -Force $versiones | Out-Null
    dotnet vpk download github --repoUrl $repositorio -o $versiones 2>$null | Out-Null

    Write-Host "Armando el instalador..."
    dotnet vpk pack `
        --packId CFDIManager.Escritorio `
        --packVersion $Version `
        --runtime win-x64 `
        --packDir $carpeta `
        --mainExe CFDIManagerEscritorio.exe `
        --packTitle "CFDI Manager" `
        --packAuthors "NickSoft Inc" `
        --icon (Join-Path $raiz "CFDIManager.Escritorio\app.ico") `
        --framework "net10-x64-desktop,net10-x64-aspnetcore,webview2,vcredist143-x64" `
        --outputDir $versiones
    if ($LASTEXITCODE -ne 0) { throw "Falló vpk pack." }

    $setup = Get-ChildItem $versiones -Filter "*Setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Write-Host ("Instalador: {0} ({1:N0} MB)" -f $setup.FullName, ($setup.Length / 1MB))

    if ($Subir) {
        Write-Host "Publicando en GitHub Releases (escritorio-v$Version)..."
        dotnet vpk upload github --repoUrl $repositorio --token $token -o $versiones `
            --publish --releaseName "CFDI Manager Escritorio $Version" --tag "escritorio-v$Version"
        if ($LASTEXITCODE -ne 0) { throw "Falló la publicación en GitHub." }
        Write-Host "Publicada: $repositorio/releases/tag/escritorio-v$Version"
    }
}
finally {
    Pop-Location
}
