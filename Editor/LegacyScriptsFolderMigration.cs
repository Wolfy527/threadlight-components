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
internal static class LegacyScriptsFolderMigration
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
    private const string BuilderPackagePath =
        "Packages/com.wolfyvr.threadlight.builder";
    private const string ThreadlightMirroringPackagePath =
        "Packages/com.wolfyvr.threadlight.mirroring";
    private const double CleanupQuietSeconds = 5.0;
    private const int MaximumCleanupAttempts = 3;

    private static bool cleanupScheduled;
    private static bool cleanupWaitingForProjectChange;
    private static int cleanupAttempts;
    private static double cleanupQuietSince;

    static LegacyScriptsFolderMigration()
    {
        EditorApplication.delayCall += TryMigrate;
    }

    private static void TryMigrate()
    {
        // Discovery and path validation are completed before any file moves.
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            Debug.LogError(
                "[ThreadLight Components] Could not locate the Unity project " +
                "root, so legacy assets were left unchanged.");
            return;
        }

        string legacyScriptsPath = Path.GetFullPath(
            Path.Combine(projectRoot, LegacyScriptsPath));
        string placeholderMarkerPath = Path.Combine(
            legacyScriptsPath,
            PlaceholderMarkerName);
        bool hasPlaceholders = IsOwnedPlaceholder(placeholderMarkerPath);
        bool hasLegacyScripts =
            Directory.Exists(legacyScriptsPath) && !hasPlaceholders;
        string legacyGhostPath = Path.GetFullPath(
            Path.Combine(projectRoot, LegacyGhostMaterialPath));
        bool hasLegacyGhost = File.Exists(legacyGhostPath);

        if (!hasLegacyScripts && !hasLegacyGhost)
        {
            ScheduleTemporaryCleanupIfNeeded(projectRoot);
            return;
        }

        string assetsRoot = Path.GetFullPath(Application.dataPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!legacyScriptsPath.StartsWith(
                assetsRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !legacyGhostPath.StartsWith(
                assetsRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError(
                "[ThreadLight Components] Refused to migrate a legacy path " +
                "outside this project's Assets folder.");
            return;
        }

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string backupParent = Path.Combine(
            projectRoot,
            "Legacy Package Backups",
            "ThreadLight Components",
            timestamp + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string backupScriptsPath = Path.Combine(backupParent, "Scripts");
        string legacyScriptsMetaPath = legacyScriptsPath + ".meta";
        string backupScriptsMetaPath = backupScriptsPath + ".meta";
        string backupGhostPath = Path.Combine(
            backupParent,
            "Ghost Material.mat");
        string legacyGhostMetaPath = legacyGhostPath + ".meta";
        string backupGhostMetaPath = backupGhostPath + ".meta";
        bool scriptsMoved = false;
        bool ghostMoved = false;

        try
        {
            // Only assets with known GUIDs or a recognized legacy signature are
            // eligible. Custom folders at the same path remain untouched.
            if (hasLegacyScripts &&
                IsRecognizedLegacyScripts(legacyScriptsPath))
            {
                Directory.CreateDirectory(backupParent);
                Directory.Move(legacyScriptsPath, backupScriptsPath);
                scriptsMoved = true;

                if (File.Exists(legacyScriptsMetaPath))
                {
                    File.Move(
                        legacyScriptsMetaPath,
                        backupScriptsMetaPath);
                }

                CreateCompilePlaceholders(
                    backupScriptsPath,
                    legacyScriptsPath);
            }
            else if (hasLegacyScripts)
            {
                Debug.LogWarning(
                    "[ThreadLight Components] A Scripts folder exists at " +
                    "the old location, but it does not match the known legacy " +
                    "package. It was left unchanged for safety.");
            }

            if (hasLegacyGhost &&
                string.Equals(
                    AssetDatabase.AssetPathToGUID(LegacyGhostMaterialPath),
                    LegacyGhostMaterialGuid,
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(backupParent);
                File.Move(legacyGhostPath, backupGhostPath);
                ghostMoved = true;

                if (File.Exists(legacyGhostMetaPath))
                    File.Move(legacyGhostMetaPath, backupGhostMetaPath);
            }
            else if (hasLegacyGhost)
            {
                Debug.LogWarning(
                    "[ThreadLight Components] A Ghost Material exists at " +
                    "the old location, but it does not match the known legacy " +
                    "asset. It was left unchanged for safety.");
            }

            if (!scriptsMoved && !ghostMoved)
                return;

            if (scriptsMoved)
            {
                Debug.Log(
                    "[ThreadLight Components] Moved obsolete legacy " +
                    "scripts to:\n" + backupScriptsPath);
            }

            if (ghostMoved)
            {
                Debug.Log(
                    "[ThreadLight Components] Moved the obsolete legacy " +
                    "Ghost Material to:\n" + backupGhostPath);
            }

            // All filesystem work is complete before requesting a refresh. This
            // callback may reload the scripting domain, so nothing transactional
            // is allowed to follow it.
            ScheduleTemporaryCleanupIfNeeded(projectRoot);
            EditorApplication.delayCall += () =>
                AssetDatabase.Refresh(ImportAssetOptions.Default);
        }
        catch (Exception exception)
        {
            try
            {
                RestoreLegacyAssets(
                    scriptsMoved,
                    legacyScriptsPath,
                    legacyScriptsMetaPath,
                    backupScriptsPath,
                    backupScriptsMetaPath,
                    ghostMoved,
                    legacyGhostPath,
                    legacyGhostMetaPath,
                    backupGhostPath,
                    backupGhostMetaPath);
            }
            catch (Exception rollbackException)
            {
                exception = new AggregateException(
                    "Legacy migration failed and could not be fully rolled back.",
                    exception,
                    rollbackException);
            }

            Debug.LogError(
                "[ThreadLight Components] Could not migrate legacy assets. " +
                "Close tools using those files and restart Unity to retry.\n" +
                exception);
        }
    }

    private static bool IsRecognizedLegacyScripts(string legacyScriptsPath)
    {
        string actualGuid = AssetDatabase.AssetPathToGUID(LegacyScriptsPath);
        if (string.Equals(
                   actualGuid,
                   LegacyScriptsGuid,
                   StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        HashSet<string> expectedGuids = new HashSet<string>(
            new[]
            {
                LiveMirroringScriptGuid,
                GeneratedTargetMetadataGuid,
                AuthoringOnlyComponentGuid
            },
            StringComparer.OrdinalIgnoreCase
        );
        int matches = 0;

        foreach (string metaPath in Directory.GetFiles(
                     legacyScriptsPath,
                     "*.meta",
                     SearchOption.AllDirectories))
        {
            string guid = ReadMetaGuid(metaPath);
            if (!string.IsNullOrWhiteSpace(guid) &&
                expectedGuids.Remove(guid))
            {
                matches++;
            }
        }

        return matches >= 2;
    }

    private static void CreateCompilePlaceholders(
        string backupScriptsPath,
        string legacyScriptsPath)
    {
        string backupRoot = Path.GetFullPath(backupScriptsPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(legacyScriptsPath);

        foreach (string sourcePath in Directory.GetFiles(
                     backupScriptsPath,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalizedSourcePath = Path.GetFullPath(sourcePath);
            if (!normalizedSourcePath.StartsWith(
                    backupRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "A legacy script resolved outside the migration backup.");
            }

            string relativePath =
                normalizedSourcePath.Substring(backupRoot.Length);
            string placeholderPath = Path.Combine(
                legacyScriptsPath,
                relativePath);
            string placeholderParent = Path.GetDirectoryName(placeholderPath);
            if (!string.IsNullOrWhiteSpace(placeholderParent))
                Directory.CreateDirectory(placeholderParent);

            File.WriteAllText(
                placeholderPath,
                PlaceholderScriptContents + Environment.NewLine);
        }

        File.WriteAllText(
            Path.Combine(legacyScriptsPath, PlaceholderMarkerName),
            PlaceholderMarkerContents + Environment.NewLine);
    }

    private static void RestoreLegacyAssets(
        bool scriptsMoved,
        string legacyScriptsPath,
        string legacyScriptsMetaPath,
        string backupScriptsPath,
        string backupScriptsMetaPath,
        bool ghostMoved,
        string legacyGhostPath,
        string legacyGhostMetaPath,
        string backupGhostPath,
        string backupGhostMetaPath)
    {
        if (scriptsMoved)
        {
            if (Directory.Exists(legacyScriptsPath))
                Directory.Delete(legacyScriptsPath, true);
            if (File.Exists(legacyScriptsMetaPath))
                File.Delete(legacyScriptsMetaPath);

            if (Directory.Exists(backupScriptsPath))
                Directory.Move(backupScriptsPath, legacyScriptsPath);
            if (File.Exists(backupScriptsMetaPath))
            {
                File.Move(
                    backupScriptsMetaPath,
                    legacyScriptsMetaPath);
            }
        }

        if (ghostMoved)
        {
            if (File.Exists(legacyGhostPath))
                File.Delete(legacyGhostPath);
            if (File.Exists(legacyGhostMetaPath))
                File.Delete(legacyGhostMetaPath);

            if (File.Exists(backupGhostPath))
                File.Move(backupGhostPath, legacyGhostPath);
            if (File.Exists(backupGhostMetaPath))
                File.Move(backupGhostMetaPath, legacyGhostMetaPath);
        }
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
                "[ThreadLight Components] Could not remove the owned " +
                "temporary fallback payload after " + MaximumCleanupAttempts +
                " attempts. It will be retried after the project changes or " +
                "Unity reloads; no creator-owned asset was removed.");
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

    private static bool IsOwnedPlaceholder(string markerPath)
    {
        return File.Exists(markerPath) &&
               string.Equals(
                   File.ReadAllText(markerPath).Trim(),
                   PlaceholderMarkerContents,
                   StringComparison.Ordinal);
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

    private static string ReadMetaGuid(string metaPath)
    {
        if (!File.Exists(metaPath))
            return string.Empty;

        foreach (string line in File.ReadLines(metaPath))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(
                    "guid:",
                    StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring("guid:".Length).Trim();
            }
        }

        return string.Empty;
    }

    private static bool IsBuilderInstalled(string projectRoot)
    {
        return Directory.Exists(Path.GetFullPath(
                   Path.Combine(projectRoot, BuilderPackagePath))) ||
               Directory.Exists(Path.GetFullPath(
                   Path.Combine(
                       projectRoot,
                       ThreadlightMirroringPackagePath)));
    }
}
}
