using UnityEngine;

/// <summary>
/// Stable MonoScript identity for pre-ThreadLight prefabs. The implementation
/// lives in the namespaced base so importing an old script with this GUID cannot
/// overwrite the managed runtime contract.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class LiveMirroringSystem :
    Threadlight.Mirroring.LiveMirroringSystem
{
}
