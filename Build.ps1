$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$taskReferences = @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $taskFramework $_) }
Push-Location -LiteralPath $PSScriptRoot
try {
    & $taskCompiler /nologo /target:winexe /optimize+ /utf8output '/out:PutItInThePile.exe' '/resource:src\Main.xaml,Main.xaml' /reference:System.Xaml.dll /reference:System.Web.Extensions.dll @taskReferences 'src\App.cs' 'src\AssemblyInfo.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Write-Host 'Built PutItInThePile.exe. Another historic achievement.'
} finally { Pop-Location }
