param(
    [string]$RuntimeDirectory = (Join-Path $PSScriptRoot 'runtime'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework x64 C# compiler is required.' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.sha256') -Encoding UTF8
$runtimeFiles = foreach ($row in $manifest) {
    if ($row -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9.-]+)$') { throw 'Invalid runtime manifest.' }
    $expected = $Matches[1]
    $file = Join-Path $runtime $Matches[2]
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing runtime file: $file" }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected) {
        throw "Runtime file differs from the recorded build: $file"
    }
    $file
}

New-Item -ItemType Directory -Path $out -Force | Out-Null
$refs = @('System.Windows.Forms','System.Drawing','System.Runtime.Serialization','Microsoft.VisualBasic','System.Xml') |
    ForEach-Object { "/r:$_.dll" }
$refs += @('AntdUI.dll','QRCoder.dll') | ForEach-Object { '/r:' + (Join-Path $runtime $_) }
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName
$icon = Join-Path $PSScriptRoot '投屏.ico'
$manifestPath = Join-Path $PSScriptRoot 'app.manifest'
$wallpaperPath = Join-Path $PSScriptRoot 'wallpaper.png'
if (!(Test-Path -LiteralPath $wallpaperPath -PathType Leaf)) { throw 'Missing wallpaper.png.' }
# csc's /win32icon parser does not handle some non-ASCII paths reliably.
# Feed it short ASCII temporary copies of the resource files.
$resourceRoot = [IO.Path]::GetTempPath()
$buildIcon = Join-Path $resourceRoot ('AndroidWirelessMirror-' + [guid]::NewGuid().ToString('N') + '.ico')
$buildManifest = Join-Path $resourceRoot ('AndroidWirelessMirror-' + [guid]::NewGuid().ToString('N') + '.manifest')
$buildWallpaper = Join-Path $resourceRoot ('AndroidWirelessMirror-' + [guid]::NewGuid().ToString('N') + '.png')
Copy-Item -LiteralPath $icon -Destination $buildIcon -Force
Copy-Item -LiteralPath $manifestPath -Destination $buildManifest -Force
Copy-Item -LiteralPath $wallpaperPath -Destination $buildWallpaper -Force
try {
    $common = @('/nologo','/target:winexe','/platform:x64','/langversion:5','/codepage:65001','/optimize+',
        "/win32icon:$buildIcon","/win32manifest:$buildManifest","/resource:$buildWallpaper,Wallpaper.png")
    & $compiler @common @refs ('/out:' + (Join-Path $out '安卓无线投屏.exe')) @sources
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
finally {
    Remove-Item -LiteralPath $buildIcon -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $buildManifest -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $buildWallpaper -Force -ErrorAction SilentlyContinue
}
$legacyPhoneAudio = Join-Path $out '安卓无线投屏-手机出声.exe'
if (Test-Path -LiteralPath $legacyPhoneAudio) { Remove-Item -LiteralPath $legacyPhoneAudio -Force }

foreach ($file in $runtimeFiles) { Copy-Item -LiteralPath $file -Destination $out -Force }
foreach ($name in @('投屏.ico','使用说明.txt','LICENSE.txt','AntdUI-LICENSE.txt','QRCoder-LICENSE.txt','Lucide-LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $out -Force
}
Write-Output $out
