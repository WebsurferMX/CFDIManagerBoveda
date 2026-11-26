<#
.SYNOPSIS
    Arma la carpeta de CFDI Manager Escritorio lista para instalar: la app WPF, la web (carpeta web) y PostgreSQL
    portátil (carpeta pgsql, tomada de un PostgreSQL instalado en este equipo).

.EXAMPLE
    pwsh -File .\herramientas\Publicar-Escritorio.ps1
    pwsh -File .\herramientas\Publicar-Escritorio.ps1 -Salida D:\CFDIManagerEscritorio -Pgsql "C:\Program Files\PostgreSQL\18"
#>
param(
    [string]$Salida = (Join-Path $PSScriptRoot "..\publish\Escritorio"),
    [string]$Pgsql
)

$ErrorActionPreference = "Stop"
$raiz = Resolve-Path (Join-Path $PSScriptRoot "..")
$Salida = [IO.Path]::GetFullPath($Salida)

if (-not $Pgsql) {
    $Pgsql = Get-ChildItem "$env:ProgramFiles\PostgreSQL" -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "bin\pg_ctl.exe") } |
        Sort-Object { [int]($_.Name -replace '\D', '') } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Pgsql -or -not (Test-Path (Join-Path $Pgsql "bin\initdb.exe"))) {
    throw "No se encontró PostgreSQL; indícalo con -Pgsql."
}

Write-Host "Publicando la app de escritorio en $Salida..."
dotnet publish (Join-Path $raiz "CFDIManager.Escritorio\CFDIManager.Escritorio.csproj") -c Release -o $Salida --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación de CFDIManager.Escritorio." }

Write-Host "Publicando la web en $Salida\web..."
dotnet publish (Join-Path $raiz "CFDIManager.Web\CFDIManager.Web.csproj") -c Release -o (Join-Path $Salida "web") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación de CFDIManager.Web." }

Write-Host "Copiando PostgreSQL desde $Pgsql..."
foreach ($carpeta in "bin", "lib", "share") {
    robocopy (Join-Path $Pgsql $carpeta) (Join-Path $Salida "pgsql\$carpeta") /MIR /NFL /NDL /NJH /NJS /NP /XF *.pdb | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Falló la copia de PostgreSQL ($carpeta)." }
}

$mb = [math]::Round(((Get-ChildItem $Salida -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 0)
Write-Host "Listo: $Salida ($mb MB). Ejecutable: CFDIManagerEscritorio.exe"
