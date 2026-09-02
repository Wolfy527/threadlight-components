// Unity can temporarily compile a legacy MonoScript inside this package while
// resolving an imported asset with the same historical GUID. These generic
// no-op services keep that one import cycle valid; the editor migration then
// restores the managed source and quarantines the legacy script tree.
internal static class LiveMirroringMigrationService
{
    public static void MigrateIfNeeded<T>(T system) { }
}

internal static class LiveMirroringScaleService
{
    public static void ApplyScaleReference<T>(T system) { }
}

internal static class LiveMirroringTransformService
{
    public static void MirrorEnabledPairs<T>(T system) { }
}
