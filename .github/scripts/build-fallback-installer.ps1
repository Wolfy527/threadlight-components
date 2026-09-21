param(
    [Parameter(Mandatory = $false)]
    [string] $PackageRoot = ".",

    [Parameter(Mandatory = $false)]
    [string] $OutputDirectory = ".artifacts/fallback",

    [Parameter(Mandatory = $false)]
    [string] $ProjectInstallerRoot = "",

    [Parameter(Mandatory = $false)]
    [string] $UiPackageRoot = ""
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $PackageRoot).Path
$manifestPath = Join-Path $root "package.json"
$templatePath = Join-Path $root `
    "Distribution~/ThreadlightComponentsBootstrap.cs.template"

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Missing package.json at $manifestPath"
}

if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "Missing fallback bootstrap template at $templatePath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = [string] $manifest.version
$minimumSharedUiVersion = "1.0.2"
$maximumSharedUiVersionExclusive = "2.0.0"
$migrationSourceNames = @(
    "LegacyScriptsFolderMigration.cs",
    "LegacyScriptsFolderMigration.PackageRecovery.cs",
    "LegacyScriptsFolderMigration.Migration.cs",
    "LegacyScriptsFolderMigration.Identity.cs",
    "LegacyScriptsFolderMigration.Scheduling.cs"
)
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Fallback releases require a stable x.y.z package version. Found '$version'."
}

$uiDependency = [string] $manifest.vpmDependencies."com.wolfyvr.threadlight.ui"
if ($uiDependency -ne ">=$minimumSharedUiVersion <$maximumSharedUiVersionExclusive") {
    throw "Components, bootstrap, and fallback UI dependency floors must agree. Found '$uiDependency'."
}
$templateSource = Get-Content -LiteralPath $templatePath -Raw
if ($templateSource -notmatch ('MinimumSharedUiVersion\s*=\s*"' +
        [regex]::Escape($minimumSharedUiVersion) + '"') -or
    $templateSource -notmatch ('MaximumSharedUiVersionExclusive\s*=\s*"' +
        [regex]::Escape($maximumSharedUiVersionExclusive) + '"')) {
    throw "The bootstrap template UI range does not match the Components manifest."
}

$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
}

$workingRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
    ("threadlight-components-fallback-" + [guid]::NewGuid().ToString("N"))
$fallbackStage = Join-Path $workingRoot "fallback-package"
$installerStage = Join-Path $workingRoot "installer-assets"
$unityPackageStage = Join-Path $workingRoot "unitypackage"
$payloadPath = Join-Path $workingRoot "ThreadlightComponentsFallback.bytes"
$installerOutput = Join-Path $outputRoot `
    "Threadlight-Components-Fallback-Installer-$version.unitypackage"
$standaloneInstallerOutput = Join-Path $outputRoot `
    "Threadlight-Components-$version.unitypackage"
$payloadOutput = Join-Path $outputRoot `
    "ThreadlightComponentsFallback-$version.bytes"

$installerRootPath = "Assets/Threadlight/Components/Installer"
$installerEditorPath = "$installerRootPath/Editor"
$bootstrapAssetPath = "$installerEditorPath/ThreadlightComponentsBootstrap.cs"
$payloadAssetPath = "$installerRootPath/ThreadlightComponentsFallback.bytes"
$markerAssetPath = "$installerRootPath/Threadlight Components Bootstrapper.marker"
$markerContents = "Threadlight Components temporary export bootstrapper"

$assetGuids = [ordered]@{
    $installerRootPath = "d9ad23fa951c4b14bfc93923b7f36b0e"
    $installerEditorPath = "52eb624efef54c59b10fbc4e19fb337e"
    $bootstrapAssetPath = "fc608eef8f3f43e5a3579a8629d34a5f"
    $payloadAssetPath = "33bd79b26cc644e4896285530a240b2b"
    $markerAssetPath = "82661455d14d4ad09fc9759d9bdb485c"
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Value
    )

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    [System.IO.File]::WriteAllText(
        $Path,
        $Value,
        [System.Text.UTF8Encoding]::new($false)
    )
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BasePath,

        [Parameter(Mandatory = $true)]
        [string] $FullPath
    )

    $baseWithSeparator = $BasePath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + [System.IO.Path]::DirectorySeparatorChar
    $baseUri = [System.Uri]::new($baseWithSeparator)
    $pathUri = [System.Uri]::new($FullPath)
    return [System.Uri]::UnescapeDataString(
        $baseUri.MakeRelativeUri($pathUri).ToString()
    ).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Write-FolderMeta {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Guid
    )

    Write-Utf8NoBom -Path $Path -Value (@"
fileFormatVersion: 2
guid: $Guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@ + [System.Environment]::NewLine)
}

