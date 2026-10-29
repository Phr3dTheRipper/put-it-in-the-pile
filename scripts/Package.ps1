$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskExe = Join-Path $taskRoot 'PutItInThePile.exe'
if (!(Test-Path -LiteralPath $taskExe)) { throw 'Build the app with Build.ps1 first.' }
$taskDist = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Force -Path $taskDist | Out-Null
$taskArchivePath = Join-Path $taskDist 'PutItInThePile-Windows.zip'
$taskStage = Join-Path ([IO.Path]::GetTempPath()) ('PutItInThePile-' + [Guid]::NewGuid().ToString('N'))
$taskPackageRoot = Join-Path $taskStage 'PutItInThePile'
New-Item -ItemType Directory -Force -Path $taskPackageRoot | Out-Null

# An explicit file list keeps local settings, diagnostics, and account data out.
foreach ($taskFile in @('PutItInThePile.exe', 'Build.ps1', 'README.txt', 'README.md', 'LICENSE', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $taskFile) -Destination $taskPackageRoot
}
foreach ($taskDir in @('src', 'scripts', 'docs')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $taskDir) -Destination $taskPackageRoot -Recurse
}
Compress-Archive -LiteralPath $taskPackageRoot -DestinationPath $taskArchivePath -Force
$taskDigest = (Get-FileHash -LiteralPath $taskArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $taskDist 'SHA256SUMS.txt'), ($taskDigest + '  PutItInThePile-Windows.zip' + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
Write-Host ('Packaged ' + $taskArchivePath)
Write-Host ('SHA-256: ' + $taskDigest)
