namespace Threadlight.Components.Editor
{
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static partial class LegacyScriptsFolderMigration
{
    private static void TryMigrateCore()
    {
        // Discovery and path validation are completed before any file moves.
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            Debug.LogError(
                "[ThreadLight Components] Could not locate the Unity project " +
                "folder. Legacy assets were left unchanged.");
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
        string legacyFallbackPath = Path.GetFullPath(
            Path.Combine(projectRoot, LegacyFallbackPath));
        bool hasLegacyFallback = Directory.Exists(legacyFallbackPath);

        if (!hasLegacyScripts && !hasLegacyGhost && !hasLegacyFallback)
        {
            CancelMigrationRetry();
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
                StringComparison.OrdinalIgnoreCase) ||
            !legacyFallbackPath.StartsWith(
                assetsRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError(
                "[ThreadLight Components] Legacy assets were not migrated because " +
                "a path resolved outside this project's Assets folder. Existing " +
                "files were left unchanged.");
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
        string backupFallbackPath = Path.Combine(
            backupParent,
            "Prefab Components Fallback");
        string legacyFallbackMetaPath = legacyFallbackPath + ".meta";
        string backupFallbackMetaPath = backupFallbackPath + ".meta";
        bool scriptsMoved = false;
        bool ghostMoved = false;
        bool fallbackMoved = false;

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
                    "the previous location, but ThreadLight could not verify that " +
                    "it belongs to the legacy package. It was left unchanged.");
            }

            if (hasLegacyFallback &&
                !HasReparsePoint(legacyFallbackMetaPath) &&
                IsRecognizedLegacyFallback(legacyFallbackPath))
            {
                Directory.CreateDirectory(backupParent);
                Directory.Move(legacyFallbackPath, backupFallbackPath);
                fallbackMoved = true;

                if (File.Exists(legacyFallbackMetaPath))
                {
                    File.Move(
                        legacyFallbackMetaPath,
                        backupFallbackMetaPath);
                }
            }
            else if (hasLegacyFallback)
            {
                Debug.LogWarning(
                    "[ThreadLight Components] A fallback folder exists at " +
                    "the previous location, but ThreadLight could not verify that " +
                    "it belongs to the legacy package. It was left unchanged.");
                if (HasLegacyFallbackManifest(legacyFallbackPath))
                    ScheduleMigrationRetry();
            }

            if (hasLegacyGhost &&
                !HasReparsePoint(legacyGhostPath) &&
                !HasReparsePoint(legacyGhostMetaPath) &&
                string.Equals(
                    ReadMetaGuid(legacyGhostMetaPath),
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
                    "the previous location, but ThreadLight could not verify that " +
                    "it belongs to the legacy package. It was left unchanged.");
            }

            if (!scriptsMoved && !ghostMoved && !fallbackMoved)
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

            if (fallbackMoved)
            {
                Debug.Log(
                    "[ThreadLight Components] Moved the obsolete embedded fallback " +
                    "and all files inside it, including creator-added files, to:\n" +
                    backupFallbackPath);
            }

            // All filesystem work is complete before requesting a refresh. This
            // callback may reload the scripting domain, so nothing transactional
            // is allowed to follow it.
            ScheduleTemporaryCleanupIfNeeded(projectRoot);
            CancelMigrationRetry();
            ScheduleRefresh();
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
                    backupGhostMetaPath,
                    fallbackMoved,
                    legacyFallbackPath,
                    legacyFallbackMetaPath,
                    backupFallbackPath,
                    backupFallbackMetaPath);
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
            ScheduleMigrationRetry();
        }
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
        string backupGhostMetaPath,
        bool fallbackMoved,
        string legacyFallbackPath,
        string legacyFallbackMetaPath,
        string backupFallbackPath,
        string backupFallbackMetaPath)
    {
        List<Exception> rollbackErrors = new List<Exception>();

        TryRollback(
            ghostMoved,
            () =>
            {
                if (File.Exists(legacyGhostPath))
                    File.Delete(legacyGhostPath);
                if (File.Exists(legacyGhostMetaPath))
                    File.Delete(legacyGhostMetaPath);

                if (File.Exists(backupGhostPath))
                    File.Move(backupGhostPath, legacyGhostPath);
                if (File.Exists(backupGhostMetaPath))
                    File.Move(backupGhostMetaPath, legacyGhostMetaPath);
            },
            rollbackErrors);

        TryRollback(
            fallbackMoved,
            () =>
            {
                if (Directory.Exists(legacyFallbackPath))
                    Directory.Delete(legacyFallbackPath, true);
                if (File.Exists(legacyFallbackMetaPath))
                    File.Delete(legacyFallbackMetaPath);

                if (Directory.Exists(backupFallbackPath))
                    Directory.Move(backupFallbackPath, legacyFallbackPath);
                if (File.Exists(backupFallbackMetaPath))
                {
                    File.Move(
                        backupFallbackMetaPath,
                        legacyFallbackMetaPath);
                }
            },
            rollbackErrors);

        TryRollback(
            scriptsMoved,
            () =>
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
            },
            rollbackErrors);

        if (rollbackErrors.Count > 0)
        {
            throw new AggregateException(
                "One or more legacy assets could not be restored.",
                rollbackErrors);
        }
    }

    private static void TryRollback(
        bool shouldRun,
        Action rollback,
        ICollection<Exception> errors)
    {
        if (!shouldRun)
            return;

        try
        {
            rollback();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }

}
}
