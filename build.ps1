param([string]$OutputName='TinyClip.exe')
$ErrorActionPreference='Stop'
$Framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$Compiler=Join-Path $Framework 'csc.exe'
if(-not(Test-Path -LiteralPath $Compiler)){throw 'Windows .NET Framework compiler not found.'}
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'build') | Out-Null
$References=@('System.dll','System.Core.dll','System.Security.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Xaml.dll') | ForEach-Object { '/reference:'+(Join-Path $Framework $_) }
$References+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:'+(Join-Path "$Framework\WPF" $_) }
$Sources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object {$_.FullName})
& $Compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /codepage:65001 /win32manifest:"$PSScriptRoot\app.manifest" /out:"$PSScriptRoot\build\$OutputName" @References @Sources
if($LASTEXITCODE -ne 0){throw 'C# compilation failed.'}
Write-Output "EXE: $PSScriptRoot\build\$OutputName"
