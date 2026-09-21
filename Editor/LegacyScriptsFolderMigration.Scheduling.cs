namespace Threadlight.Components.Editor
{
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static partial class LegacyScriptsFolderMigration
{
    private static void ScheduleMigrationRetry()
    {
        if (migrationRetryScheduled ||
            migrationAttempts >= MaximumMigrationAttempts)
        {
            return;
        }

        migrationRetryScheduled = true;
        nextMigrationAttemptAt =
            EditorApplication.timeSinceStartup + MigrationRetrySeconds;
        EditorApplication.update += RetryMigrationWhenQuiet;
    }

    private static void RetryMigrationWhenQuiet()
    {
        if (EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            EditorApplication.timeSinceStartup < nextMigrationAttemptAt)
        {
            return;
        }

        EditorApplication.update -= RetryMigrationWhenQuiet;
        migrationRetryScheduled = false;
        migrationAttempts++;
        TryMigrate();
    }

    private static void CancelMigrationRetry()
    {
        if (migrationRetryScheduled)
        {
            EditorApplication.update -= RetryMigrationWhenQuiet;
            migrationRetryScheduled = false;
        }

        migrationAttempts = 0;
    }

    private static void ScheduleRefresh()
    {
        if (refreshScheduled)
            return;

        refreshScheduled = true;
        EditorApplication.delayCall += RefreshAfterMigration;
    }

    private static void RefreshAfterMigration()
    {
        refreshScheduled = false;
        AssetDatabase.Refresh(ImportAssetOptions.Default);
    }

    private static void ScheduleTemporaryCleanup()
    {
        if (cleanupScheduled)
            return;

        if (cleanupWaitingForProjectChange)
        {
            EditorApplication.projectChanged -= RetryCleanupAfterProjectChange;
            cleanupWaitingForProjectChange = false;
        }
        cleanupAttempts = 0;
        ScheduleCleanupAttempt();
    }

    private static void ScheduleCleanupAttempt()
    {
        cleanupScheduled = true;
        cleanupQuietSince = EditorApplication.timeSinceStartup;
        EditorApplication.update += TryCleanupTemporaryAssets;
        EditorApplication.projectChanged += ResetCleanupQuietPeriod;
    }

    private static void ScheduleTemporaryCleanupIfNeeded(string projectRoot)
    {
        // The Builder keeps the payload available for product exports. Projects
        // without a payload need no quiet-period update callback at all.
        if (string.IsNullOrWhiteSpace(projectRoot) ||
            IsBuilderInstalled(projectRoot))
        {
            return;
        }

        string payloadPath = Path.GetFullPath(
            Path.Combine(projectRoot, InstallerPayloadPath));
        if (!File.Exists(payloadPath))
            return;

        ScheduleTemporaryCleanup();
    }

    private static void TryCleanupTemporaryAssets()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            cleanupQuietSince = EditorApplication.timeSinceStartup;
            return;
        }

        if (EditorApplication.timeSinceStartup - cleanupQuietSince <
            CleanupQuietSeconds)
            return;

        EditorApplication.update -= TryCleanupTemporaryAssets;
        EditorApplication.projectChanged -= ResetCleanupQuietPeriod;
        cleanupScheduled = false;

        bool cleanupFailed = false;
        string projectRoot =
            Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrWhiteSpace(projectRoot))
            return;

        if (!IsBuilderInstalled(projectRoot) &&
            !CleanupOwnedInstallerPayload())
        {
            cleanupFailed = true;
        }

        if (cleanupFailed)
        {
            cleanupAttempts++;
            if (cleanupAttempts < MaximumCleanupAttempts)
            {
                ScheduleCleanupAttempt();
                return;
            }

            cleanupWaitingForProjectChange = true;
            EditorApplication.projectChanged += RetryCleanupAfterProjectChange;
            Debug.LogWarning(
                "[ThreadLight Components] Could not remove temporary installer " +
                "files after " + MaximumCleanupAttempts + " attempts. Cleanup " +
                "will retry after the project changes or Unity reloads. Creator " +
                "assets were left unchanged.");
        }
    }

    private static void RetryCleanupAfterProjectChange()
    {
        EditorApplication.projectChanged -= RetryCleanupAfterProjectChange;
        cleanupWaitingForProjectChange = false;
        ScheduleTemporaryCleanup();
    }

    private static void ResetCleanupQuietPeriod()
    {
        cleanupQuietSince = EditorApplication.timeSinceStartup;
    }

    private static bool CleanupOwnedInstallerPayload()
    {
        string projectRoot =
            Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrWhiteSpace(projectRoot))
            return false;

        string payloadPath = Path.GetFullPath(
            Path.Combine(projectRoot, InstallerPayloadPath));
        if (!File.Exists(payloadPath))
            return true;

        if (!string.Equals(
                AssetDatabase.AssetPathToGUID(InstallerPayloadPath),
                InstallerPayloadGuid,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return AssetDatabase.DeleteAsset(InstallerPayloadPath) ||
               !File.Exists(payloadPath);
    }

    private static bool IsBuilderInstalled(string projectRoot)
    {
        // Package Manager can resolve packages from the project, an external
        // local folder, or its cache. Their registered package names are the
        // authoritative installation state; a fixed Packages/<id> folder only
        // describes the embedded case.
        try
        {
            string[] packageNames = registeredPackageNamesProvider?.Invoke();
            if (packageNames == null)
                return true;

            for (int index = 0; index < packageNames.Length; index++)
            {
                string packageName = packageNames[index];
                if (string.Equals(
                        packageName,
                        BuilderPackageName,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        packageName,
                        ThreadlightMirroringPackageName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[ThreadLight Components] Could not confirm whether a ThreadLight " +
                "Builder is installed, so temporary installer files were kept. " +
                "Unity reported: " + exception.Message);
            return true;
        }
    }

    private static string[] GetRegisteredPackageNames()
    {
        UnityEditor.PackageManager.PackageInfo[] packages =
            UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
        if (packages == null)
            return null;

        string[] packageNames = new string[packages.Length];
        for (int index = 0; index < packages.Length; index++)
            packageNames[index] = packages[index]?.name;
        return packageNames;
    }

}
}
