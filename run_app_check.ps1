$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$exe = 'D:\C#\WindowsFormsApp1\WindowsFormsApp1\bin\Debug\WindowsFormsApp1.exe'
Get-Process WindowsFormsApp1 -ErrorAction SilentlyContinue | Stop-Process -Force
$before = Get-Date
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
if ($p.HasExited) {
    Write-Output ("EXITED=" + $p.ExitCode)
} else {
    Write-Output ("RUNNING PID=" + $p.Id)
    Stop-Process -Id $p.Id -Force
}
Write-Output '---EVENTS---'
Get-WinEvent -LogName Application -MaxEvents 50 |
    Where-Object {
        $_.TimeCreated -gt $before.AddSeconds(-2) -and
        ($_.ProviderName -eq '.NET Runtime' -or $_.ProviderName -eq 'Application Error' -or $_.ProviderName -eq 'Windows Error Reporting')
    } |
    Select-Object TimeCreated, ProviderName, Id, LevelDisplayName, Message |
    Format-List
