<#
.SYNOPSIS
    Revisa si este equipo está listo para correr CFDI Manager: .NET, PostgreSQL, contraseña,
    base de datos, carpeta Storage y rutas de los XML guardadas en la base.

.DESCRIPTION
    Solo lee; no cambia nada. Deja el resultado en pantalla y en herramientas\Diagnostico-<equipo>.txt
    para poder compartirlo. Ejecutar desde la carpeta del proyecto:

        powershell -ExecutionPolicy Bypass -File .\herramientas\Diagnostico-Equipo.ps1
#>

$ErrorActionPreference = 'Continue'
$raiz = Split-Path -Parent $PSScriptRoot
$web = Join-Path $raiz 'CFDIManager.Web'
$reporte = Join-Path $PSScriptRoot "Diagnostico-$env:COMPUTERNAME.txt"
$lineas = New-Object System.Collections.Generic.List[string]
$pendientes = New-Object System.Collections.Generic.List[string]

function Escribir([string]$texto, [string]$color = 'Gray') {
    Write-Host $texto -ForegroundColor $color
    $lineas.Add($texto)
}

function Bien([string]$texto) { Escribir "  [OK]    $texto" 'Green' }
function Aviso([string]$texto) { Escribir "  [AVISO] $texto" 'Yellow' }
function Mal([string]$texto, [string]$pendiente) {
    Escribir "  [FALTA] $texto" 'Red'
    if ($pendiente) { $pendientes.Add($pendiente) }
}
function Titulo([string]$texto) { Escribir ''; Escribir "== $texto ==" 'Cyan' }

Escribir "Diagnóstico CFDI Manager · $env:COMPUTERNAME · usuario $env:USERNAME · $(Get-Date -Format 'yyyy-MM-dd HH:mm')" 'White'
Escribir "Carpeta del proyecto: $raiz"

# ---------- .NET ----------
Titulo '.NET'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Mal 'No está instalado dotnet.' 'Instalar el SDK de .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0'
} else {
    $sdks = @(& dotnet --list-sdks 2>$null)
    $sdk10 = @($sdks -match '^10\.')
    if ($sdk10.Count -gt 0) {
        Bien "SDK de .NET 10: $($sdk10[-1].Split(' ')[0])"
    } else {
        Mal "Hay dotnet pero no el SDK 10 (SDKs: $($sdks -join ', '))." 'Instalar el SDK de .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0'
    }
}

# ---------- PostgreSQL ----------
Titulo 'PostgreSQL'
$psql = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' -ErrorAction SilentlyContinue |
    Sort-Object { [int]($_.FullName -replace '.*PostgreSQL\\(\d+).*', '$1') } | Select-Object -Last 1
if (-not $psql) {
    Mal 'No se encontró PostgreSQL en C:\Program Files\PostgreSQL.' 'Instalar PostgreSQL 18: https://www.postgresql.org/download/windows/'
} else {
    $version = [int]($psql.FullName -replace '.*PostgreSQL\\(\d+).*', '$1')
    if ($version -ge 18) { Bien "PostgreSQL $version ($($psql.DirectoryName))" }
    else { Aviso "PostgreSQL ${version}: el respaldo viene de la versión 18; pg_restore de una versión menor puede fallar." }

    $servicio = Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($servicio -and $servicio.Status -eq 'Running') { Bien "Servicio $($servicio.Name) en ejecución" }
    elseif ($servicio) { Mal "El servicio $($servicio.Name) está $($servicio.Status)." "Iniciar el servicio $($servicio.Name) (services.msc)." }
    else { Aviso 'No se encontró el servicio de PostgreSQL.' }

    $puerto = Test-NetConnection -ComputerName localhost -Port 5432 -WarningAction SilentlyContinue -InformationLevel Quiet
    if ($puerto) { Bien 'Puerto 5432 responde' } else { Mal 'El puerto 5432 no responde.' 'Revisar que PostgreSQL escuche en el puerto 5432.' }
}

# ---------- Contraseña ----------
Titulo 'Variable CFDI_POSTGRES_PASSWORD'
$password = $null
foreach ($ambito in 'Process', 'User', 'Machine') {
    $valor = [Environment]::GetEnvironmentVariable('CFDI_POSTGRES_PASSWORD', $ambito)
    if ($valor) { $password = $valor; Bien "Definida (ámbito $ambito)"; break }
}
if (-not $password) {
    Mal 'No está definida.' "Crearla: [Environment]::SetEnvironmentVariable('CFDI_POSTGRES_PASSWORD', 'contraseña', 'User') y volver a abrir sesión."
}

# ---------- Base de datos ----------
Titulo 'Base de datos cfdi_manager'
$conBase = $false
if ($psql -and $password) {
    $env:PGPASSWORD = $password

    function Consultar([string]$sql) {
        & $psql.FullName -h localhost -U postgres -d cfdi_manager -w -At -F '|' -c $sql 2>&1
    }

    $prueba = Consultar 'SELECT 1;'
    if ($LASTEXITCODE -ne 0) {
        if ("$prueba" -match 'does not exist|no existe') {
            Mal 'La base cfdi_manager no existe.' 'Restaurar el respaldo: createdb cfdi_manager y pg_restore -d cfdi_manager cfdi_manager.backup'
        } elseif ("$prueba" -match 'password|contraseña') {
            Mal 'PostgreSQL rechazó la contraseña de CFDI_POSTGRES_PASSWORD.' 'Corregir CFDI_POSTGRES_PASSWORD con la contraseña del usuario postgres.'
        } else {
            Mal "No se pudo conectar: $prueba" 'Revisar la conexión a PostgreSQL.'
        }
    } else {
        $conBase = $true
        Bien "Conexión correcta · tamaño $(Consultar "SELECT pg_size_pretty(pg_database_size('cfdi_manager'));")"
        Escribir "  Empresas: $(Consultar 'SELECT COUNT(*) FROM empresas;') · CFDI: $(Consultar 'SELECT COUNT(*) FROM cfdi;') · Usuarios: $(Consultar 'SELECT COUNT(*) FROM usuarios;')"
    }
}

