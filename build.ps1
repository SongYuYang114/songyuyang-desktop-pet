param([string]$Destination = (Join-Path $PSScriptRoot 'dist\真人桌宠增强版.exe'))
$ErrorActionPreference = 'Stop'
$destinationFolder = Split-Path -Parent ([IO.Path]::GetFullPath($Destination))
New-Item -ItemType Directory -Path $destinationFolder -Force | Out-Null
& (Join-Path $PSScriptRoot 'src\build.ps1') -Destination $Destination
