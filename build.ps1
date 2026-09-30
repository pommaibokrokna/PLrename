param([string]$OutputPath = (Join-Path $PSScriptRoot 'PLrename.exe'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found.' }
$logoPath = Join-Path $PSScriptRoot 'PLstu_small.png'
$resourceArgs = @()
if (Test-Path -LiteralPath $logoPath) { $resourceArgs += "/resource:$logoPath,BatchNumberRenamer.Logo.png" }
& $compiler /nologo /target:winexe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "/win32icon:$PSScriptRoot\App.ico" @resourceArgs "/out:$OutputPath" "$PSScriptRoot\BatchNumberRenamer.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
