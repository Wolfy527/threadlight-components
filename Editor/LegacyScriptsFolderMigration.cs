namespace Threadlight.Components.Editor
{
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
/// <summary>
/// Moves recognized pre-VPM assets out of Unity's compilation path while
/// preserving a timestamped backup. Unknown folders are never claimed.
/// </summary>
internal static partial class LegacyScriptsFolderMigration
{
    // The shipped Glizzy predates the package stack and is the sole supported
    // legacy source. Exact GUID checks below prevent claiming unrelated files.
    private const string LegacyScriptsPath =
        "Assets/Wolfy_527/~ Supporting Files/Scripts";
    private const string LegacyScriptsGuid =
        "1a754f8d169daa9408e3740cfeeab3aa";
    private const string LegacyGhostMaterialPath =
        "Assets/Wolfy_527/~ Supporting Files/Ghost Material.mat";
    private const string LegacyGhostMaterialGuid =
        "4342400023fc9204e9fab7239dec44ef";
    private const string LegacyFallbackPath =
        "Assets/Wolfy_527/~ Supporting Files/Prefab Components Fallback";
    private const string LegacyFallbackPackageName =
        "com.wolfy527.prefab-components.fallback";
    private const string LegacyFallbackLiveMirroringGuid =
        "b4ef5c021c9e12e45ac198b875874db0";
    private const string LegacyFallbackAuthoringOnlyGuid =
        "076fe599df6ed2c478fcc7948ea0f20e";
    private const string LegacyFallbackTargetMetadataGuid =
        "e70a5bbf5de42b0478484ce9a4024548";
    private const string LegacyFallbackGeneratedObjectGuid =
        "c9e72b6f6ebf0944cbc8e46be25353e4";
    private const string LegacyFallbackHierarchyMetadataGuid =
        "f09ee1c973e46e642a90ceb8782efd1b";
    private const string LegacyFallbackPrefabIdGuid =
        "139e0c1a87d72dc488222ee8b21bb47e";
    private const string LegacyFallbackGhostMaterialGuid =
        "4b0d26895d37a904eb40cd0d68cdb0e7";
    private const string LiveMirroringScriptGuid =
        "5c54d508ba4a3ee4baa5148633885b51";
    private const string GeneratedTargetMetadataGuid =
        "48742d3549a555842844b99523feab8f";
    private const string AuthoringOnlyComponentGuid =
        "40218417691f9c041a2ac01d1b9d1a5c";
    private const string PlaceholderMarkerName =
        "Threadlight Components Migration Placeholder.txt";
    private const string PlaceholderMarkerContents =
        "Threadlight Components migration placeholder";
    private const string PlaceholderScriptContents =
        "// Threadlight Components migration placeholder. Intentionally inert.";
    private const string InstallerRoot =
        "Assets/Threadlight/Components/Installer";
    private const string InstallerPayloadPath =
        InstallerRoot + "/ThreadlightComponentsFallback.bytes";
    private const string InstallerPayloadGuid =
        "33bd79b26cc644e4896285530a240b2b";
    private const string BuilderPackageName =
        "com.wolfyvr.threadlight.builder";
    private const string ThreadlightMirroringPackageName =
        "com.wolfyvr.threadlight.mirroring";
    private const double CleanupQuietSeconds = 5.0;
    private const int MaximumCleanupAttempts = 3;
    private const int MaximumMigrationAttempts = 3;
    private const double MigrationRetrySeconds = 0.5;

    private static bool cleanupScheduled;
    private static bool cleanupWaitingForProjectChange;
    private static bool migrationRunning;
    private static bool refreshScheduled;
    private static bool migrationRetryScheduled;
    private static int migrationAttempts;
    private static double nextMigrationAttemptAt;
    private static int cleanupAttempts;
    private static double cleanupQuietSince;
    private static Func<string[]> registeredPackageNamesProvider =
        GetRegisteredPackageNames;

    static LegacyScriptsFolderMigration()
    {
        TryRestoreInstalledPackage();
        AssetDatabase.importPackageStarted += HandlePackageImportStarted;
        AssetDatabase.onImportPackageItemsCompleted +=
            HandlePackageImportItemsCompleted;
        AssetDatabase.importPackageCompleted += HandlePackageImportCompleted;
        AssetDatabase.importPackageCancelled += HandlePackageImportCancelled;
        AssetDatabase.importPackageFailed += HandlePackageImportFailed;
        EditorApplication.delayCall += TryMigrate;
    }

    private static void HandlePackageImportStarted(string packageName)
    {
        CaptureInstalledPackage();
    }

    private static void HandlePackageImportItemsCompleted(
        string[] importedAssets)
    {
        TryRestoreInstalledPackage();
        HandleImportedAssets(importedAssets);
    }

    private static void HandlePackageImportCompleted(string packageName)
    {
        TryRestoreInstalledPackage();
        TryMigrate();
    }

    private static void HandlePackageImportCancelled(string packageName)
    {
        TryRestoreInstalledPackage();
    }

    private static void HandlePackageImportFailed(
        string packageName,
        string errorMessage)
    {
        TryRestoreInstalledPackage();
    }

    /// <summary>
    /// Quarantines recognized legacy assets during the import callback, before
    /// Unity can compile their obsolete scripts alongside the managed package.
    /// </summary>
    internal static void HandleImportedAssets(string[] importedAssets)
    {
        if (importedAssets == null)
            return;

        foreach (string importedAsset in importedAssets)
        {
            if (IsLegacyImportPath(importedAsset))
            {
                CancelMigrationRetry();
                TryMigrate();
                return;
            }
        }
    }

    private static void TryMigrate()
    {
        if (migrationRunning)
            return;

        migrationRunning = true;
        try
        {
            TryMigrateCore();
        }
        finally
        {
            migrationRunning = false;
        }
    }

}

internal sealed class LegacyScriptsImportPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        LegacyScriptsFolderMigration.HandleImportedAssets(importedAssets);
    }
}
}
