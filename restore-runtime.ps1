param(
    [Parameter(Mandatory = $true)]
    [string]$Archive,
    [string]$RuntimeDirectory = (Join-Path $PSScriptRoot 'runtime')
)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Archive).Path)
try {
    $missing = foreach ($row in Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.sha256') -Encoding UTF8) {
        if ($row -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9.-]+)$') { throw 'Invalid runtime manifest.' }
        $expected = $Matches[1]
        $name = $Matches[2]
        $destination = Join-Path $RuntimeDirectory $name
        if (Test-Path -LiteralPath $destination) {
            if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) {
                throw "Existing runtime file differs from the recorded build: $destination"
            }
            continue
        }

        $entries = @($zip.Entries | Where-Object { [IO.Path]::GetFileName($_.FullName) -eq $name })
        if ($entries.Count -ne 1) { throw "Expected one archive entry for $name." }
        $stream = $entries[0].Open()
        try {
            if ((Get-FileHash -InputStream $stream -Algorithm SHA256).Hash -ne $expected) {
                throw "Archive entry differs from the recorded build: $name"
            }
        } finally {
            $stream.Dispose()
        }
        [pscustomobject]@{ Entry = $entries[0]; Destination = $destination }
    }

    New-Item -ItemType Directory -Path $RuntimeDirectory -Force | Out-Null
    foreach ($file in $missing) {
        [IO.Compression.ZipFileExtensions]::ExtractToFile($file.Entry, $file.Destination, $false)
    }
} finally {
    $zip.Dispose()
}
Write-Output (Resolve-Path -LiteralPath $RuntimeDirectory).Path
