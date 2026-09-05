namespace Threadlight.Mirroring
{
using System;
using Threadlight.Authoring;
using UnityEngine;

/// <summary>
/// Customer setup behavior and stable data for released Live Mirroring prefabs.
/// Only stored target references are evaluated; creator generation remains in
/// ThreadLight Authoring. All evaluation stops before play mode and upload.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("")]
public class LiveMirroringSystem : AuthoringOnlyComponent
{
    public const string DefaultConstraintTargetsObjectName =
        "SETUP - Constraint Locations";
    public override bool RemoveGameObjectWithComponent => true;
    public enum Axis { X, Y, Z }

    [Serializable]
    public sealed class MirrorOptions
    {
        public bool mirrorPosition = true;
        public bool mirrorRotation = true;
        public bool mirrorScale = true;
        public Axis mirrorAxis = Axis.X;
    }

    [Serializable]
    public sealed class MirrorPair
    {
        public bool mirrorEnabled = true;
        public string pairName = "Mirror Pair";
        public HumanBodyBones sourceBone = HumanBodyBones.RightHand;
        public HumanBodyBones mirroredBone = HumanBodyBones.LeftHand;
        public Transform sourceTarget;
        public Transform mirroredTarget;
        public Vector3 mirroredRotationOffset;
    }

    [SerializeField] private int dataVersion;
    public int DataVersion => dataVersion;
    public bool liveMirror = true;
    public Transform mirrorCenter;
    [HideInInspector] public string constraintTargetsObjectName =
        DefaultConstraintTargetsObjectName;
    [HideInInspector] public bool addVrcfuryArmatureLinks = true;
    public bool applyScaleReference = true;
    public Transform scaleReference;
    [HideInInspector] public Transform[] scaleHandles;
    public MirrorPair[] pairs;
    public bool showScenePreview = true;
    public GameObject previewSource;
    public Material previewMaterial;
    [HideInInspector] public bool generateThreadlightComponentsBootstrapper = true;
    [HideInInspector] public string threadlightComponentsBootstrapperFolderPath =
        "Assets/Threadlight/Components/Temp";
    public MirrorOptions mirrorOptions = new MirrorOptions();

    public void SetDataVersion(int version) => dataVersion = version;

    public bool HasSupportedDataVersion => dataVersion >= 0 && dataVersion <= 3;
    public bool ShouldCreateOppositeTarget(MirrorPair pair) => pair != null && pair.mirrorEnabled;
    public bool ShouldMirrorOppositeTarget(MirrorPair pair) => ShouldCreateOppositeTarget(pair);

#if UNITY_EDITOR
    [NonSerialized] internal bool scaleSynchronizationInitialized;
    [NonSerialized] internal Vector3 synchronizedWorldScale = Vector3.one;
    [NonSerialized] internal Transform synchronizedScaleReference;
    [NonSerialized] internal int synchronizedHandleSignature;
    [NonSerialized] internal bool preferScaleReferenceAfterUndo;
    [NonSerialized] internal bool undoRefreshQueued;
    [NonSerialized] internal Threadlight.Components.CustomerMirroringEvaluationBuffers evaluationBuffers;
#endif

    protected override void OnEnable()
    {
        base.OnEnable();
#if UNITY_EDITOR
        scaleSynchronizationInitialized = false;
        UnityEditor.Undo.undoRedoPerformed -= QueueUndoRefresh;
        UnityEditor.Undo.undoRedoPerformed += QueueUndoRefresh;
#endif
    }

    protected virtual void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.undoRedoPerformed -= QueueUndoRefresh;
        UnityEditor.EditorApplication.delayCall -= ApplyUndoRefresh;
        undoRefreshQueued = false;
#endif
    }

    protected virtual void Update()
    {
        if (!Application.isPlaying && liveMirror) MirrorAll();
    }

    public void MirrorAll()
    {
#if UNITY_EDITOR
        if (HasSupportedDataVersion && !Application.isPlaying &&
            !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode &&
            !UnityEditor.EditorUtility.IsPersistent(this))
            Threadlight.Components.CustomerLiveMirroringService.UpdateMirroring(this);
#endif
    }

#if UNITY_EDITOR
    private void QueueUndoRefresh()
    {
        if (undoRefreshQueued || Application.isPlaying) return;
        preferScaleReferenceAfterUndo = scaleSynchronizationInitialized && scaleReference != null &&
            (scaleReference.lossyScale - synchronizedWorldScale).sqrMagnitude > .00000001f;
        undoRefreshQueued = true;
        UnityEditor.EditorApplication.delayCall += ApplyUndoRefresh;
    }

    private void ApplyUndoRefresh()
    {
        UnityEditor.EditorApplication.delayCall -= ApplyUndoRefresh;
        if (this != null && isActiveAndEnabled && liveMirror && !Application.isPlaying)
            MirrorAll();
        undoRefreshQueued = false;
        UnityEditor.SceneView.RepaintAll();
    }
#endif
}
}
