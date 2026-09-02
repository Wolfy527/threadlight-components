namespace Threadlight.Mirroring
{
using System;
using Threadlight.Authoring;
using UnityEngine;

/// <summary>
/// Compatibility data retained for prefabs created by released Live Mirroring
/// versions. New creator projects use ThreadLight Authoring and stop emitting
/// this component at the customer export boundary.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(
    true, null, "Assembly-CSharp", "LiveMirroringSystem")]
public sealed class LiveMirroringSystem : AuthoringOnlyComponent
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
}
}
