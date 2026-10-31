Add-Type -AssemblyName System.Windows.Forms

# Seleccionar carpeta
$FolderBrowser = New-Object System.Windows.Forms.FolderBrowserDialog
$FolderBrowser.Description = "Seleccione la carpeta con los XML"
$FolderBrowser.SelectedPath = (Get-Location).Path

if ($FolderBrowser.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
    exit
}

$Ruta = $FolderBrowser.SelectedPath

# Nombres por serie
$MapaNombres = @{
    '61A'  = 'CLIENTE MOSTRADOR ATIZAPAN'
    '62A'  = 'CLIENTE MOSTRADOR NICOLAS ROMERO'
    '63E'  = 'CLIENTE MOSTRADOR EXPO'
}

# Cambio de series
$MapaSeries = @{
    '61A'  = '61AA'
    '62A'  = '62AA'
    '63E'  = '63EE'
}

# Log
$Log = Join-Path $Ruta "Cambios.log"

"======== INICIO $(Get-Date) ========" | Out-File $Log -Encoding UTF8

Get-ChildItem -Path $Ruta -Filter *.xml -Recurse | ForEach-Object {

    try {

        $Archivo = $_.FullName
        $contenido = Get-Content $Archivo -Raw -Encoding UTF8

        $serie = ([regex]::Match(
            $contenido,
            'Serie="([^"]+)"'
        )).Groups[1].Value

        # Solo RFC genérico
        if (
            $MapaNombres.ContainsKey($serie) -and
            $contenido -match 'Rfc="XAXX010101000"'
        ) {

            $nombreNuevo = $MapaNombres[$serie]

            $contenido = [regex]::Replace(
                $contenido,
                '(Nombre=")[^"]*(" Rfc="XAXX010101000")',
                ('$1' + $nombreNuevo + '$2')
            )
        }

        # Cambiar serie
        if ($MapaSeries.ContainsKey($serie)) {

            $serieNueva = $MapaSeries[$serie]

            $contenido = $contenido -replace (
                'Serie="' + [regex]::Escape($serie) + '"'
            ), (
                'Serie="' + $serieNueva + '"'
            )
        }

        Set-Content -Path $Archivo -Value $contenido -Encoding UTF8

        Add-Content $Log (
            "{0} | OK | {1} | Serie: {2}" -f `
            (Get-Date -Format "yyyy-MM-dd HH:mm:ss"),
            $_.Name,
            $serie
        )

        Write-Host "OK -> $($_.Name)" -ForegroundColor Green
    }
    catch {

        Add-Content $Log (
            "{0} | ERROR | {1} | {2}" -f `
            (Get-Date -Format "yyyy-MM-dd HH:mm:ss"),
            $_.Name,
            $_.Exception.Message
        )

        Write-Host "ERROR -> $($_.Name)" -ForegroundColor Red
    }
}

Add-Content $Log "======== FIN $(Get-Date) ========"

[System.Windows.Forms.MessageBox]::Show(
    "Proceso terminado.`n`nLog generado:`n$Log",
    "XML Actualizados",
    [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Information
)
