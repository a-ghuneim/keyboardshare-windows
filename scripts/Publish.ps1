# Publishes and packages locally. Does not create a tag or contact GitHub.
[CmdletBinding()]
param([string]$OutputDirectory = 'publish/release/win-x64')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'KeyboardShare/KeyboardShare.csproj'
$outputPath = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repoRoot $OutputDirectory }
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw 'Expected a semantic Version in the application project.' }

Push-Location $repoRoot
try {
    & dotnet publish $projectPath -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' '-p:DebugSymbols=false' '-p:ContinuousIntegrationBuild=true' -o $outputPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    foreach ($name in @('README.md', 'LICENSE', 'SECURITY.md')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $outputPath -Force
    }

    # Preserve license and notice files supplied by the exact runtime packs restored
    # for this publish, instead of relying on a particular SDK installation path.
    $assets = Get-Content -LiteralPath (Join-Path $repoRoot 'KeyboardShare/obj/project.assets.json') -Raw | ConvertFrom-Json
    $noticePath = Join-Path $outputPath 'LICENSES'
    New-Item -ItemType Directory -Path $noticePath -Force | Out-Null
    foreach ($pack in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
        $dependency = @($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies | Where-Object { $_.name -eq $pack }) | Select-Object -First 1
        if ($null -eq $dependency) { throw "Runtime pack missing from restore metadata: $pack" }
        $packVersion = ($dependency.version -replace '[\[\]\(\)]', '').Split(',')[0].Trim()
        $packPath = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder ($pack.ToLowerInvariant() + '/' + $packVersion)
            if (Test-Path -LiteralPath $candidate) { $packPath = $candidate; break }
        }
        if ($null -eq $packPath) { throw "Cannot find restored runtime pack: $pack $packVersion" }
        $notices = @(Get-ChildItem -LiteralPath $packPath -File | Where-Object { $_.Name -match '^(LICENSE|THIRD[-_]?PARTY[-_]?NOTICES|NOTICE)(\..*)?$' })
        if ($notices.Count -eq 0) { throw "No license files found for $pack" }
        foreach ($notice in $notices) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $noticePath ($pack + '-' + $notice.Name)) -Force
        }
    }

    $archiveName = "KeyboardShare-$version-win-x64.zip"
    New-Item -ItemType Directory -Path (Join-Path $repoRoot 'publish/release') -Force | Out-Null
    $archivePath = Join-Path $repoRoot ('publish/release/' + $archiveName)
    # Explicit allowlist: never package incidental settings, logs or debug symbols.
    $packageFiles = @('KeyboardShare.exe', 'README.md', 'LICENSE', 'SECURITY.md', 'LICENSES') | ForEach-Object { Join-Path $outputPath $_ }
    Compress-Archive -LiteralPath $packageFiles -DestinationPath $archivePath -Force
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($archivePath + '.sha256') -Value "$hash  $archiveName" -Encoding Ascii
    if ((Get-Content -LiteralPath (Join-Path $repoRoot 'LICENSE') -Raw) -match 'REPLACE BEFORE PUBLICATION') {
        Write-Warning 'Replace the copyright holder placeholder in LICENSE and rerun this script before publishing.'
    }
    Write-Host "Release archive: $archivePath"
    Write-Host "Checksum: $archivePath.sha256"
}
finally { Pop-Location }