# ---------- Storage ----------
Titulo 'Carpeta Storage'
$storage = Join-Path $web 'Storage'
if (-not (Test-Path $storage)) {
    Mal "No existe $storage" 'Copiar la carpeta Storage del equipo original a CFDIManager.Web\Storage.'
} else {
    $bytes = (Get-ChildItem $storage -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    Bien ("Existe · {0:N0} MB" -f ($bytes / 1MB))
    foreach ($sub in 'XML', 'Certificados') {
        if (Test-Path (Join-Path $storage $sub)) { Bien "Storage\$sub" } else { Aviso "No existe Storage\$sub" }
    }
    if (Test-Path (Join-Path $storage 'Llaves')) {
        Aviso 'Existe Storage\Llaves: si se copió de otro equipo no sirve aquí (CIEC, SMTP y SINUBE no se podrán leer). Bórrala para que se creen llaves nuevas y vuelve a capturar esas contraseñas.'
    }
}

# ---------- Rutas guardadas en la base ----------
Titulo 'Rutas de los XML en la base'
$prefijoNuevo = "$web\"
if ($conBase) {
    $filas = Consultar "SELECT COALESCE(NULLIF(left(ruta_xml, strpos(ruta_xml, '\Storage\')), ''), '(otra)') , ruta_xml FROM cfdi WHERE COALESCE(ruta_xml, '') <> '';"
    $grupos = $filas | ForEach-Object { $p = $_.Split('|', 2); [pscustomobject]@{ Prefijo = $p[0]; Ruta = $p[1] } } | Group-Object Prefijo

    $faltan = 0
    foreach ($g in $grupos | Sort-Object Count -Descending) {
        $existen = ($g.Group | Where-Object { Test-Path -LiteralPath $_.Ruta }).Count
        $faltan += $g.Count - $existen
        $texto = "{0}  ->  {1:N0} XML, {2:N0} se encuentran en disco" -f $g.Name, $g.Count, $existen
        if ($existen -eq $g.Count) { Bien $texto } else { Aviso $texto }
    }

    $ajenos = $grupos | Where-Object { $_.Name -ne $prefijoNuevo -and $_.Name -ne '(otra)' }
    if ($ajenos) {
        $pendientes.Add("Corregir las rutas de la base al prefijo de este equipo ($prefijoNuevo): ver el SQL al final del reporte.")
    }
    if ($faltan -gt 0) {
        Aviso ("{0:N0} XML de la base no se encuentran en disco." -f $faltan)
    }
}

# ---------- Puerto de la app ----------
Titulo 'Puerto 5000 de la app'
$ocupado = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue
if ($ocupado) { Aviso "El puerto 5000 ya está en uso (proceso $($ocupado[0].OwningProcess))." } else { Bien 'Libre' }

# ---------- Resumen ----------
Titulo 'Pendientes'
if ($pendientes.Count -eq 0) {
    Escribir '  Nada pendiente. Arrancar con:' 'Green'
    Escribir '    dotnet run --project CFDIManager.Web --urls "http://0.0.0.0:5000"'
} else {
    $i = 1
    foreach ($p in $pendientes) { Escribir "  $i. $p" 'Yellow'; $i++ }
}

if ($conBase -and $ajenos) {
    Escribir ''
    Escribir '-- SQL para corregir las rutas (revisarlo y ejecutarlo en pgAdmin o psql):' 'Cyan'
    $viejos = ($ajenos | ForEach-Object { "    '" + $_.Name.Replace("'", "''") + "'" }) -join ",`n"
    Escribir @"
DO `$`$
DECLARE
  nuevo text := '$($prefijoNuevo.Replace("'", "''"))';
  viejo text;
  c record;
BEGIN
  FOREACH viejo IN ARRAY ARRAY[
$viejos]
  LOOP
    FOR c IN SELECT * FROM (VALUES
      ('cfdi','ruta_xml'), ('archivos_cfdi','ruta_archivo'), ('sat_descargas','xml_path'),
      ('empresas','ruta_xml'), ('empresas','ruta_cer'), ('empresas','ruta_key'),
      ('empresas_sat','ruta_cer'), ('empresas_sat','ruta_key'), ('storage_archivos','ruta'),
      ('sat_paquetes','archivo_zip'), ('cfdi_importaciones','archivo'), ('sinube_envios','archivo')
    ) AS t(tabla, columna)
    LOOP
      EXECUTE format('UPDATE %I SET %I = %L || substr(%I, %s) WHERE starts_with(%I, %L)',
        c.tabla, c.columna, nuevo, c.columna, length(viejo) + 1, c.columna, viejo);
    END LOOP;
  END LOOP;
END `$`$;
"@
}

$lineas | Set-Content -Path $reporte -Encoding UTF8
Write-Host ''
Write-Host "Reporte guardado en $reporte" -ForegroundColor White
