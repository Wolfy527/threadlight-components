namespace Threadlight.Components.Editor
{
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static partial class LegacyScriptsFolderMigration
{
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
                "[ThreadLight Components] Could not restore package files after " +
                "a legacy import. A recovery copy was kept, and restoration will " +
                "retry after Unity reloads.\n" +
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

}
}
