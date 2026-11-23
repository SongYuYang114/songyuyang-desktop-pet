param([string]$Destination = (Join-Path $PSScriptRoot '真人桌宠增强版.exe'))
$ErrorActionPreference = 'Stop'
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpfPath = Join-Path $frameworkPath 'WPF'
$compilerPath = Join-Path $frameworkPath 'csc.exe'
$resourceOptions = @('coat','home','scarf','festival','extra','rest') | ForEach-Object { "/resource:$PSScriptRoot\$_.png,$_.png" }
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ "/out:$Destination" "/win32manifest:$PSScriptRoot\app.manifest" $resourceOptions "/r:$wpfPath\PresentationFramework.dll" "/r:$wpfPath\PresentationCore.dll" "/r:$wpfPath\WindowsBase.dll" /r:System.Xaml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll /r:System.Core.dll "$PSScriptRoot\Models.cs" "$PSScriptRoot\Atlas.cs" "$PSScriptRoot\AppHost.cs" "$PSScriptRoot\PetWindow.cs" "$PSScriptRoot\ControlPanel.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
