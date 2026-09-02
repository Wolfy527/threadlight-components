[CmdletBinding()]
param(
    [string]$PackageRoot = "."
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $PackageRoot).Path
$manifestPath = Join-Path $root "package.json"
$changelogPath = Join-Path $root "CHANGELOG.md"
$errors = [Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Missing package.json at $manifestPath"
}
if (-not (Test-Path -LiteralPath $changelogPath -PathType Leaf)) {
    throw "Missing CHANGELOG.md at $changelogPath"
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$packageName = [string]$manifest.name
$version = [string]$manifest.version
$releaseHeading = Get-Content -LiteralPath $changelogPath |
    Where-Object { $_ -match '^##\s+(\S+)\s*$' } |
    Select-Object -First 1
$changelogVersion = if ($releaseHeading -match '^##\s+(\S+)\s*$') {
    $Matches[1]
}
else {
    ""
}
if ($changelogVersion -ne $version) {
    $errors.Add(
        "CHANGELOG.md starts with release '$changelogVersion'; package.json is '$version'.")
}

if (Get-Command git -ErrorAction SilentlyContinue) {
    & git -C $root rev-parse --is-inside-work-tree 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $tag = "v$version"
        $matchingTag = & git -C $root tag --list $tag
        if ($LASTEXITCODE -ne 0) {
            $errors.Add("Could not inspect local Git tags for '$packageName'.")
        }
        elseif (@($matchingTag) -contains $tag) {
            $errors.Add(
                "Git tag '$tag' already exists; increment package.json before release.")
        }
    }
}

foreach ($relativeListing in @(".vpm-listing/index.json", "index.json")) {
    $listingPath = Join-Path $root $relativeListing
    if (-not (Test-Path -LiteralPath $listingPath -PathType Leaf)) {
        continue
    }
    try {
        $listing = Get-Content -Raw -LiteralPath $listingPath | ConvertFrom-Json
        $packageProperty = $listing.packages.PSObject.Properties[$packageName]
        $versionProperty = if ($null -ne $packageProperty) {
            $packageProperty.Value.versions.PSObject.Properties[$version]
        }
        else {
            $null
        }
        if ($null -ne $versionProperty) {
            $errors.Add(
                "VPM listing '$relativeListing' already contains '$packageName@$version'.")
        }
    }
    catch {
        $errors.Add("Could not inspect VPM listing '$relativeListing': $($_.Exception.Message)")
    }
}

foreach ($relativeArtifactRoot in @(".artifacts", "Distribution~")) {
    $artifactRoot = Join-Path $root $relativeArtifactRoot
    if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) {
        continue
    }
    $escapedName = [regex]::Escape($packageName)
    $escapedVersion = [regex]::Escape($version)
    $identityPattern = "^(?:$escapedName-)?[^\\/]*-$escapedVersion\.(?:zip|tgz|unitypackage)$|^$escapedName-$escapedVersion\.(?:zip|tgz)$"
    foreach ($artifact in Get-ChildItem -LiteralPath $artifactRoot -File -Recurse) {
        if ($artifact.Name -match $identityPattern) {
            $errors.Add(
                "Release artifact identity already exists: '$relativeArtifactRoot/$($artifact.Name)'.")
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "ERROR: $_" -ForegroundColor Red }
    throw "Release preflight failed with $($errors.Count) error(s)."
}

Write-Host "Release preflight passed for $packageName@$version."
