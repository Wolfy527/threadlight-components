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
    private const string BuilderPackagePath =
        "Packages/com.wolfyvr.threadlight.builder";
    private const string ThreadlightMirroringPackagePath =
        "Packages/com.wolfyvr.threadlight.mirroring";
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

    private static void CaptureInstalledPackage()
    {
        TryRestoreInstalledPackage();
        UnityEditor.PackageManager.PackageInfo package =
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(LegacyScriptsFolderMigration).Assembly);
        if (package == null ||
            string.IsNullOrWhiteSpace(package.resolvedPath) ||
            !Directory.Exists(package.resolvedPath) ||
            ContainsReparsePoint(package.resolvedPath))
        {
            return;
        }

        string root = Path.GetFullPath(package.resolvedPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string snapshotRoot = GetPackageSnapshotRoot();
        if (string.IsNullOrWhiteSpace(snapshotRoot))
            return;
        Directory.CreateDirectory(snapshotRoot);

        foreach (string filePath in Directory.GetFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                HasReparsePoint(fullPath))
            {
                throw new InvalidDataException(
                    "A Components package file resolved outside its root.");
            }

            string relativePath = fullPath.Substring(root.Length);
            if (!IsProtectedPackagePath(relativePath))
                continue;

            string snapshotPath = Path.GetFullPath(Path.Combine(
                snapshotRoot,
                relativePath));
            if (!snapshotPath.StartsWith(
                    snapshotRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "A package snapshot path resolved outside its root.");
            }

            string parent = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            File.Copy(fullPath, snapshotPath, true);
        }
    }

    private static void TryRestoreInstalledPackage()
    {
        try
        {
            RestoreInstalledPackage();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[ThreadLight Components] Could not restore package-owned " +
                "files after a legacy import. The recoverable snapshot was " +
                "retained under Library and will be retried on reload.\n" +
                exception);
        }
    }

    private static void RestoreInstalledPackage()
    {
        string snapshotRoot = GetPackageSnapshotRoot();
        if (string.IsNullOrWhiteSpace(snapshotRoot) ||
            !Directory.Exists(snapshotRoot))
        {
            return;
        }

        UnityEditor.PackageManager.PackageInfo package =
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(LegacyScriptsFolderMigration).Assembly);
        if (package == null ||
            string.IsNullOrWhiteSpace(package.resolvedPath) ||
            !Directory.Exists(package.resolvedPath))
        {
            return;
        }

        string root = Path.GetFullPath(package.resolvedPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string assetRoot = string.IsNullOrWhiteSpace(package.assetPath)
            ? "Packages/" + package.name
            : package.assetPath.Replace('\\', '/').TrimEnd('/');
        List<string> changedAssetPaths = new List<string>();

        foreach (string snapshotPath in Directory.GetFiles(
                     snapshotRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            string fullSnapshotPath = Path.GetFullPath(snapshotPath);
            if (!File.Exists(fullSnapshotPath))
                continue;
            if (!fullSnapshotPath.StartsWith(
                    snapshotRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "A package snapshot file resolved outside its root.");
            }

            string relativePath =
                fullSnapshotPath.Substring(snapshotRoot.Length);
            string destination = Path.GetFullPath(Path.Combine(
                root,
                relativePath));
            if (!destination.StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "A package snapshot path resolved outside Components.");
            }

            string parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            byte[] current = File.Exists(destination)
                ? File.ReadAllBytes(destination)
                : null;
            byte[] expected = File.ReadAllBytes(fullSnapshotPath);
            if (ByteArraysEqual(current, expected))
                continue;

            File.WriteAllBytes(destination, expected);
            if (!relativePath.EndsWith(
                    ".meta",
                    StringComparison.OrdinalIgnoreCase))
            {
                changedAssetPaths.Add(
                    assetRoot + "/" + relativePath.Replace('\\', '/'));
            }
        }

        Directory.Delete(snapshotRoot, true);
        foreach (string assetPath in changedAssetPaths)
        {
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
        }
    }

    private static string GetPackageSnapshotRoot()
    {
        if (string.IsNullOrWhiteSpace(Application.dataPath))
            return null;

        string projectRoot =
            Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrWhiteSpace(projectRoot))
            return null;

        string libraryRoot = Path.GetFullPath(Path.Combine(
            projectRoot,
            "Library"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string snapshotRoot = Path.GetFullPath(Path.Combine(
            libraryRoot,
            "Threadlight",
            "Components",
            "Package Import Snapshot"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return snapshotRoot.StartsWith(
            libraryRoot,
            StringComparison.OrdinalIgnoreCase)
            ? snapshotRoot
            : null;
    }

    private static bool IsProtectedPackagePath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith(
                   "Runtime/",
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(
                   "Editor/",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized,
                   "Runtime.meta",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized,
                   "Editor.meta",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized,
                   "package.json",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized,
                   "package.json.meta",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized,
                   "Ghost Material.mat",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized,
                   "Ghost Material.mat.meta",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool ByteArraysEqual(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Length != right.Length)
            return false;

        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
                return false;
        }

        return true;
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

    private static void TryMigrateCore()
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
                    "the old location, but it does not match the known legacy " +
                    "package. It was left unchanged for safety.");
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
                    "the old location, but its manifest and owned asset GUIDs " +
                    "do not match the supported legacy package. It was left " +
                    "unchanged for safety.");
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
                    "the old location, but it does not match the known legacy " +
                    "asset. It was left unchanged for safety.");
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
                    "[ThreadLight Components] Moved the obsolete embedded " +
                    "fallback, including any creator-added files, to:\n" +
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

    private static bool IsLegacyImportPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        string normalized = assetPath.Replace('\\', '/');
        return IsAtOrBelow(normalized, LegacyScriptsPath) ||
               IsAtOrBelow(normalized, LegacyFallbackPath) ||
               string.Equals(
                   normalized,
                   LegacyGhostMaterialPath,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAtOrBelow(string assetPath, string rootPath)
    {
        return string.Equals(
                   assetPath,
                   rootPath,
                   StringComparison.OrdinalIgnoreCase) ||
               assetPath.StartsWith(
                   rootPath + "/",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecognizedLegacyScripts(string legacyScriptsPath)
    {
        if (ContainsReparsePoint(legacyScriptsPath) ||
            HasReparsePoint(legacyScriptsPath + ".meta"))
        {
            return false;
        }

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

    private static bool IsRecognizedLegacyFallback(string fallbackPath)
    {
        if (ContainsReparsePoint(fallbackPath))
            return false;

        string manifestPath = Path.Combine(fallbackPath, "package.json");
        if (!File.Exists(manifestPath))
            return false;

        if (!TryReadLegacyFallbackManifest(manifestPath, out _))
            return false;

        return HasExpectedMetaGuid(
                   fallbackPath,
                   "Live Mirroring/Runtime/LiveMirroringSystem.cs.meta",
                   LegacyFallbackLiveMirroringGuid) &&
               HasExpectedMetaGuid(
                   fallbackPath,
                   "Shared/Authoring/Runtime/GeneratedTargetMetadata.cs.meta",
                   LegacyFallbackTargetMetadataGuid) &&
               HasExpectedMetaGuid(
                   fallbackPath,
                   "Shared/Authoring/Runtime/AuthoringOnlyComponent.cs.meta",
                   LegacyFallbackAuthoringOnlyGuid) &&
               HasExpectedMetaGuid(
                   fallbackPath,
                   "Shared/Authoring/Runtime/GeneratedEditorOnlyObject.cs.meta",
                   LegacyFallbackGeneratedObjectGuid) &&
               HasExpectedMetaGuid(
                   fallbackPath,
                   "Shared/Authoring/Runtime/GeneratedHierarchyMetadata.cs.meta",
                   LegacyFallbackHierarchyMetadataGuid) &&
               HasExpectedMetaGuid(
                   fallbackPath,
                   "Shared/Authoring/Runtime/PrefabId.cs.meta",
                   LegacyFallbackPrefabIdGuid) &&
               HasExpectedMetaGuid(
                   fallbackPath,
                   "Ghost Material.mat.meta",
                   LegacyFallbackGhostMaterialGuid);
    }

    private static bool HasLegacyFallbackManifest(string fallbackPath)
    {
        return TryReadLegacyFallbackManifest(
            Path.Combine(fallbackPath, "package.json"),
            out _);
    }

    private static bool TryReadLegacyFallbackManifest(
        string manifestPath,
        out LegacyFallbackManifest manifest)
    {
        manifest = null;
        if (!File.Exists(manifestPath) || HasReparsePoint(manifestPath))
            return false;

        try
        {
            manifest = JsonUtility.FromJson<LegacyFallbackManifest>(
                File.ReadAllText(manifestPath));
        }
        catch (Exception)
        {
            return false;
        }

        return manifest != null &&
               string.Equals(
                   manifest.name,
                   LegacyFallbackPackageName,
                   StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(manifest.version);
    }

    private static bool HasExpectedMetaGuid(
        string rootPath,
        string relativePath,
        string expectedGuid)
    {
        string metaPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        string normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return metaPath.StartsWith(
                   normalizedRoot,
                   StringComparison.OrdinalIgnoreCase) &&
               !HasReparsePoint(metaPath) &&
               string.Equals(
                   ReadMetaGuid(metaPath),
                   expectedGuid,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsReparsePoint(string rootPath)
    {
        Stack<DirectoryInfo> pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(rootPath));

        while (pending.Count > 0)
        {
            DirectoryInfo directory = pending.Pop();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return true;

            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
                if (entry is DirectoryInfo child)
                    pending.Push(child);
            }
        }

        return false;
    }

    private static bool HasReparsePoint(string path)
    {
        return (File.Exists(path) || Directory.Exists(path)) &&
               (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
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

    [Serializable]
    private sealed class LegacyFallbackManifest
    {
        public string name;
        public string version;
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
