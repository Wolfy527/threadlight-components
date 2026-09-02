namespace Threadlight.Authoring
{
using System;

/// <summary>
/// Migrates the Prefab ID container format. Builder state and individual module
/// settings are migrated separately by ThreadLight Builder.
/// </summary>
public static class PrefabIdMigrationService
{
    public static PrefabIdMigrationResult TryMigrate(PrefabId prefabId)
    {
        if (prefabId == null)
            return PrefabIdMigrationResult.InvalidVersion;
        if (prefabId.PrefabSchema < 0)
            return PrefabIdMigrationResult.InvalidVersion;
        if (prefabId.PrefabSchema > PrefabId.CurrentSchemaVersion)
            return PrefabIdMigrationResult.NewerThanSupported;
        if (prefabId.PrefabSchema == PrefabId.CurrentSchemaVersion)
            return PrefabIdMigrationResult.UpToDate;

        if (prefabId.PrefabSchema == 0)
        {
            prefabId.NormalizeLegacySnapshot();
            prefabId.SetSchemaVersion(1);
        }
        if (prefabId.PrefabSchema == 1)
        {
            prefabId.NormalizeLegacySnapshot();
            prefabId.RefreshSnapshotFingerprint();
            prefabId.SetSchemaVersion(2);
        }
        return PrefabIdMigrationResult.Migrated;
    }

    public static bool HasValidFingerprint(PrefabId prefabId)
    {
        if (prefabId == null ||
            prefabId.PrefabSchema != PrefabId.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(prefabId.SnapshotFingerprint))
        {
            return false;
        }

        return string.Equals(
            prefabId.SnapshotFingerprint,
            PrefabIdSnapshotUtility.ComputeFingerprint(prefabId),
            StringComparison.Ordinal
        );
    }

}

public enum PrefabIdMigrationResult
{
    UpToDate,
    Migrated,
    NewerThanSupported,
    InvalidVersion
}
}
