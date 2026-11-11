param(
    [string]$IsccPath = 'ISCC.exe',
    [string]$RuntimeDirectory = (Join-Path $PSScriptRoot 'runtime'),
    [string]$LanguageFile,
    [string]$ReleaseDirectory = (Join-Path $PSScriptRoot '发布')
)
$ErrorActionPreference = 'Stop'
$iscc = (Get-Command $IsccPath -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$release = [IO.Path]::GetFullPath($ReleaseDirectory)
New-Item -ItemType Directory -Path $release -Force | Out-Null
$tempRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path
$staging = Join-Path $tempRoot ('AndroidWirelessMirrorBuild-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $payload = & (Join-Path $PSScriptRoot 'build.ps1') -RuntimeDirectory $RuntimeDirectory -OutputDirectory (Join-Path $staging '安卓无线投屏便携版')
    $payload = @($payload) | Select-Object -Last 1
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archiveName = '安卓无线投屏便携版-Windows-x64.zip'
    $archive = Join-Path $staging $archiveName
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive, [IO.Compression.CompressionLevel]::Optimal, $true)
    $arguments = @("/O$release", "/DPayloadDir=$payload")
    if ($LanguageFile) { $arguments += '/DLanguageFile=' + (Resolve-Path -LiteralPath $LanguageFile).Path }
    & $iscc @arguments (Join-Path $PSScriptRoot 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    Move-Item -LiteralPath $archive -Destination (Join-Path $release $archiveName) -Force
} finally {
    $directory = Get-Item -LiteralPath $staging
    if ($directory.FullName -eq $staging -and $directory.Parent.FullName -eq $tempRoot -and
        -not ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force
    }
}
