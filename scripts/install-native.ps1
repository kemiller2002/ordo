param(
    [string]$Version,
    [string]$InstallBase = $(if ($env:ECHELON_HOME) { $env:ECHELON_HOME } else { Join-Path $HOME ".echelon" })
)

$ErrorActionPreference = "Stop"
$repo = "kemiller2002/ordo"

if (-not $Version) {
    $release = Invoke-RestMethod -Headers @{ "User-Agent" = "echelon-installer" } -Uri "https://api.github.com/repos/$repo/releases/latest"
    $Version = $release.tag_name -replace '^v', ''
}

$asset = "ordo-win-x64.zip"
$baseUrl = "https://github.com/$repo/releases/download/v$Version"
$temp = Join-Path ([IO.Path]::GetTempPath()) ("ordo-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    $archive = Join-Path $temp $asset
    $checksums = Join-Path $temp "checksums.txt"
    Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/$asset" -OutFile $archive
    Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/checksums.txt" -OutFile $checksums

    $line = Get-Content $checksums | Where-Object { $_ -match ("\s" + [Regex]::Escape($asset) + "$") } | Select-Object -First 1
    if (-not $line) { throw "No checksum found for $asset." }

    $expected = ($line -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { throw "Checksum verification failed for $asset." }

    Expand-Archive -Path $archive -DestinationPath $temp -Force
    $sourceRoot = Join-Path $temp "ordo-win-x64"
    $target = Join-Path $InstallBase "tools\ordo\$Version"
    $binDir = Join-Path $InstallBase "bin"

    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    New-Item -ItemType Directory -Force -Path (Split-Path $target), $binDir | Out-Null
    Copy-Item -Recurse -Force $sourceRoot $target

    $exe = Join-Path $target "ordo.exe"
    foreach ($name in @("ordo", "sde")) {
        $cmd = Join-Path $binDir "$name.cmd"
        $cmdContent = "@echo off" + [Environment]::NewLine + '"' + $exe + '" %*' + [Environment]::NewLine
        Set-Content -Encoding Ascii -Path $cmd -Value $cmdContent
    }

    Set-Content -Encoding Ascii -Path (Join-Path $InstallBase "tools\ordo\current-version") -Value $Version

    Write-Host "Installed Ordo $Version to $target"
    Write-Host "Add $binDir to PATH if it is not already present."
}
finally {
    if (Test-Path $temp) { Remove-Item -Recurse -Force $temp }
}
