$ErrorActionPreference = 'Stop'
$paths = @(
  'D:\C#\WindowsFormsApp1\.vs',
  'D:\C#\WindowsFormsApp1\WindowsFormsApp1\bin',
  'D:\C#\WindowsFormsApp1\WindowsFormsApp1\obj'
)
Get-Process WindowsFormsApp1 -ErrorAction SilentlyContinue | Stop-Process -Force
foreach ($path in $paths) {
  if (Test-Path $path) {
    Remove-Item -Recurse -Force $path
  }
}
Write-Output 'cache cleared'
