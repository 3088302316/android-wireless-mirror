param(
    [string]$IsccPath = 'ISCC.exe',
    [string]$RuntimeDirectory = (Join-Path $PSScriptRoot 'runtime'),
    [string]$LanguageFile
)
$ErrorActionPreference = 'Stop'
$iscc = (Get-Command $IsccPath -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$release = Join-Path $PSScriptRoot '发布'
New-Item -ItemType Directory -Path $release -Force | Out-Null

$payload = & (Join-Path $PSScriptRoot 'build.ps1') -RuntimeDirectory $RuntimeDirectory
$arguments = @("/O$release", "/DPayloadDir=$payload")
if ($LanguageFile) { $arguments += '/DLanguageFile=' + (Resolve-Path -LiteralPath $LanguageFile).Path }
& $iscc @arguments (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