function Write-MonoMeta {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Guid
    )

    Write-Utf8NoBom -Path $Path -Value (@"
fileFormatVersion: 2
guid: $Guid
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
"@ + [System.Environment]::NewLine)
}

function Write-TextMeta {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Guid
    )

    Write-Utf8NoBom -Path $Path -Value (@"
fileFormatVersion: 2
guid: $Guid
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@ + [System.Environment]::NewLine)
}

function Copy-PackageContent {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Source,

        [Parameter(Mandatory = $true)]
        [string] $Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $includedFiles = @(
        "package.json",
        "README.md",
        "LICENSE.md",
        "Ghost Material.mat",
        "Ghost Material.mat.meta",
        "Editor.meta",
        "Editor/Threadlight.Components.Support.Editor.asmdef",
        "Editor/Threadlight.Components.Support.Editor.asmdef.meta"
    )
    foreach ($migrationSourceName in $migrationSourceNames) {
        $includedFiles += "Editor/$migrationSourceName"
        $includedFiles += "Editor/$migrationSourceName.meta"
    }
    $includedDirectories = @("Runtime", "Editor/LiveMirroring")

    Get-ChildItem -LiteralPath $Source -Recurse -File -Force |
        ForEach-Object {
            $relative = (Get-RelativePath -BasePath $Source -FullPath $_.FullName).
                Replace('\', '/')
            $included = $includedFiles -contains $relative
            if (-not $included) {
                foreach ($directory in $includedDirectories) {
                    if ($relative -eq "$directory.meta" -or
                        $relative.StartsWith(
                            "$directory/",
                            [System.StringComparison]::OrdinalIgnoreCase
                        )) {
                        $included = $true
                        break
                    }
                }
            }
            if ($included) {
                $target = Join-Path $Destination $relative
                $parent = Split-Path -Parent $target
                New-Item -ItemType Directory -Path $parent -Force | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
            }
        }
}

function Build-UnityPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string] $AssetsRoot,

        [Parameter(Mandatory = $true)]
        [string] $StagingRoot,

        [Parameter(Mandatory = $true)]
        [string] $OutputPath
    )

    New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null

    Get-ChildItem -LiteralPath $AssetsRoot -Recurse -Filter "*.meta" -File |
        Sort-Object FullName |
        ForEach-Object {
            $metaPath = $_.FullName
            $metaText = Get-Content -LiteralPath $metaPath -Raw
            $guidMatch = [regex]::Match(
                $metaText,
                '(?m)^guid:\s*([0-9a-fA-F]{32})\s*$'
            )
            if (-not $guidMatch.Success) {
                throw "Invalid or missing GUID in $metaPath"
            }

            $metaBytes = [System.IO.File]::ReadAllBytes($metaPath)
            if ($metaBytes.Length -eq 0 -or
                $metaBytes[$metaBytes.Length - 1] -ne 10) {
                throw "Unity metadata must end with a newline: $metaPath"
            }

            $assetPath = $metaPath.Substring(0, $metaPath.Length - 5)
            $relativeAssetPath = (
                Get-RelativePath `
                    -BasePath $AssetsRoot `
                    -FullPath $assetPath
            ).Replace('\', '/')
            $entryPath = Join-Path $StagingRoot $guidMatch.Groups[1].Value
            New-Item -ItemType Directory -Path $entryPath -Force | Out-Null
            Copy-Item `
                -LiteralPath $metaPath `
                -Destination (Join-Path $entryPath "asset.meta") `
                -Force
            Write-Utf8NoBom `
                -Path (Join-Path $entryPath "pathname") `
                -Value $relativeAssetPath

            if (Test-Path -LiteralPath $assetPath -PathType Leaf) {
                Copy-Item `
                    -LiteralPath $assetPath `
                    -Destination (Join-Path $entryPath "asset") `
                    -Force
            }
        }

    $tar = Get-Command tar -ErrorAction Stop
    if (Test-Path -LiteralPath $OutputPath) {
        Remove-Item -LiteralPath $OutputPath -Force
    }

    & $tar.Source -czf $OutputPath -C $StagingRoot .
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
        throw "Failed to build Unity package at $OutputPath"
    }
}

