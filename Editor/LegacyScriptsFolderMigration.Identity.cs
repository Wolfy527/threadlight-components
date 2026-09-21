namespace Threadlight.Components.Editor
{
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static partial class LegacyScriptsFolderMigration
{
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

    private static bool IsOwnedPlaceholder(string markerPath)
    {
        return File.Exists(markerPath) &&
               string.Equals(
                   File.ReadAllText(markerPath).Trim(),
                   PlaceholderMarkerContents,
                   StringComparison.Ordinal);
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

    [Serializable]
    private sealed class LegacyFallbackManifest
    {
        public string name;
        public string version;
    }
}
}
