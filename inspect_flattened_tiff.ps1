Add-Type -AssemblyName System.Drawing
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$path = 'D:\matrials\2-picture\粉色海浪玫瑰_flattened.tif'
$img = [System.Drawing.Image]::FromFile($path)
try {
    Write-Output ("PATH=" + $path)
    Write-Output ("SIZE=" + $img.Width + "x" + $img.Height)
    Write-Output ("FRAMES=" + $img.GetFrameCount([System.Drawing.Imaging.FrameDimension]::Page))
}
finally {
    $img.Dispose()
}
