namespace Threadlight.Authoring
{
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Produces a deterministic integrity fingerprint for the serialized Prefab ID
/// payload. Object references contribute their property paths rather than
/// instance IDs so the result remains stable between Unity sessions.
/// </summary>
internal static class PrefabIdSnapshotUtility
{
    public static string ComputeFingerprint(PrefabId prefabId)
    {
        if (prefabId == null)
            return string.Empty;

        StringBuilder input = new StringBuilder();
        Append(input, prefabId.Id);
        Append(
            input,
            prefabId.BuilderDataVersion.ToString(CultureInfo.InvariantCulture)
        );
        Append(input, prefabId.BuilderPackageVersion);
        Append(input, prefabId.BuilderState);

        AppendPaths(input, prefabId.ObjectReferences);
        AppendPaths(input, prefabId.BuilderOwnedPaths);

        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(
                Encoding.UTF8.GetBytes(input.ToString())
            );
            StringBuilder hex = new StringBuilder(hash.Length * 2);

            foreach (byte value in hash)
                hex.Append(value.ToString("x2"));

            return hex.ToString();
        }
    }

    private static void AppendPaths(
        StringBuilder input,
        System.Collections.Generic.IReadOnlyList<PrefabIdObjectReference>
            references)
    {
        int count = references?.Count ?? 0;
        Append(input, count.ToString(CultureInfo.InvariantCulture));

        if (references == null)
            return;

        foreach (PrefabIdObjectReference reference in references)
            Append(input, reference?.PropertyPath);
    }

    private static void AppendPaths(
        StringBuilder input,
        System.Collections.Generic.IReadOnlyList<string> paths)
    {
        int count = paths?.Count ?? 0;
        Append(input, count.ToString(CultureInfo.InvariantCulture));

        if (paths == null)
            return;

        foreach (string path in paths)
            Append(input, path);
    }

    private static void Append(StringBuilder destination, string value)
    {
        string normalized = value ?? string.Empty;
        destination.Append(normalized.Length);
        destination.Append(':');
        destination.Append(normalized);
        destination.Append(';');
    }
}
}
