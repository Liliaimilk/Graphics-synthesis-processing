$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$bin = 'D:\C#\WindowsFormsApp1\WindowsFormsApp1\bin\Debug'
Set-Location $bin
[System.Reflection.Assembly]::LoadFrom((Join-Path $bin 'Magick.NET.Core.dll')) | Out-Null
[System.Reflection.Assembly]::LoadFrom((Join-Path $bin 'Magick.NET-Q8-AnyCPU.dll')) | Out-Null
[System.Reflection.Assembly]::LoadFrom((Join-Path $bin 'WindowsFormsApp1.exe')) | Out-Null
$inputPath = (Get-ChildItem -Path 'D:\matrials\2-picture' -Filter '*.tif' | Select-Object -First 1 -ExpandProperty FullName)
Write-Output "INPUT=$inputPath"
try {
    $result = [WindowsFormsApp1.PSDAnalyzer]::FlattenTiffToSingleLayerTiff($inputPath, $null)
    Write-Output "OUTPUT=$result"
}
catch {
    $ex = $_.Exception
    $level = 0
    while ($ex -ne $null) {
        Write-Output ("EX[{0}] {1}" -f $level, $ex.GetType().FullName)
        Write-Output $ex.Message
        Write-Output $ex.ToString()
        $ex = $ex.InnerException
        $level++
    }
    exit 1
}
