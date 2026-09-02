namespace Threadlight.Authoring
{
using System;
using System.Collections.Generic;
using UnityEngine;

#if VRC_SDK_VRCSDK3
using VRC.SDKBase;
#endif

[Serializable]
/// <summary>
/// Stores one Unity object reference separately from the JSON Builder state.
/// </summary>
public sealed class PrefabIdObjectReference
{
    [SerializeField]
    private string propertyPath;

    [SerializeField]
    private UnityEngine.Object value;

    public string PropertyPath => propertyPath;
    public UnityEngine.Object Value => value;

    public PrefabIdObjectReference(
        string propertyPath,
        UnityEngine.Object value)
    {
        this.propertyPath = propertyPath ?? string.Empty;
        this.value = value;
    }
}

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Wolfy/Prefab ID")]
/// <summary>
/// Lightweight customer-side snapshot used to resume a prefab in Prefab
/// Builder without distributing the Builder package itself.
/// </summary>
public sealed class PrefabId : MonoBehaviour
#if VRC_SDK_VRCSDK3
    , IEditorOnly, IPreprocessCallbackBehaviour
#endif
{
    public const int CurrentSchemaVersion = 2;

    // Field names are part of the serialized customer prefab format.
    [SerializeField, HideInInspector]
    private string prefabId;

    [SerializeField, HideInInspector]
    private int prefabSchema = CurrentSchemaVersion;

    [SerializeField, HideInInspector]
    private int builderDataVersion;

    [SerializeField, HideInInspector]
    private string builderPackageVersion;

    [SerializeField, HideInInspector, TextArea]
    private string builderState;

    [SerializeField, HideInInspector]
    private List<PrefabIdObjectReference> objectReferences =
        new List<PrefabIdObjectReference>();

    [SerializeField, HideInInspector]
    private List<string> builderOwnedPaths = new List<string>();

    [SerializeField, HideInInspector]
    private string snapshotFingerprint;

    public string Id => prefabId;
    public int PrefabSchema => prefabSchema;
    public int BuilderDataVersion => builderDataVersion;
    public string BuilderPackageVersion => builderPackageVersion;
    public string BuilderState => builderState;
    public IReadOnlyList<PrefabIdObjectReference> ObjectReferences =>
        objectReferences;
    public IReadOnlyList<string> BuilderOwnedPaths => builderOwnedPaths;
    public string SnapshotFingerprint => snapshotFingerprint;
    public bool HasSnapshot =>
        !string.IsNullOrWhiteSpace(prefabId) &&
        !string.IsNullOrWhiteSpace(builderState);

    private bool removalQueued;

    protected void Awake() => QueueRemovalInPlayMode();
    protected void OnEnable() => QueueRemovalInPlayMode();

    private void QueueRemovalInPlayMode()
    {
        if (!Application.isPlaying || removalQueued)
            return;

        removalQueued = true;
        Destroy(this);
    }

#if VRC_SDK_VRCSDK3
    public int PreprocessOrder => -10000;

    public bool OnPreprocess()
    {
        if (this != null)
            DestroyImmediate(this, true);
        return true;
    }
#endif

    public void SetSnapshot(
        string id,
        int dataVersion,
        string packageVersion,
        string state,
        IEnumerable<PrefabIdObjectReference> references,
        IEnumerable<string> ownedPaths)
    {
        prefabId = string.IsNullOrWhiteSpace(id)
            ? Guid.NewGuid().ToString("N")
            : id.Trim();
        prefabSchema = CurrentSchemaVersion;
        builderDataVersion = Mathf.Max(0, dataVersion);
        builderPackageVersion = packageVersion?.Trim() ?? string.Empty;
        builderState = state ?? string.Empty;
        objectReferences = references != null
            ? new List<PrefabIdObjectReference>(references)
            : new List<PrefabIdObjectReference>();
        builderOwnedPaths = ownedPaths != null
            ? new List<string>(ownedPaths)
            : new List<string>();
        snapshotFingerprint = PrefabIdSnapshotUtility.ComputeFingerprint(this);
    }

    internal void NormalizeLegacySnapshot()
    {
        prefabId = prefabId?.Trim() ?? string.Empty;
        builderPackageVersion = builderPackageVersion?.Trim() ?? string.Empty;
        builderState = builderState ?? string.Empty;
        objectReferences = objectReferences ??
            new List<PrefabIdObjectReference>();
        builderOwnedPaths = builderOwnedPaths ?? new List<string>();
        snapshotFingerprint = snapshotFingerprint?.Trim() ?? string.Empty;
    }

    internal void SetSchemaVersion(int version)
    {
        prefabSchema = version;
    }

    internal void RefreshSnapshotFingerprint()
    {
        snapshotFingerprint = PrefabIdSnapshotUtility.ComputeFingerprint(this);
    }
}
}
