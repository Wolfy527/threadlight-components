param(
    [Parameter(Mandatory = $false)]
    [string] $PackageRoot = "."
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $PackageRoot).Path
$manifestPath = Join-Path $root "package.json"
$templatePath = Join-Path $root `
    "Distribution~/ThreadlightComponentsBootstrap.cs.template"
$legacyMigrationPath = Join-Path $root `
    "Editor/LegacyScriptsFolderMigration.cs"
$sourceManifest = Get-Content -LiteralPath $manifestPath -Raw |
    ConvertFrom-Json
$sourceVersion = [string] $sourceManifest.version
$sourceLineage = [string] $sourceManifest.compatibilityLineage
$sourceReleaseEpoch = [int] $sourceManifest.compatibilityReleaseEpoch
if ($sourceVersion -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)') {
    throw "Package manifest version '$sourceVersion' is not valid semantic version."
}
if ([string]::IsNullOrWhiteSpace($sourceLineage) -or
    $sourceReleaseEpoch -lt 1) {
    throw "Package manifest does not declare a valid compatibility release identity."
}
$newerFallbackVersion =
    "{0}.0.0" -f ([int] $Matches.major + 1)
$previousFallbackVersion = if ([int] $Matches.patch -gt 0) {
    "{0}.{1}.{2}" -f `
        [int] $Matches.major, `
        [int] $Matches.minor, `
        ([int] $Matches.patch - 1)
}
else {
    "0.0.1"
}
$workingRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
    ("threadlight-fallback-bootstrap-compile-" + [guid]::NewGuid().ToString("N"))

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Value
    )

    [System.IO.File]::WriteAllText(
        $Path,
        $Value,
        [System.Text.UTF8Encoding]::new($false)
    )
}

try {
    New-Item -ItemType Directory -Path $workingRoot -Force | Out-Null

    $bootstrap = Get-Content -LiteralPath $templatePath -Raw
    $bootstrap = $bootstrap.Replace(
        "@@INSTALLER_NAMESPACE@@",
        "Threadlight.ComponentsFallbackInstaller"
    ).Replace(
        "@@INSTALLER_ROOT@@",
        "Assets/Threadlight/Components/Temp/Threadlight Components Installer"
    ).Replace(
        "@@INSTALLER_PAYLOAD_GUID@@",
        "33bd79b26cc644e4896285530a240b2b"
    )
    Write-Utf8NoBom `
        -Path (Join-Path $workingRoot "ThreadlightComponentsBootstrap.cs") `
        -Value $bootstrap
    Copy-Item `
        -LiteralPath $legacyMigrationPath `
        -Destination (Join-Path $workingRoot "LegacyScriptsFolderMigration.cs") `
        -Force

    Write-Utf8NoBom `
        -Path (Join-Path $workingRoot "UnityStubs.cs") `
        -Value @'
namespace UnityEngine
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text.RegularExpressions;

    public static class Application
    {
        public static string dataPath { get; set; }
    }

    public static class Debug
    {
        public static readonly List<string> Messages = new List<string>();
        public static readonly List<string> Errors = new List<string>();
        public static readonly List<string> Warnings = new List<string>();
        public static void Log(object message) => Messages.Add(message?.ToString());
        public static void LogError(object message) => Errors.Add(message?.ToString());
        public static void LogWarning(object message) => Warnings.Add(message?.ToString());
    }

    public static class JsonUtility
    {
        public static T FromJson<T>(string json)
        {
            T value = Activator.CreateInstance<T>();
            foreach (string name in new[] {
                "name", "version", "compatibilityLineage"
            })
            {
                FieldInfo field = typeof(T).GetField(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
                Match match = Regex.Match(
                    json ?? string.Empty,
                    "\\\"" + name + "\\\"\\s*:\\s*\\\"([^\\\"]*)\\\"");
                if (field != null && match.Success)
                    field.SetValue(value, match.Groups[1].Value);
            }
            FieldInfo epochField = typeof(T).GetField(
                "compatibilityReleaseEpoch",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Match epochMatch = Regex.Match(
                json ?? string.Empty,
                "\"compatibilityReleaseEpoch\"\\s*:\\s*(\\d+)");
            if (epochField != null && epochMatch.Success)
                epochField.SetValue(value, int.Parse(epochMatch.Groups[1].Value));
            return value;
        }
    }
}

namespace UnityEditor
{
    using System;
    using System.Collections.Generic;

    public sealed class PackageInfo {}
    public abstract class AssetPostprocessor {}

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute {}

    public static class EditorApplication
    {
        public static Action delayCall { get; set; }
        public static event Action update;
        public static event Action projectChanged;
        public static event Action quitting;
        public static double timeSinceStartup => 0;
        public static bool isCompiling => false;
        public static bool isUpdating => false;
    }

    [Flags]
    public enum ImportAssetOptions
    {
        Default = 0,
        ForceUpdate = 1,
        ForceSynchronousImport = 8
    }

    public static class AssetDatabase
    {
        public static Action<string[]> onImportPackageItemsCompleted;
        public static event Action<string> importPackageStarted;
        public static event Action<string> importPackageCompleted;
        public static event Action<string> importPackageCancelled;
        public static event Action<string, string> importPackageFailed;
        public static readonly Dictionary<string, string> Guids =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static readonly List<string> DeletedAssets =
            new List<string>();
        public static bool IsValidFolder(string path) => false;
        public static string AssetPathToGUID(string path) =>
            Guids.TryGetValue(path, out string guid) ? guid : "";
        public static bool DeleteAsset(string path)
        {
            DeletedAssets.Add(path);
            return true;
        }
        public static void Refresh(ImportAssetOptions options) {}
        public static void ImportAsset(
            string path,
            ImportAssetOptions options) {}
    }

    public static class EditorUtility
    {
        public static bool DisplayDialog(
            string title,
            string message,
            string ok) => true;
    }

    public static class SessionState
    {
        private static readonly Dictionary<string, bool> Values =
            new Dictionary<string, bool>();
        public static bool GetBool(string key, bool defaultValue) =>
            Values.TryGetValue(key, out bool value) ? value : defaultValue;
        public static void SetBool(string key, bool value) => Values[key] = value;
        public static void Clear() => Values.Clear();
    }
}

namespace UnityEditor.PackageManager
{
    public sealed class PackageInfo
    {
        public string name;
        public string version;
        public string resolvedPath;
        public string assetPath;
        public static PackageInfo[] RegisteredPackages = new PackageInfo[0];
        public static PackageInfo[] GetAllRegisteredPackages() =>
            RegisteredPackages;
        public static PackageInfo FindForAssembly(
            System.Reflection.Assembly assembly) =>
            RegisteredPackages.Length > 0 ? RegisteredPackages[0] : null;
    }

    public static class Client
    {
        public static object Resolve() => null;
    }
}
'@

    Write-Utf8NoBom `
        -Path (Join-Path $workingRoot "BootstrapCompile.csproj") `
        -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
'@

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnetSdks = if ($null -ne $dotnet) {
        @(& $dotnet.Source --list-sdks)
    }
    else {
        @()
    }

    if ($dotnetSdks.Count -gt 0) {
        & $dotnet.Source build `
            (Join-Path $workingRoot "BootstrapCompile.csproj") `
            --nologo `
            --verbosity quiet
        $outputAssembly = Join-Path `
            $workingRoot `
            "bin/Debug/net8.0/BootstrapCompile.dll"
    }
    else {
        $unityCsc = Get-ChildItem `
            -Path "C:\Program Files\Unity\Hub\Editor\*\Editor\Data\MonoBleedingEdge\lib\mono\4.5\csc.exe" `
            -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -eq $unityCsc) {
            throw "No .NET SDK or Unity C# compiler was found."
        }

        $monoRoot = Split-Path -Parent (Split-Path -Parent $unityCsc.FullName)
        $monoBleedingEdge = Split-Path -Parent `
            (Split-Path -Parent $monoRoot)
        $monoExecutable = Join-Path $monoBleedingEdge "bin/mono.exe"
        if (-not (Test-Path -LiteralPath $monoExecutable -PathType Leaf)) {
            throw "Unity Mono runtime was not found."
        }

        $compressionReference = Join-Path `
            $monoRoot `
            "4.8-api/System.IO.Compression.dll"
        if (-not (Test-Path -LiteralPath $compressionReference -PathType Leaf)) {
            throw "Unity System.IO.Compression reference was not found."
        }

        $outputAssembly = Join-Path $workingRoot "BootstrapCompile.dll"
        & $monoExecutable `
            $unityCsc.FullName `
            /nologo `
            /target:library `
            /langversion:latest `
            "/out:$outputAssembly" `
            "/reference:$compressionReference" `
            (Join-Path $workingRoot "ThreadlightComponentsBootstrap.cs") `
            (Join-Path $workingRoot "LegacyScriptsFolderMigration.cs") `
            (Join-Path $workingRoot "UnityStubs.cs")
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Fallback bootstrap compile check failed."
    }

    $assembly = [System.Reflection.Assembly]::Load(
        [System.IO.File]::ReadAllBytes($outputAssembly)
    )
    $migrationType = $assembly.GetType(
        "Threadlight.Components.Editor.LegacyScriptsFolderMigration",
        $true
    )
    $isBuilderInstalled = $migrationType.GetMethod(
        "IsBuilderInstalled",
        [System.Reflection.BindingFlags]::NonPublic -bor
            [System.Reflection.BindingFlags]::Static
    )
    if ($null -eq $isBuilderInstalled) {
        throw "Could not locate the fallback payload preservation check."
    }

    $matrixProject = Join-Path $workingRoot "matrix-project"
    $matrixPackages = Join-Path $matrixProject "Packages"
    New-Item -ItemType Directory -Path $matrixPackages -Force | Out-Null
    $invoke = {
        param([string] $ProjectRoot)
        return [bool] $isBuilderInstalled.Invoke($null, @($ProjectRoot))
    }

    if (& $invoke $matrixProject) {
        throw "Fallback payload preservation claimed a project with no Builder."
    }

    $privateBuilder = Join-Path $matrixPackages "com.wolfyvr.threadlight.builder"
    New-Item -ItemType Directory -Path $privateBuilder -Force | Out-Null
    if (-not (& $invoke $matrixProject)) {
        throw "Fallback payload was not preserved for the private Builder."
    }
    Remove-Item -LiteralPath $privateBuilder -Recurse -Force

    $lightweightBuilder = Join-Path `
        $matrixPackages `
        "com.wolfyvr.threadlight.mirroring"
    New-Item -ItemType Directory -Path $lightweightBuilder -Force | Out-Null
    if (-not (& $invoke $matrixProject)) {
        throw "Fallback payload was not preserved for Threadlight Mirroring."
    }

    $handleImportedAssets = $migrationType.GetMethod(
        "HandleImportedAssets",
        [System.Reflection.BindingFlags]::NonPublic -bor
            [System.Reflection.BindingFlags]::Static
    )
    if ($null -eq $handleImportedAssets) {
        throw "Could not locate the legacy import migration entry point."
    }
    $handlePackageImportStarted = $migrationType.GetMethod(
        "HandlePackageImportStarted",
        [System.Reflection.BindingFlags]::NonPublic -bor
            [System.Reflection.BindingFlags]::Static
    )
    $handlePackageImportItemsCompleted = $migrationType.GetMethod(
        "HandlePackageImportItemsCompleted",
        [System.Reflection.BindingFlags]::NonPublic -bor
            [System.Reflection.BindingFlags]::Static
    )
    if ($null -eq $handlePackageImportStarted -or
        $null -eq $handlePackageImportItemsCompleted) {
        throw "Could not locate the managed package import guards."
    }

    $snapshotProject = Join-Path $workingRoot "managed-package-snapshot"
    $snapshotPackageRoot = Join-Path $snapshotProject `
        "Packages/com.wolfyvr.threadlight.components"
    $snapshotSource = Join-Path $snapshotPackageRoot `
        "Runtime/LiveMirroringSystem.cs"
    New-Item `
        -ItemType Directory `
        -Path ([System.IO.Path]::GetDirectoryName($snapshotSource)) `
        -Force |
        Out-Null
    Write-Utf8NoBom -Path $snapshotSource -Value "managed source"
    New-Item `
        -ItemType Directory `
        -Path (Join-Path $snapshotProject "Assets") `
        -Force |
        Out-Null
    $snapshotPackage = [UnityEditor.PackageManager.PackageInfo]::new()
    $snapshotPackage.name = "com.wolfyvr.threadlight.components"
    $snapshotPackage.version = $sourceVersion
    $snapshotPackage.resolvedPath = $snapshotPackageRoot
    [UnityEditor.PackageManager.PackageInfo]::RegisteredPackages =
        [UnityEditor.PackageManager.PackageInfo[]] @($snapshotPackage)
    [UnityEngine.Application]::dataPath = Join-Path $snapshotProject "Assets"
    $handlePackageImportStarted.Invoke($null, [object[]] @("Legacy Import"))
    Write-Utf8NoBom -Path $snapshotSource -Value "legacy overwrite"
    [object[]] $snapshotArguments = New-Object object[] 1
    $snapshotArguments[0] = [string[]] @()
    $handlePackageImportItemsCompleted.Invoke($null, $snapshotArguments)
    if ((Get-Content -Raw -LiteralPath $snapshotSource).Trim() -ne
        "managed source") {
        throw "A legacy GUID overwrite was not restored from the package snapshot."
    }
    [UnityEditor.PackageManager.PackageInfo]::RegisteredPackages =
        [UnityEditor.PackageManager.PackageInfo[]] @()

    function Invoke-LegacyImportMigration {
        param(
            [Parameter(Mandatory = $true)]
            [string] $Project,

            [Parameter(Mandatory = $true)]
            [string[]] $ImportedAssets
        )

        [UnityEngine.Application]::dataPath = Join-Path $Project "Assets"
        [object[]] $arguments = New-Object object[] 1
        $arguments[0] = [string[]] $ImportedAssets
        $handleImportedAssets.Invoke($null, $arguments)
    }

    function Add-MetaGuid {
        param(
            [Parameter(Mandatory = $true)]
            [string] $Path,

            [Parameter(Mandatory = $true)]
            [string] $Guid
        )

        New-Item `
            -ItemType Directory `
            -Path ([System.IO.Path]::GetDirectoryName($Path)) `
            -Force |
            Out-Null
        Write-Utf8NoBom -Path $Path -Value ("guid: " + $Guid)
    }

    function Add-LegacyFallbackFixture {
        param(
            [Parameter(Mandatory = $true)]
            [string] $Project,

            [Parameter(Mandatory = $false)]
            [string] $PackageName = "com.wolfy527.prefab-components.fallback",

            [Parameter(Mandatory = $false)]
            [switch] $Incomplete
        )

        $fallbackRoot = Join-Path $Project `
            "Assets/Wolfy_527/~ Supporting Files/Prefab Components Fallback"
        New-Item -ItemType Directory -Path $fallbackRoot -Force | Out-Null
        Write-Utf8NoBom `
            -Path (Join-Path $fallbackRoot "package.json") `
            -Value (([ordered] @{
                name = $PackageName
                version = "1.1.9"
            }) | ConvertTo-Json -Compress)
        Write-Utf8NoBom `
            -Path (Join-Path $fallbackRoot "creator-sentinel.txt") `
            -Value "preserve"
        Add-MetaGuid `
            -Path (Join-Path $fallbackRoot `
                "Live Mirroring/Runtime/LiveMirroringSystem.cs.meta") `
            -Guid "b4ef5c021c9e12e45ac198b875874db0"
        Add-MetaGuid `
            -Path (Join-Path $fallbackRoot `
                "Shared/Authoring/Runtime/GeneratedTargetMetadata.cs.meta") `
            -Guid "e70a5bbf5de42b0478484ce9a4024548"
        if (-not $Incomplete) {
            Add-MetaGuid `
                -Path (Join-Path $fallbackRoot `
                    "Shared/Authoring/Runtime/AuthoringOnlyComponent.cs.meta") `
                -Guid "076fe599df6ed2c478fcc7948ea0f20e"
        }
        Add-MetaGuid `
            -Path (Join-Path $fallbackRoot `
                "Shared/Authoring/Runtime/GeneratedEditorOnlyObject.cs.meta") `
            -Guid "c9e72b6f6ebf0944cbc8e46be25353e4"
        Add-MetaGuid `
            -Path (Join-Path $fallbackRoot `
                "Shared/Authoring/Runtime/GeneratedHierarchyMetadata.cs.meta") `
            -Guid "f09ee1c973e46e642a90ceb8782efd1b"
        Add-MetaGuid `
            -Path (Join-Path $fallbackRoot `
                "Shared/Authoring/Runtime/PrefabId.cs.meta") `
            -Guid "139e0c1a87d72dc488222ee8b21bb47e"
        Add-MetaGuid `
            -Path (Join-Path $fallbackRoot "Ghost Material.mat.meta") `
            -Guid "4b0d26895d37a904eb40cd0d68cdb0e7"
        return $fallbackRoot
    }

    $reverseImportProject = Join-Path $workingRoot "legacy-reverse-import"
    $reverseFallback = Add-LegacyFallbackFixture -Project $reverseImportProject
    Invoke-LegacyImportMigration `
        -Project $reverseImportProject `
        -ImportedAssets @(
            "Assets/Wolfy_527/~ Supporting Files/Prefab Components Fallback/package.json"
        )
    if (Test-Path -LiteralPath $reverseFallback) {
        throw "A recognized fallback imported after Components was not quarantined."
    }
    $fallbackSentinel = Get-ChildItem `
        -LiteralPath (Join-Path $reverseImportProject "Legacy Package Backups") `
        -Filter "creator-sentinel.txt" `
        -File `
        -Recurse |
        Select-Object -First 1
    if ($null -eq $fallbackSentinel) {
        throw "Fallback quarantine did not preserve creator-added files."
    }

    $unrelatedProject = Join-Path $workingRoot "unrelated-fallback"
    $unrelatedFallback = Add-LegacyFallbackFixture `
        -Project $unrelatedProject `
        -PackageName "com.example.unrelated"
    Invoke-LegacyImportMigration `
        -Project $unrelatedProject `
        -ImportedAssets @(
            "Assets/Wolfy_527/~ Supporting Files/Prefab Components Fallback/package.json"
        )
    if (-not (Test-Path -LiteralPath $unrelatedFallback -PathType Container)) {
        throw "An unrelated same-path fallback folder was incorrectly claimed."
    }

    $partialProject = Join-Path $workingRoot "partial-fallback-import"
    $partialFallback = Add-LegacyFallbackFixture `
        -Project $partialProject `
        -Incomplete
    Invoke-LegacyImportMigration `
        -Project $partialProject `
        -ImportedAssets @(
            "Assets/Wolfy_527/~ Supporting Files/Prefab Components Fallback/package.json"
        )
    if (-not (Test-Path -LiteralPath $partialFallback -PathType Container)) {
        throw "A partially imported fallback was claimed before ownership was proven."
    }
    Add-MetaGuid `
        -Path (Join-Path $partialFallback `
            "Shared/Authoring/Runtime/AuthoringOnlyComponent.cs.meta") `
        -Guid "076fe599df6ed2c478fcc7948ea0f20e"
    Invoke-LegacyImportMigration `
        -Project $partialProject `
        -ImportedAssets @(
            "Assets/Wolfy_527/~ Supporting Files/Prefab Components Fallback/Shared/Authoring/Runtime/AuthoringOnlyComponent.cs.meta"
        )
    if (Test-Path -LiteralPath $partialFallback) {
        throw "A completed fallback import was not retried and quarantined."
    }

    $oldGlizzyProject = Join-Path $workingRoot "old-glizzy-reverse-import"
    $oldScripts = Join-Path $oldGlizzyProject `
        "Assets/Wolfy_527/~ Supporting Files/Scripts"
    New-Item -ItemType Directory -Path $oldScripts -Force | Out-Null
    Write-Utf8NoBom `
        -Path (Join-Path $oldScripts "LiveMirroringSystem.cs") `
        -Value "// legacy"
    Add-MetaGuid `
        -Path (Join-Path $oldScripts "LiveMirroringSystem.cs.meta") `
        -Guid "5c54d508ba4a3ee4baa5148633885b51"
    Write-Utf8NoBom `
        -Path (Join-Path $oldScripts "GeneratedTargetMetadata.cs") `
        -Value "// legacy"
    Add-MetaGuid `
        -Path (Join-Path $oldScripts "GeneratedTargetMetadata.cs.meta") `
        -Guid "48742d3549a555842844b99523feab8f"
    $oldGhost = Join-Path $oldGlizzyProject `
        "Assets/Wolfy_527/~ Supporting Files/Ghost Material.mat"
    New-Item `
        -ItemType Directory `
        -Path ([System.IO.Path]::GetDirectoryName($oldGhost)) `
        -Force |
        Out-Null
    Write-Utf8NoBom -Path $oldGhost -Value "legacy material"
    Add-MetaGuid `
        -Path ($oldGhost + ".meta") `
        -Guid "4342400023fc9204e9fab7239dec44ef"
    Invoke-LegacyImportMigration `
        -Project $oldGlizzyProject `
        -ImportedAssets @(
            "Assets/Wolfy_527/~ Supporting Files/Scripts/LiveMirroringSystem.cs",
            "Assets/Wolfy_527/~ Supporting Files/Ghost Material.mat"
        )
    $placeholder = Join-Path $oldScripts `
        "Threadlight Components Migration Placeholder.txt"
    if (-not (Test-Path -LiteralPath $placeholder -PathType Leaf) -or
        (Test-Path -LiteralPath $oldGhost)) {
        throw "The public old Glizzy reverse-import path was not migrated safely."
    }

    $bootstrapType = $assembly.GetType(
        "Threadlight.ComponentsFallbackInstaller.ThreadlightComponentsBootstrap",
        $true
    )
    $compareVersions = $bootstrapType.GetMethod(
        "CompareVersions",
        [System.Reflection.BindingFlags]::NonPublic -bor
            [System.Reflection.BindingFlags]::Static
    )
    if ($null -eq $compareVersions) {
        throw "Could not locate the fallback semantic-version comparison."
    }

    function Assert-VersionOrder {
        param(
            [Parameter(Mandatory = $true)]
            [string] $Older,

            [Parameter(Mandatory = $true)]
            [string] $Newer,

            [int] $OlderEpoch = $sourceReleaseEpoch,

            [int] $NewerEpoch = $sourceReleaseEpoch
        )

        $comparison = [int] $compareVersions.Invoke(
            $null,
            [object[]] @($Older, $OlderEpoch, $Newer, $NewerEpoch)
        )
        if ($comparison -ge 0) {
            throw "Expected semantic version '$Older' to precede '$Newer'."
        }
    }

    $semanticVersionSequence = @(
        "1.0.0-alpha",
        "1.0.0-alpha.1",
        "1.0.0-alpha.beta",
        "1.0.0-beta",
        "1.0.0-beta.2",
        "1.0.0-beta.11",
        "1.0.0-rc.1",
        "1.0.0"
    )
    for ($index = 0; $index -lt $semanticVersionSequence.Count - 1; $index++) {
        Assert-VersionOrder `
            -Older $semanticVersionSequence[$index] `
            -Newer $semanticVersionSequence[$index + 1]
    }
    $buildMetadataComparison = [int] $compareVersions.Invoke(
        $null,
        [object[]] @(
            "1.0.0+build.1", $sourceReleaseEpoch,
            "1.0.0+build.2", $sourceReleaseEpoch)
    )
    if ($buildMetadataComparison -ne 0) {
        throw "Build metadata incorrectly affected semantic-version precedence."
    }
    Assert-VersionOrder `
        -Older "999.999.999" `
        -OlderEpoch 0 `
        -Newer "0.1.0" `
        -NewerEpoch 1
    Assert-VersionOrder `
        -Older "999.999.999" `
        -OlderEpoch 1 `
        -Newer "0.1.0" `
        -NewerEpoch 2

    $bootstrapRun = $bootstrapType.GetMethod(
        "Run",
        [System.Reflection.BindingFlags]::NonPublic -bor
            [System.Reflection.BindingFlags]::Static
    )
    if ($null -eq $bootstrapRun) {
        throw "Could not locate the fallback bootstrap execution path."
    }

    function Reset-BootstrapState {
        [UnityEditor.EditorApplication]::delayCall = $null
        [UnityEditor.PackageManager.PackageInfo]::RegisteredPackages =
            [UnityEditor.PackageManager.PackageInfo[]] @()
        [UnityEditor.AssetDatabase]::Guids.Clear()
        [UnityEditor.AssetDatabase]::DeletedAssets.Clear()
        [UnityEditor.SessionState]::Clear()
        [UnityEngine.Debug]::Messages.Clear()
        [UnityEngine.Debug]::Errors.Clear()
        [UnityEngine.Debug]::Warnings.Clear()
        foreach ($fieldName in @("running", "cleanupScheduled", "waitingForSharedUi")) {
            $bootstrapType.GetField(
                $fieldName,
                [System.Reflection.BindingFlags]::NonPublic -bor
                    [System.Reflection.BindingFlags]::Static
            ).SetValue($null, $false)
        }
        $bootstrapType.GetField(
            "bundledVersion",
            [System.Reflection.BindingFlags]::NonPublic -bor
                [System.Reflection.BindingFlags]::Static
        ).SetValue($null, $null)
    }

    function New-FallbackScenario {
        param(
            [Parameter(Mandatory = $true)]
            [string] $Name,
            [switch] $WithoutResolvedUi
        )
        $project = Join-Path $workingRoot $Name
        $assets = Join-Path $project "Assets"
        $installer = Join-Path $assets `
            "Threadlight/Components/Temp/Threadlight Components Installer"
        $payload = Join-Path $installer "ThreadlightComponentsFallback.bytes"
        $payloadSource = Join-Path $project "payload-source"
        New-Item -ItemType Directory -Path $installer -Force | Out-Null
        New-Item -ItemType Directory -Path $payloadSource -Force | Out-Null
        $payloadManifest = [ordered] @{
            name = "com.wolfyvr.threadlight.components.fallback"
            version = $sourceVersion
            compatibilityLineage = $sourceLineage
            compatibilityReleaseEpoch = $sourceReleaseEpoch
        } | ConvertTo-Json -Compress
        Write-Utf8NoBom `
            -Path (Join-Path $payloadSource "package.json") `
            -Value $payloadManifest
        $uiManifest = '{"name":"com.wolfyvr.threadlight.ui","version":"1.0.0"}'
        $stagedUi = Join-Path $payloadSource "SharedUI~"
        New-Item -ItemType Directory -Path $stagedUi -Force | Out-Null
        Write-Utf8NoBom -Path (Join-Path $stagedUi "package.json") -Value $uiManifest
        if (-not $WithoutResolvedUi) {
            $resolvedUi = Join-Path $project "Packages/com.wolfyvr.threadlight.ui"
            New-Item -ItemType Directory -Path $resolvedUi -Force | Out-Null
            Write-Utf8NoBom -Path (Join-Path $resolvedUi "package.json") -Value $uiManifest
        }
        $payloadScripts = [ordered] @{
            "Runtime/LiveMirroringSystem.cs" =
                "5c54d508ba4a3ee4baa5148633885b51"
            "Runtime/AuthoringOnlyComponent.cs" =
                "40218417691f9c041a2ac01d1b9d1a5c"
        }
        foreach ($entry in $payloadScripts.GetEnumerator()) {
            $scriptPath = Join-Path $payloadSource $entry.Key
            New-Item `
                -ItemType Directory `
                -Path ([System.IO.Path]::GetDirectoryName($scriptPath)) `
                -Force |
                Out-Null
            Write-Utf8NoBom -Path $scriptPath -Value "// payload script"
            Write-Utf8NoBom `
                -Path ($scriptPath + ".meta") `
                -Value ("guid: " + $entry.Value)
        }
        Compress-Archive `
            -Path (Join-Path $payloadSource "*") `
            -DestinationPath ($payload + ".zip")
        Move-Item -LiteralPath ($payload + ".zip") -Destination $payload
        return $project
    }

    Reset-BootstrapState
    $uiFirstScenario = New-FallbackScenario -Name "shared-ui-before-components" -WithoutResolvedUi
    [UnityEngine.Application]::dataPath = Join-Path $uiFirstScenario "Assets"
    $bootstrapRun.Invoke($null, @())
    if (-not (Test-Path (Join-Path $uiFirstScenario "Packages/com.wolfyvr.threadlight.ui/package.json")) -or
        (Test-Path (Join-Path $uiFirstScenario "Assets/Threadlight/Components/Fallback"))) {
        throw "Shared UI must install before exposing the Components fallback."
    }
    # This stub verifies bootstrap ordering, not Unity compilation. The real
    # package matrix must separately prove that the editor dependency compiles.
    $uiAssemblyName = [System.Reflection.AssemblyName]::new("Threadlight.EditorUI")
    [System.Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly(
        $uiAssemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run) | Out-Null
    $bootstrapRun.Invoke($null, @())
    if (-not (Test-Path (Join-Path $uiFirstScenario "Assets/Threadlight/Components/Fallback"))) {
        throw "Components did not resume installation after the shared UI became available."
    }
    Reset-BootstrapState
    $unknownUiScenario = New-FallbackScenario -Name "unknown-shared-ui-destination" -WithoutResolvedUi
    [UnityEngine.Application]::dataPath = Join-Path $unknownUiScenario "Assets"
    $unknownUiRoot = Join-Path $unknownUiScenario "Packages/com.wolfyvr.threadlight.ui"
    New-Item -ItemType Directory -Path $unknownUiRoot -Force | Out-Null
    Write-Utf8NoBom -Path (Join-Path $unknownUiRoot "creator.txt") -Value "preserve me"
    $bootstrapRun.Invoke($null, @())
    if ([UnityEngine.Debug]::Errors.Count -eq 0 -or
        (Get-Content (Join-Path $unknownUiRoot "creator.txt") -Raw) -ne "preserve me" -or
        (Test-Path (Join-Path $unknownUiScenario "Assets/Threadlight/Components/Fallback"))) {
        throw "An unrecognized UI destination was not preserved and refused."
    }

    function Set-CompatibleFallback {
        param(
            [Parameter(Mandatory = $true)]
            [string] $Project,

            [Parameter(Mandatory = $true)]
            [string] $Version,

            [Parameter(Mandatory = $false)]
            [switch] $WithSentinel
        )

        $fallbackRoot = Join-Path `
            $Project `
            "Assets/Threadlight/Components/Fallback"
        New-Item -ItemType Directory -Path $fallbackRoot -Force | Out-Null
        $fallbackManifest = [ordered] @{
            name = "com.wolfyvr.threadlight.components.fallback"
            version = $Version
            compatibilityLineage = $sourceLineage
            compatibilityReleaseEpoch = $sourceReleaseEpoch
        } | ConvertTo-Json -Compress
        Write-Utf8NoBom `
            -Path (Join-Path $fallbackRoot "package.json") `
            -Value $fallbackManifest

        $compatibleGuidFiles = [ordered] @{
            "Runtime/LiveMirroringSystem.cs.meta" =
                "5c54d508ba4a3ee4baa5148633885b51"
            "Runtime/GeneratedTargetMetadata.cs.meta" =
                "48742d3549a555842844b99523feab8f"
            "Runtime/GeneratedEditorOnlyObject.cs.meta" =
                "6a339ada66db0524bb16d5ed1fbe64bc"
            "Runtime/AuthoringOnlyComponent.cs.meta" =
                "40218417691f9c041a2ac01d1b9d1a5c"
            "Runtime/GeneratedHierarchyMetadata.cs.meta" =
                "0bc840ca6f774dfc91cc06af65dc5b45"
            "Runtime/PrefabId.cs.meta" =
                "5d8f4687c2084716afeb3da11a1b050d"
            "Ghost Material.mat.meta" =
                "4342400023fc9204e9fab7239dec44ef"
        }
        foreach ($entry in $compatibleGuidFiles.GetEnumerator()) {
            $metaPath = Join-Path $fallbackRoot $entry.Key
            New-Item `
                -ItemType Directory `
                -Path ([System.IO.Path]::GetDirectoryName($metaPath)) `
                -Force |
                Out-Null
            Write-Utf8NoBom -Path $metaPath -Value ("guid: " + $entry.Value)
        }

        if ($WithSentinel) {
            Write-Utf8NoBom `
                -Path (Join-Path $fallbackRoot "creator-sentinel.txt") `
                -Value "preserve when the installed fallback is retained"
        }

        return $fallbackRoot
    }

    function New-RegisteredPackageFixture {
        param(
            [Parameter(Mandatory = $true)]
            [string] $Project,

            [Parameter(Mandatory = $true)]
            [string] $Name,

            [Parameter(Mandatory = $true)]
            [string] $Version,

            [Parameter(Mandatory = $true)]
            [string] $Lineage,

            [Parameter(Mandatory = $true)]
            [int] $ReleaseEpoch
        )

        $packageRoot = Join-Path $Project ("Packages/" + $Name)
        New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
        $manifest = [ordered] @{
            name = $Name
            version = $Version
            compatibilityLineage = $Lineage
            compatibilityReleaseEpoch = $ReleaseEpoch
        } | ConvertTo-Json -Compress
        Write-Utf8NoBom `
            -Path (Join-Path $packageRoot "package.json") `
            -Value $manifest

        $package = [UnityEditor.PackageManager.PackageInfo]::new()
        $package.name = $Name
        $package.version = $Version
        $package.resolvedPath = $packageRoot
        return $package
    }

    function Set-FullFallback {
        param(
            [Parameter(Mandatory = $true)]
            [string] $Project,

            [Parameter(Mandatory = $true)]
            [string] $Version
        )

        $fallbackRoot = Join-Path `
            $Project `
            "Assets/Threadlight/Components/Fallback"
        New-Item -ItemType Directory -Path $fallbackRoot -Force | Out-Null
        Get-ChildItem -LiteralPath $root -Force |
            Where-Object {
                $_.Name -ne ".git" -and
                $_.Name -ne "Samples~"
            } |
            ForEach-Object {
                Copy-Item `
                    -LiteralPath $_.FullName `
                    -Destination $fallbackRoot `
                    -Recurse `
                    -Force
            }

        $fallbackManifestPath = Join-Path $fallbackRoot "package.json"
        $fallbackManifest = Get-Content `
            -LiteralPath $fallbackManifestPath `
            -Raw |
            ConvertFrom-Json
        $fallbackManifest.name =
            "com.wolfyvr.threadlight.components.fallback"
        $fallbackManifest.version = $Version
        Write-Utf8NoBom `
            -Path $fallbackManifestPath `
            -Value ($fallbackManifest | ConvertTo-Json -Depth 20)
        Write-Utf8NoBom `
            -Path (Join-Path $fallbackRoot "creator-sentinel.txt") `
            -Value "preserve when the installed fallback is quarantined"
        return $fallbackRoot
    }

    function Assert-FallbackVersion {
        param(
            [Parameter(Mandatory = $true)]
            [string] $FallbackRoot,

            [Parameter(Mandatory = $true)]
            [string] $ExpectedVersion
        )

        $manifest = Get-Content `
            -LiteralPath (Join-Path $FallbackRoot "package.json") `
            -Raw |
            ConvertFrom-Json
        if ($manifest.version -ne $ExpectedVersion) {
            throw (
                "Expected fallback version $ExpectedVersion but found " +
                "$($manifest.version). Messages: " +
                ([string]::Join(" | ", [UnityEngine.Debug]::Messages)) +
                ". Warnings: " +
                ([string]::Join(" | ", [UnityEngine.Debug]::Warnings)) +
                ". Errors: " +
                ([string]::Join(" | ", [UnityEngine.Debug]::Errors))
            )
        }
    }

    Reset-BootstrapState
    $fallbackOnly = New-FallbackScenario -Name "fallback-only"
    [UnityEngine.Application]::dataPath = Join-Path $fallbackOnly "Assets"
    $bootstrapRun.Invoke($null, @())
    $installedFallbackManifest = Join-Path `
        $fallbackOnly `
        "Assets/Threadlight/Components/Fallback/package.json"
    if (-not (Test-Path -LiteralPath $installedFallbackManifest -PathType Leaf)) {
        throw (
            "Fallback-only installation did not install Threadlight Components. " +
            ([string]::Join(" | ", [UnityEngine.Debug]::Errors))
        )
    }
    Assert-FallbackVersion `
        -FallbackRoot ([System.IO.Path]::GetDirectoryName(
            $installedFallbackManifest)) `
        -ExpectedVersion $sourceVersion

    Reset-BootstrapState
    $relocatedScenario = New-FallbackScenario -Name "relocated-script-migration"
    $relocatedScript = Join-Path `
        $relocatedScenario `
        "Assets/Creator Tools/Glizzy Copy/LiveMirroringSystem.cs"
    New-Item `
        -ItemType Directory `
        -Path ([System.IO.Path]::GetDirectoryName($relocatedScript)) `
        -Force |
        Out-Null
    Write-Utf8NoBom -Path $relocatedScript -Value "// relocated owned script"
    Write-Utf8NoBom `
        -Path ($relocatedScript + ".meta") `
        -Value "guid: 5c54d508ba4a3ee4baa5148633885b51"
    $unrelatedScript = Join-Path `
        $relocatedScenario `
        "Assets/Creator Tools/Other/LiveMirroringSystem.cs"
    New-Item `
        -ItemType Directory `
        -Path ([System.IO.Path]::GetDirectoryName($unrelatedScript)) `
        -Force |
        Out-Null
    Write-Utf8NoBom -Path $unrelatedScript -Value "// unrelated same-name script"
    Write-Utf8NoBom `
        -Path ($unrelatedScript + ".meta") `
        -Value "guid: 00000000000000000000000000000000"
    [UnityEngine.Application]::dataPath = Join-Path $relocatedScenario "Assets"
    $bootstrapRun.Invoke($null, @())
    if (Test-Path -LiteralPath $relocatedScript) {
        throw "A relocated script with an owned GUID was not migrated."
    }
    if (-not (Test-Path -LiteralPath $unrelatedScript -PathType Leaf)) {
        throw "An unrelated same-name script was incorrectly migrated."
    }
    $relocatedBackup = Get-ChildItem `
        -LiteralPath (Join-Path $relocatedScenario "Legacy Package Backups") `
        -Filter "LiveMirroringSystem.cs" `
        -File `
        -Recurse |
        Select-Object -First 1
    if ($null -eq $relocatedBackup) {
        throw "The relocated owned script was not preserved in a backup."
    }

    Reset-BootstrapState
    $upgradeScenario = New-FallbackScenario -Name "fallback-upgrade"
    $upgradeFallback = Set-CompatibleFallback `
        -Project $upgradeScenario `
        -Version $previousFallbackVersion `
        -WithSentinel
    [UnityEngine.Application]::dataPath = Join-Path $upgradeScenario "Assets"
    $bootstrapRun.Invoke($null, @())
    Assert-FallbackVersion `
        -FallbackRoot $upgradeFallback `
        -ExpectedVersion $sourceVersion
    if (Test-Path -LiteralPath (
            Join-Path $upgradeFallback "creator-sentinel.txt")) {
        throw "Older fallback content was not replaced by the bundled version."
    }

    Reset-BootstrapState
    $equalScenario = New-FallbackScenario -Name "fallback-equal"
    $equalFallback = Set-CompatibleFallback `
        -Project $equalScenario `
        -Version $sourceVersion `
        -WithSentinel
    [UnityEngine.Application]::dataPath = Join-Path $equalScenario "Assets"
    $bootstrapRun.Invoke($null, @())
    Assert-FallbackVersion `
        -FallbackRoot $equalFallback `
        -ExpectedVersion $sourceVersion
    if (-not (Test-Path -LiteralPath (
            Join-Path $equalFallback "creator-sentinel.txt") -PathType Leaf)) {
        throw "Equal compatible fallback content was unnecessarily replaced."
    }

    Reset-BootstrapState
    $newerScenario = New-FallbackScenario -Name "fallback-newer"
    $newerFallback = Set-CompatibleFallback `
        -Project $newerScenario `
        -Version $newerFallbackVersion `
        -WithSentinel
    [UnityEngine.Application]::dataPath = Join-Path $newerScenario "Assets"
    $bootstrapRun.Invoke($null, @())
    Assert-FallbackVersion `
        -FallbackRoot $newerFallback `
        -ExpectedVersion $newerFallbackVersion
    if (-not (Test-Path -LiteralPath (
            Join-Path $newerFallback "creator-sentinel.txt") -PathType Leaf)) {
        throw "Newer compatible fallback content was downgraded."
    }

    Reset-BootstrapState
    $managedScenario = New-FallbackScenario -Name "managed-vpm"
    [UnityEngine.Application]::dataPath = Join-Path $managedScenario "Assets"
    $managedFallback = Set-FullFallback `
        -Project $managedScenario `
        -Version "1.0.0"
    $managedFallbackAssemblyNames = @(
        Get-ChildItem -LiteralPath $managedFallback -Recurse -Filter "*.asmdef" |
            ForEach-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).name }
    )
    $expectedFallbackAssemblyNames = @(
        "Threadlight.Components",
        "Threadlight.Components.Support.Editor",
        "Threadlight.Components.Mirroring.Editor"
    )
    if (@(Compare-Object $expectedFallbackAssemblyNames $managedFallbackAssemblyNames).Count -ne 0) {
        throw (
            "The managed coexistence fixture must contain the runtime, installer, " +
            "and customer mirroring editor assemblies. Found: $managedFallbackAssemblyNames.")
    }
    $managedRelocatedScript = Join-Path `
        $managedScenario `
        "Assets/Relocated Old Copy/AuthoringOnlyComponent.cs"
    New-Item `
        -ItemType Directory `
        -Path ([System.IO.Path]::GetDirectoryName($managedRelocatedScript)) `
        -Force |
        Out-Null
    Write-Utf8NoBom -Path $managedRelocatedScript -Value "// old embedded copy"
    Write-Utf8NoBom `
        -Path ($managedRelocatedScript + ".meta") `
        -Value "guid: 40218417691f9c041a2ac01d1b9d1a5c"
    $managedPackage = [UnityEditor.PackageManager.PackageInfo]::new()
    $managedPackage.name = "com.wolfyvr.threadlight.components"
    $managedPackage.version = $sourceVersion
    [UnityEditor.PackageManager.PackageInfo]::RegisteredPackages =
        [UnityEditor.PackageManager.PackageInfo[]] @($managedPackage)
    $bootstrapRun.Invoke($null, @())
    if (Test-Path -LiteralPath $managedFallback) {
        throw "Managed VPM left a recognized fallback package under Assets."
    }
    $managedBackupRoot = Join-Path `
        $managedScenario `
        "Library/Threadlight/Legacy Package Backups/Threadlight Components Fallback"
    $managedFallbackBackup = Get-ChildItem `
        -LiteralPath $managedBackupRoot `
        -Recurse `
        -File `
        -Filter "creator-sentinel.txt" |
        Select-Object -First 1
    if ($null -eq $managedFallbackBackup) {
        throw "Managed VPM did not retain a recoverable fallback backup."
    }
    $managedBackupAssemblyCount = @(
        Get-ChildItem `
            -LiteralPath $managedBackupRoot `
            -Recurse `
            -Filter "*.asmdef"
    ).Count
    if ($managedBackupAssemblyCount -ne $expectedFallbackAssemblyNames.Count) {
        throw (
            "The quarantined fallback did not retain all customer assemblies. " +
            "Found $managedBackupAssemblyCount.")
    }
    if (Test-Path -LiteralPath $managedRelocatedScript) {
        throw "Managed VPM did not migrate a relocated non-VPM script copy."
    }

    Reset-BootstrapState
    $supportedVpmScenario = New-FallbackScenario `
        -Name "supported-vpm-older-than-bundle"
    [UnityEngine.Application]::dataPath = Join-Path `
        $supportedVpmScenario `
        "Assets"
    $newerBundledFallback = Set-FullFallback `
        -Project $supportedVpmScenario `
        -Version $sourceVersion
    $supportedVpmPackage = [UnityEditor.PackageManager.PackageInfo]::new()
    $supportedVpmPackage.name = "com.wolfyvr.threadlight.components"
    $supportedVpmPackage.version = $previousFallbackVersion
    [UnityEditor.PackageManager.PackageInfo]::RegisteredPackages =
        [UnityEditor.PackageManager.PackageInfo[]] @($supportedVpmPackage)
    $bootstrapRun.Invoke($null, @())
    if (Test-Path -LiteralPath $newerBundledFallback) {
        throw (
            "Supported VPM $previousFallbackVersion lost authority to the " +
            "newer bundled fallback $sourceVersion.")
    }
    $supportedVpmBackupRoot = Join-Path `
        $supportedVpmScenario `
        "Library/Threadlight/Legacy Package Backups/Threadlight Components Fallback"
    $supportedVpmFallbackBackup = Get-ChildItem `
        -LiteralPath $supportedVpmBackupRoot `
        -Recurse `
        -File `
        -Filter "creator-sentinel.txt" |
        Select-Object -First 1
    if ($null -eq $supportedVpmFallbackBackup) {
        throw (
            "Supported VPM authority did not preserve the newer bundled " +
            "fallback in a recoverable backup.")
    }

    Reset-BootstrapState
    $renamedScenario = New-FallbackScenario -Name "renamed-lineage-vpm"
    [UnityEngine.Application]::dataPath = Join-Path $renamedScenario "Assets"
    $renamedPackage = New-RegisteredPackageFixture `
        -Project $renamedScenario `
        -Name "com.example.future.components" `
        -Version $sourceVersion `
        -Lineage $sourceLineage `
        -ReleaseEpoch $sourceReleaseEpoch
    [UnityEditor.PackageManager.PackageInfo]::RegisteredPackages =
        [UnityEditor.PackageManager.PackageInfo[]] @($renamedPackage)
    $bootstrapRun.Invoke($null, @())
    if (Test-Path -LiteralPath (Join-Path $renamedScenario `
            "Assets/Threadlight/Components/Fallback")) {
        throw "A renamed managed package with matching lineage lost VPM authority."
    }

    Reset-BootstrapState
    $differentLineageScenario = New-FallbackScenario `
        -Name "different-lineage-package"
    [UnityEngine.Application]::dataPath = Join-Path `
        $differentLineageScenario "Assets"
    $differentLineagePackage = New-RegisteredPackageFixture `
        -Project $differentLineageScenario `
        -Name "com.example.unrelated.components" `
        -Version "999.0.0" `
        -Lineage "6b71b6af-82cd-42ae-9972-2fa9329616e8" `
        -ReleaseEpoch 999
    [UnityEditor.PackageManager.PackageInfo]::RegisteredPackages =
        [UnityEditor.PackageManager.PackageInfo[]] @($differentLineagePackage)
    $bootstrapRun.Invoke($null, @())
    Assert-FallbackVersion `
        -FallbackRoot (Join-Path $differentLineageScenario `
            "Assets/Threadlight/Components/Fallback") `
        -ExpectedVersion $sourceVersion

    Reset-BootstrapState
    $legacyVpmScenario = New-FallbackScenario -Name "legacy-vpm-high-semver"
    [UnityEngine.Application]::dataPath = Join-Path $legacyVpmScenario "Assets"
    $legacyPackage = [UnityEditor.PackageManager.PackageInfo]::new()
    $legacyPackage.name = "dev.avatar-tools.prop-components"
    $legacyPackage.version = "999.999.999"
    [UnityEditor.PackageManager.PackageInfo]::RegisteredPackages =
        [UnityEditor.PackageManager.PackageInfo[]] @($legacyPackage)
    $bootstrapRun.Invoke($null, @())
    if (Test-Path -LiteralPath (Join-Path $legacyVpmScenario `
            "Assets/Threadlight/Components/Fallback")) {
        throw "An explicitly recognized legacy VPM package lost managed authority."
    }
    if ([UnityEngine.Debug]::Warnings.Count -eq 0) {
        throw "Epoch zero legacy VPM did not request the newer ThreadLight line."
    }

    Reset-BootstrapState
    $lightweightScenario = New-FallbackScenario -Name "lightweight-builder"
    [UnityEngine.Application]::dataPath = Join-Path $lightweightScenario "Assets"
    $lightweightPackage = [UnityEditor.PackageManager.PackageInfo]::new()
    $lightweightPackage.name = "com.wolfyvr.threadlight.mirroring"
    $lightweightPackage.version = "1.0.5"
    [UnityEditor.PackageManager.PackageInfo]::RegisteredPackages =
        [UnityEditor.PackageManager.PackageInfo[]] @($lightweightPackage)
    $builderRelocatedScript = Join-Path `
        $lightweightScenario `
        "Assets/Old Embedded Copy/LiveMirroringSystem.cs"
    New-Item `
        -ItemType Directory `
        -Path ([System.IO.Path]::GetDirectoryName($builderRelocatedScript)) `
        -Force |
        Out-Null
    Write-Utf8NoBom -Path $builderRelocatedScript -Value "// old embedded copy"
    Write-Utf8NoBom `
        -Path ($builderRelocatedScript + ".meta") `
        -Value "guid: 5c54d508ba4a3ee4baa5148633885b51"
    $bootstrapRun.Invoke($null, @())
    $lightweightPayload = Join-Path `
        $lightweightScenario `
        "Assets/Threadlight/Components/Temp/Threadlight Components Installer/ThreadlightComponentsFallback.bytes"
    if (-not (Test-Path -LiteralPath $lightweightPayload -PathType Leaf)) {
        throw "Threadlight Mirroring did not retain its export payload."
    }
    if (Test-Path -LiteralPath $builderRelocatedScript) {
        throw "Builder preservation skipped relocated non-VPM script cleanup."
    }

    $legacyScriptsFolder =
        "Assets\Wolfy_527\~ Supporting Files\Scripts"
    if ($null -eq $sourceManifest.legacyFolders.PSObject.Properties[$legacyScriptsFolder] -or
        $sourceManifest.legacyPackages -notcontains
            "com.wolfyvr.threadlight.components.fallback") {
        throw "Managed VPM package does not declare Glizzy and fallback takeover cleanup."
    }

    Write-Host (
        "Shared UI resolution ordering and unknown destination preservation, " +
        "semantic-version ordering, reverse-import migration, fallback install, " +
        "relocated-script migration, " +
        "upgrade, equal/newer " +
        "retention, managed VPM including legacy and renamed-lineage authority, " +
        "Builder preservation, and takeover " +
        "declaration matrix passed."
    )
}
finally {
    if (Test-Path -LiteralPath $workingRoot) {
        Remove-Item -LiteralPath $workingRoot -Recurse -Force
    }
}