function Test-FallbackPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $ExpectedVersion,

        [Parameter(Mandatory = $true)]
        [string] $SourceRoot
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $normalizedEntries = @(
            $archive.Entries |
                ForEach-Object { $_.FullName.Replace('\', '/') }
        )
        $requiredEntries = @(
            "package.json",
            "SharedUI~/package.json",
            "SharedUI~/Editor/Threadlight.EditorUI.asmdef",
            "Runtime/Threadlight.Components.asmdef",
            "Runtime/PrefabId.cs",
            "Runtime/GeneratedTargetMetadata.cs",
            "Runtime/GeneratedHierarchyMetadata.cs",
            "Runtime/GeneratedEditorOnlyObject.cs",
            "Runtime/AuthoringOnlyComponent.cs",
            "Runtime/LiveMirroringSystem.cs",
            "Runtime/LiveMirroringSystem.cs.meta",
            "Editor/Threadlight.Components.Support.Editor.asmdef"
        )
        foreach ($migrationSourceName in $migrationSourceNames) {
            $requiredEntries += "Editor/$migrationSourceName"
            $requiredEntries += "Editor/$migrationSourceName.meta"
        }
        foreach ($requiredEntry in $requiredEntries) {
            if ($normalizedEntries -notcontains $requiredEntry) {
                throw "Fallback payload is missing '$requiredEntry'."
            }
        }

        $actualMigrationEntries = @($normalizedEntries |
            Where-Object { $_ -match '^Editor/LegacyScriptsFolderMigration.*\.cs(?:\.meta)?$' } |
            Sort-Object)
        $expectedMigrationEntries = @($migrationSourceNames | ForEach-Object {
                "Editor/$_"
                "Editor/$_.meta"
            } | Sort-Object)
        if ([string]::Join("`n", $actualMigrationEntries) -ne
            [string]::Join("`n", $expectedMigrationEntries)) {
            throw "Fallback payload migration source set does not exactly match the intended five source/meta pairs."
        }

        foreach ($relative in $expectedMigrationEntries) {
            $entry = $archive.Entries | Where-Object {
                $_.FullName.Replace('\', '/') -eq $relative
            } | Select-Object -First 1
            $entryStream = $entry.Open()
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try {
                $entryHash = [BitConverter]::ToString(
                    $sha.ComputeHash($entryStream)).Replace('-', '').ToLowerInvariant()
            }
            finally {
                $sha.Dispose()
                $entryStream.Dispose()
            }
            $sourceHash = (Get-FileHash -LiteralPath (Join-Path $SourceRoot $relative) `
                -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($entryHash -ne $sourceHash) {
                throw "Fallback payload migration entry '$relative' differs from staged Components source."
            }
        }

        if ($normalizedEntries | Where-Object {
                $_ -like ".github/*" -or
                $_ -like ".vpm-listing/*" -or
                $_ -like ".git/*" -or
                $_ -match '(^|/)(Development|Library|debug|review)(/|$)'
            }) {
            throw "Fallback payload contains repository-only files."
        }

        $manifestEntry = $archive.Entries |
            Where-Object { $_.FullName.Replace('\', '/') -eq "package.json" } |
            Select-Object -First 1
        $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
        try {
            $payloadManifest = $reader.ReadToEnd() | ConvertFrom-Json
        }
        finally {
            $reader.Dispose()
        }

        if ($payloadManifest.name -ne
                "com.wolfyvr.threadlight.components.fallback" -or
            $payloadManifest.version -ne $ExpectedVersion) {
            throw "Fallback payload package identity is invalid."
        }

        $compatibilityGuids = [ordered]@{
            "Runtime/LiveMirroringSystem.cs.meta" =
                "5c54d508ba4a3ee4baa5148633885b51"
            "Runtime/GeneratedTargetMetadata.cs.meta" =
                "48742d3549a555842844b99523feab8f"
            "Runtime/GeneratedHierarchyMetadata.cs.meta" =
                "0bc840ca6f774dfc91cc06af65dc5b45"
            "Runtime/GeneratedEditorOnlyObject.cs.meta" =
                "6a339ada66db0524bb16d5ed1fbe64bc"
            "Runtime/AuthoringOnlyComponent.cs.meta" =
                "40218417691f9c041a2ac01d1b9d1a5c"
            "Runtime/PrefabId.cs.meta" =
                "5d8f4687c2084716afeb3da11a1b050d"
            "Ghost Material.mat.meta" =
                "4342400023fc9204e9fab7239dec44ef"
        }
        foreach ($compatibilityGuid in $compatibilityGuids.GetEnumerator()) {
            $metaEntry = $archive.Entries |
                Where-Object {
                    $_.FullName.Replace('\', '/') -eq $compatibilityGuid.Key
                } |
                Select-Object -First 1
            if ($null -eq $metaEntry) {
                throw "Fallback payload is missing '$($compatibilityGuid.Key)'."
            }

            $metaReader = [System.IO.StreamReader]::new($metaEntry.Open())
            try {
                $metaText = $metaReader.ReadToEnd()
            }
            finally {
                $metaReader.Dispose()
            }
            $expectedPattern =
                '(?m)^guid:\s*' +
                [regex]::Escape($compatibilityGuid.Value) +
                '\s*$'
            if ($metaText -notmatch $expectedPattern) {
                throw "Fallback payload changed the GUID for '$($compatibilityGuid.Key)'."
            }
        }

        foreach ($forbiddenProperty in @(
            "legacyFolders",
            "legacyFiles",
            "legacyPackages",
            "vpmDependencies"
        )) {
            if ($null -ne
                $payloadManifest.PSObject.Properties[$forbiddenProperty]) {
                throw "Fallback payload must not contain '$forbiddenProperty'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $workingRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

    if ([string]::IsNullOrWhiteSpace($UiPackageRoot)) {
        $siblingUi = Join-Path (Split-Path -Parent $root) "com.wolfyvr.threadlight.ui"
        if (Test-Path -LiteralPath (Join-Path $siblingUi "package.json")) {
            $UiPackageRoot = $siblingUi
        }
        else {
            $UiPackageRoot = Join-Path $workingRoot "shared-ui-source"
            $snapshot = Join-Path $root "Distribution~/ThreadlightUI.bytes"
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [System.IO.Compression.ZipFile]::OpenRead($snapshot)
            try {
                foreach ($entry in $archive.Entries) {
                    $relative = $entry.FullName.Replace('\', '/')
                    if ($relative.StartsWith('/') -or $relative.Contains('..') -or
                        $relative.Contains(':') -or
                        ($relative -notmatch '^Editor/' -and $relative -notin @(
                            "package.json", "package.json.meta", "README.md", "README.md.meta",
                            "LICENSE.md", "LICENSE.md.meta", "Editor.meta",
                            "Threadlight Wordmark.png", "Threadlight Wordmark.png.meta",
                            "Threadlight Compact Mark.png", "Threadlight Compact Mark.png.meta"))) {
                        throw "The shared UI snapshot contains an unexpected path: $relative"
                    }
                }
            }
            finally { $archive.Dispose() }
            [System.IO.Compression.ZipFile]::ExtractToDirectory($snapshot, $UiPackageRoot)
        }
    }
    $uiRoot = (Resolve-Path -LiteralPath $UiPackageRoot).Path
    $uiManifest = Get-Content -LiteralPath (Join-Path $uiRoot "package.json") -Raw | ConvertFrom-Json
    $uiAssembly = Get-Content -LiteralPath (Join-Path $uiRoot "Editor/Threadlight.EditorUI.asmdef") -Raw | ConvertFrom-Json
    $uiAssemblyMeta = Get-Content -LiteralPath (Join-Path $uiRoot "Editor/Threadlight.EditorUI.asmdef.meta") -Raw
    if ($uiManifest.name -ne "com.wolfyvr.threadlight.ui" -or
        $uiManifest.version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
        [version] $uiManifest.version -lt [version] $minimumSharedUiVersion -or
        [version] $uiManifest.version -ge [version] $maximumSharedUiVersionExclusive -or
        $uiAssembly.name -ne "Threadlight.EditorUI" -or
        $uiAssembly.references.Count -ne 0 -or
        $uiAssemblyMeta -notmatch '(?m)^guid:\s*ba116ed4e1e542ca82aceac5f1314ca1\s*$') {
        throw "The fallback requires the independent ThreadLight UI package with its stable assembly GUID."
    }

    Copy-PackageContent -Source $root -Destination $fallbackStage

    # Keep dependency sources hidden from Assets; the bootstrap installs one
    # canonical embedded UPM package before exposing the Components fallback.
    $sharedUiStage = Join-Path $fallbackStage "SharedUI~"
    New-Item -ItemType Directory -Path $sharedUiStage -Force | Out-Null
    foreach ($entry in @("package.json", "package.json.meta", "README.md", "README.md.meta",
        "LICENSE.md", "LICENSE.md.meta", "Editor", "Editor.meta",
        "Threadlight Wordmark.png", "Threadlight Wordmark.png.meta",
        "Threadlight Compact Mark.png", "Threadlight Compact Mark.png.meta")) {
        $sourceEntry = Join-Path $uiRoot $entry
        if (-not (Test-Path -LiteralPath $sourceEntry)) {
            throw "ThreadLight UI payload is missing '$entry'."
        }
        Copy-Item -LiteralPath $sourceEntry -Destination $sharedUiStage -Recurse -Force
    }

    $fallbackManifestPath = Join-Path $fallbackStage "package.json"
    $fallbackManifest =
        Get-Content -LiteralPath $fallbackManifestPath -Raw |
        ConvertFrom-Json
    $fallbackManifest.name = "com.wolfyvr.threadlight.components.fallback"
    $fallbackManifest.displayName =
        "ThreadLight Components (Fallback)"
    $fallbackManifest.description =
        "Embedded fallback for products that require Threadlight Components."
    foreach ($property in @(
        "documentationUrl",
        "changelogUrl",
        "legacyFolders",
        "legacyFiles",
        "legacyPackages",
        "vpmDependencies"
    )) {
        $fallbackManifest.PSObject.Properties.Remove($property)
    }
    Write-Utf8NoBom `
        -Path $fallbackManifestPath `
        -Value ($fallbackManifest | ConvertTo-Json -Depth 20)

    Compress-Archive `
        -Path (Join-Path $fallbackStage "*") `
        -DestinationPath ($payloadPath + ".zip") `
        -CompressionLevel Optimal
    Move-Item -LiteralPath ($payloadPath + ".zip") -Destination $payloadPath
    Test-FallbackPayload -Path $payloadPath -ExpectedVersion $version -SourceRoot $root
    Copy-Item -LiteralPath $payloadPath -Destination $payloadOutput -Force

    $bootstrapSource = Get-Content -LiteralPath $templatePath -Raw
    $bootstrapSource = $bootstrapSource.Replace(
        "@@INSTALLER_NAMESPACE@@",
        "Threadlight.ComponentsFallbackInstaller"
    ).Replace(
        "@@INSTALLER_ROOT@@",
        $installerRootPath
    ).Replace(
        "@@INSTALLER_PAYLOAD_GUID@@",
        $assetGuids[$payloadAssetPath]
    )
    if (-not $bootstrapSource.Contains(
            'com.wolfyvr.threadlight.components.fallback')) {
        throw "Fallback bootstrap template is invalid."
    }
    $stagedInstallerRoot = Join-Path $installerStage $installerRootPath
    $stagedEditorRoot = Join-Path $installerStage $installerEditorPath
    New-Item -ItemType Directory -Path $stagedEditorRoot -Force | Out-Null
    Write-Utf8NoBom `
        -Path (Join-Path $installerStage $bootstrapAssetPath) `
        -Value $bootstrapSource
    Copy-Item `
        -LiteralPath $payloadPath `
        -Destination (Join-Path $installerStage $payloadAssetPath) `
        -Force
    Write-Utf8NoBom `
        -Path (Join-Path $installerStage $markerAssetPath) `
        -Value ($markerContents + [System.Environment]::NewLine)

    Write-FolderMeta `
        -Path ($stagedInstallerRoot + ".meta") `
        -Guid $assetGuids[$installerRootPath]
    Write-FolderMeta `
        -Path ($stagedEditorRoot + ".meta") `
        -Guid $assetGuids[$installerEditorPath]
    Write-MonoMeta `
        -Path ((Join-Path $installerStage $bootstrapAssetPath) + ".meta") `
        -Guid $assetGuids[$bootstrapAssetPath]
    Write-TextMeta `
        -Path ((Join-Path $installerStage $payloadAssetPath) + ".meta") `
        -Guid $assetGuids[$payloadAssetPath]
    Write-TextMeta `
        -Path ((Join-Path $installerStage $markerAssetPath) + ".meta") `
        -Guid $assetGuids[$markerAssetPath]

    Build-UnityPackage `
        -AssetsRoot $installerStage `
        -StagingRoot $unityPackageStage `
        -OutputPath $installerOutput
    Copy-Item `
        -LiteralPath $installerOutput `
        -Destination $standaloneInstallerOutput `
        -Force

    if (-not [string]::IsNullOrWhiteSpace($ProjectInstallerRoot)) {
        $projectInstallerPath = [System.IO.Path]::GetFullPath(
            $ProjectInstallerRoot
        )
        $projectEditorPath = Join-Path $projectInstallerPath "Editor"
        New-Item -ItemType Directory -Path $projectEditorPath -Force |
            Out-Null
        foreach ($obsoleteAssemblyFile in @(
            "Threadlight.ComponentsFallbackInstaller.asmdef",
            "Threadlight.ComponentsFallbackInstaller.asmdef.meta",
            "Wolfy.ThreadlightComponentsBootstrap.Editor.asmdef",
            "Wolfy.ThreadlightComponentsBootstrap.Editor.asmdef.meta"
        )) {
            $obsoleteAssemblyPath =
                Join-Path $projectEditorPath $obsoleteAssemblyFile
            if (Test-Path -LiteralPath $obsoleteAssemblyPath -PathType Leaf) {
                Remove-Item -LiteralPath $obsoleteAssemblyPath -Force
            }
        }
        Write-Utf8NoBom `
            -Path (Join-Path $projectEditorPath "ThreadlightComponentsBootstrap.cs") `
            -Value $bootstrapSource
        Copy-Item `
            -LiteralPath $payloadPath `
            -Destination (
                Join-Path $projectInstallerPath "ThreadlightComponentsFallback.bytes"
            ) `
            -Force
        Write-Utf8NoBom `
            -Path (Join-Path $projectInstallerPath `
                "Threadlight Components Bootstrapper.marker") `
            -Value ($markerContents + [System.Environment]::NewLine)

        $projectBootstrapPath = Join-Path `
            $projectEditorPath `
            "ThreadlightComponentsBootstrap.cs"
        $projectPayloadPath = Join-Path `
            $projectInstallerPath `
            "ThreadlightComponentsFallback.bytes"
        Test-FallbackPayload `
            -Path $projectPayloadPath `
            -ExpectedVersion $version `
            -SourceRoot $root
    }

    $payloadHash = (Get-FileHash -LiteralPath $payloadOutput -Algorithm SHA256).Hash
    $installerHash =
        (Get-FileHash -LiteralPath $installerOutput -Algorithm SHA256).Hash

    [pscustomobject]@{
        Version = $version
        FallbackPackage = "com.wolfyvr.threadlight.components.fallback"
        Payload = $payloadOutput
        PayloadSha256 = $payloadHash
        Installer = $installerOutput
        StandaloneInstaller = $standaloneInstallerOutput
        InstallerSha256 = $installerHash
    } | Format-List
}
finally {
    if (Test-Path -LiteralPath $workingRoot) {
        Remove-Item -LiteralPath $workingRoot -Recurse -Force
    }
}
