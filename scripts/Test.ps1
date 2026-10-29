$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskExe = Join-Path $taskRoot 'PutItInThePile.exe'
if (!(Test-Path -LiteralPath $taskExe)) { throw 'Build the app with Build.ps1 first.' }
$taskResultDir = Join-Path $taskRoot '.test-results'
New-Item -ItemType Directory -Force -Path $taskResultDir | Out-Null
$taskReport = Join-Path $taskResultDir ('parser-' + [Guid]::NewGuid().ToString('N') + '.txt')
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--self-test', ('"' + $taskReport + '"')) -WindowStyle Hidden -PassThru
if (!$taskProcess.WaitForExit(30000)) {
    Stop-Process -Id $taskProcess.Id -ErrorAction SilentlyContinue
    throw 'Parser checks timed out after 30 seconds.'
}
if (!(Test-Path -LiteralPath $taskReport)) { throw 'Parser checks did not produce a report.' }
$taskResults = @(Get-Content -LiteralPath $taskReport)
$taskResults | Write-Output
if ($taskProcess.ExitCode -ne 0 -or $taskResults.Count -ne 6 -or @($taskResults | Where-Object { $_ -notmatch '^PASS ' }).Count -gt 0) {
    throw 'Parser checks failed.'
}
Write-Host 'All six parser checks passed. The bar remains remarkably horizontal.'
