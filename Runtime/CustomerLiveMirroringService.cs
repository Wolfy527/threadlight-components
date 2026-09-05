#if UNITY_EDITOR
namespace Threadlight.Components
{
using System.Collections.Generic;
using Threadlight.Mirroring;
using UnityEngine;

internal enum CustomerMirroringPairStatus
{
    Accepted, Disabled, MissingPair, MissingReference, SelfReference, DuplicateTarget, Cycle
}

internal readonly struct CustomerMirroringPairFact
{
    public readonly int Index;
    public readonly LiveMirroringSystem.MirrorPair Pair;
    public readonly CustomerMirroringPairStatus Status;
    public CustomerMirroringPairFact(int index, LiveMirroringSystem.MirrorPair pair, CustomerMirroringPairStatus status)
    {
        Index = index; Pair = pair; Status = status;
    }
}

/// <summary>Cached topology and derived facts consumed by mirroring, validation, preview, and builders.</summary>
internal sealed class CustomerMirroringEvaluationBuffers
{
    private static int hierarchyRevision;

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterHierarchyInvalidation()
    {
        UnityEditor.EditorApplication.hierarchyChanged -= InvalidateHierarchy;
        UnityEditor.EditorApplication.hierarchyChanged += InvalidateHierarchy;
    }

    private static void InvalidateHierarchy() => hierarchyRevision++;

    private readonly struct TransformSlot
    {
        private readonly Transform value;
        private readonly bool alive;
        public TransformSlot(Transform value) { this.value = value; alive = value != null; }
        public bool Matches(Transform candidate) => ReferenceEquals(value, candidate) && alive == (candidate != null);
    }

    private readonly struct PairSlot
    {
        private readonly LiveMirroringSystem.MirrorPair pair;
        private readonly bool enabled;
        private readonly TransformSlot source, target;
        public PairSlot(LiveMirroringSystem.MirrorPair pair)
        {
            this.pair = pair; enabled = pair?.mirrorEnabled ?? false;
            source = new TransformSlot(pair?.sourceTarget);
            target = new TransformSlot(pair?.mirroredTarget);
        }
        public bool Matches(LiveMirroringSystem.MirrorPair candidate) => ReferenceEquals(pair, candidate) &&
            (candidate == null || enabled == candidate.mirrorEnabled &&
             source.Matches(candidate.sourceTarget) &&
             target.Matches(candidate.mirroredTarget));
    }

    public readonly List<Transform> Targets = new List<Transform>();
    public readonly HashSet<Transform> TargetSet = new HashSet<Transform>();
    public readonly List<CustomerMirroringPairFact> PairFacts = new List<CustomerMirroringPairFact>();
    public readonly List<LiveMirroringSystem.MirrorPair> EnabledPairs = new List<LiveMirroringSystem.MirrorPair>();
    internal readonly HashSet<Transform> ActiveTargets = new HashSet<Transform>();
    private readonly List<PairSlot> pairSlots = new List<PairSlot>();
    private readonly List<TransformSlot> handleSlots = new List<TransformSlot>();
    private readonly HashSet<Transform> controlled = new HashSet<Transform>();
    private readonly Dictionary<Transform, List<Transform>> edges = new Dictionary<Transform, List<Transform>>();
    private readonly Stack<List<Transform>> edgeListPool = new Stack<List<Transform>>();
    private readonly HashSet<Transform> visited = new HashSet<Transform>();
    private readonly Stack<Transform> pending = new Stack<Transform>();
    private TransformSlot scaleReferenceSlot, rootSlot;
    private bool initialized, pairsNull, handlesNull;
    private int capturedHierarchyRevision;
    internal bool ScaleTopologyValid { get; private set; }
    internal int ScaleHandleSignature { get; private set; }

    public void Analyze(LiveMirroringSystem system)
    {
        LiveMirroringSystem.MirrorPair[] pairs = system?.pairs;
        Transform[] handles = system?.scaleHandles;
        if (Matches(system, pairs, handles)) return;
        Capture(system, pairs, handles);
        Targets.Clear(); TargetSet.Clear(); PairFacts.Clear(); EnabledPairs.Clear(); ActiveTargets.Clear();
        controlled.Clear(); RecycleEdges();
        if (handles != null) for (int i = 0; i < handles.Length; i++) AddTarget(handles[i]);
        if (pairs != null) for (int i = 0; i < pairs.Length; i++)
        {
            LiveMirroringSystem.MirrorPair pair = pairs[i];
            AddTarget(pair?.sourceTarget);
            if (system.ShouldCreateOppositeTarget(pair))
                AddTarget(pair?.mirroredTarget);
            if (pair?.sourceTarget != null) ActiveTargets.Add(pair.sourceTarget);
            if (system.ShouldCreateOppositeTarget(pair) &&
                pair.mirroredTarget != null) ActiveTargets.Add(pair.mirroredTarget);
            CustomerMirroringPairStatus status = Classify(system, pair);
            PairFacts.Add(new CustomerMirroringPairFact(i, pair, status));
            if (status == CustomerMirroringPairStatus.Accepted) EnabledPairs.Add(pair);
        }
        CaptureScaleTopology(system);
    }

    private bool Matches(LiveMirroringSystem system,
        LiveMirroringSystem.MirrorPair[] pairs, Transform[] handles)
    {
        if (!initialized || Targets.Count != TargetSet.Count ||
            capturedHierarchyRevision != hierarchyRevision ||
            !scaleReferenceSlot.Matches(system?.scaleReference) ||
            !rootSlot.Matches(CustomerLiveMirroringService.ResolveRoot(system)) ||
            pairsNull != (pairs == null) || handlesNull != (handles == null) ||
            pairSlots.Count != (pairs?.Length ?? 0) || handleSlots.Count != (handles?.Length ?? 0)) return false;
        for (int i = 0; i < pairSlots.Count; i++) if (!pairSlots[i].Matches(pairs[i])) return false;
        for (int i = 0; i < handleSlots.Count; i++) if (!handleSlots[i].Matches(handles[i])) return false;
        return true;
    }

    private void Capture(LiveMirroringSystem system,
        LiveMirroringSystem.MirrorPair[] pairs, Transform[] handles)
    {
        initialized = true; pairsNull = pairs == null; handlesNull = handles == null;
        capturedHierarchyRevision = hierarchyRevision;
        scaleReferenceSlot = new TransformSlot(system?.scaleReference);
        rootSlot = new TransformSlot(CustomerLiveMirroringService.ResolveRoot(system));
        pairSlots.Clear(); handleSlots.Clear();
        if (pairs != null) for (int i = 0; i < pairs.Length; i++) pairSlots.Add(new PairSlot(pairs[i]));
        if (handles != null) for (int i = 0; i < handles.Length; i++) handleSlots.Add(new TransformSlot(handles[i]));
    }

    private void AddTarget(Transform target)
    {
        if (target != null && TargetSet.Add(target)) Targets.Add(target);
    }

    private void CaptureScaleTopology(LiveMirroringSystem system)
    {
        ScaleTopologyValid = CustomerLiveMirroringService.TryCaptureScaleTopology(
            system, Targets, out int signature);
        ScaleHandleSignature = signature;
    }

    private CustomerMirroringPairStatus Classify(
        LiveMirroringSystem system,
        LiveMirroringSystem.MirrorPair pair)
    {
        if (pair == null) return CustomerMirroringPairStatus.MissingPair;
        if (!system.ShouldMirrorOppositeTarget(pair))
            return CustomerMirroringPairStatus.Disabled;
        if (pair.sourceTarget == null || pair.mirroredTarget == null) return CustomerMirroringPairStatus.MissingReference;
        if (pair.sourceTarget == pair.mirroredTarget ||
            pair.sourceTarget.IsChildOf(pair.mirroredTarget) ||
            pair.mirroredTarget.IsChildOf(pair.sourceTarget) ||
            UnityEditor.EditorUtility.IsPersistent(pair.sourceTarget) ||
            UnityEditor.EditorUtility.IsPersistent(pair.mirroredTarget) ||
            pair.sourceTarget.gameObject.scene != system.gameObject.scene ||
            pair.mirroredTarget.gameObject.scene != system.gameObject.scene)
            return CustomerMirroringPairStatus.SelfReference;
        if (!controlled.Add(pair.mirroredTarget)) return CustomerMirroringPairStatus.DuplicateTarget;
        if (CanReach(pair.mirroredTarget, pair.sourceTarget))
        {
            controlled.Remove(pair.mirroredTarget);
            return CustomerMirroringPairStatus.Cycle;
        }
        if (!edges.TryGetValue(pair.sourceTarget, out List<Transform> next))
        {
            next = edgeListPool.Count > 0
                ? edgeListPool.Pop()
                : new List<Transform>();
            edges.Add(pair.sourceTarget, next);
        }
        next.Add(pair.mirroredTarget);
        return CustomerMirroringPairStatus.Accepted;
    }

    private void RecycleEdges()
    {
        foreach (List<Transform> next in edges.Values)
        {
            next.Clear();
            edgeListPool.Push(next);
        }
        edges.Clear();
    }

    private bool CanReach(Transform start, Transform target)
    {
        visited.Clear(); pending.Clear(); pending.Push(start);
        while (pending.Count > 0)
        {
            Transform current = pending.Pop();
            if (current == null || !visited.Add(current)) continue;
            if (current == target) return true;
            if (!edges.TryGetValue(current, out List<Transform> next)) continue;
            for (int i = 0; i < next.Count; i++) pending.Push(next[i]);
        }
        return false;
    }
}

/// <summary>
/// Customer-only evaluation of stored target references. The calculation and
/// shared-scale rules parallel Authoring's service; regression tests protect
/// their common behavior without coupling either shipping package to the other.
/// This service never generates targets or changes the serialized schema.
/// </summary>
public static class CustomerLiveMirroringService
{
    private readonly struct ScaleObservation
    {
        internal readonly Vector3 DesiredScale;
        internal readonly bool WritesNeeded;
        internal ScaleObservation(Vector3 desiredScale, bool writesNeeded)
        {
            DesiredScale = desiredScale;
            WritesNeeded = writesNeeded;
        }
    }

    private static bool CanEvaluate(LiveMirroringSystem system) =>
        system != null && system.HasSupportedDataVersion && !Application.isPlaying &&
        !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode &&
        !UnityEditor.EditorUtility.IsPersistent(system);

    public static void UpdateMirroring(LiveMirroringSystem system)
    {
        if (!CanEvaluate(system)) return;
        CustomerMirroringEvaluationBuffers evaluation = Evaluate(system);
        ApplyScaleReference(system, evaluation);
        Mirror(system, evaluation.EnabledPairs, ResolveRoot(system));

    }

    public static void ApplyScaleReference(LiveMirroringSystem system)
    {
        if (CanEvaluate(system)) ApplyScaleReference(system, Evaluate(system));
    }

    public static void ApplyScaleReference(LiveMirroringSystem system, IReadOnlyList<Transform> targets)
    {
        if (!CanEvaluate(system) || !system.applyScaleReference || system.scaleReference == null || targets == null ||
            !TryCaptureScaleTopology(system, targets, out int signature)) return;
        ApplyScaleReference(system, targets, signature);
    }

    internal static void ApplyScaleReference(
        LiveMirroringSystem system, CustomerMirroringEvaluationBuffers evaluation)
    {
        if (system == null || !system.applyScaleReference ||
            system.scaleReference == null || !evaluation.ScaleTopologyValid) return;
        ApplyScaleReference(system, evaluation.Targets,
            evaluation.ScaleHandleSignature);
    }

    private static void ApplyScaleReference(LiveMirroringSystem system,
        IReadOnlyList<Transform> targets, int signature)
    {
        Vector3 referenceScale = system.scaleReference.lossyScale;
        bool topologyChanged = !system.scaleSynchronizationInitialized ||
            system.synchronizedScaleReference != system.scaleReference || system.synchronizedHandleSignature != signature;
        ScaleObservation observation = ObserveScale(targets, referenceScale,
            system.synchronizedWorldScale, topologyChanged,
            system.preferScaleReferenceAfterUndo);
        system.preferScaleReferenceAfterUndo = false;
        int undo = !topologyChanged && observation.WritesNeeded
            ? RecordScaleUndo(system, targets) : -1;
        if (observation.WritesNeeded)
        {
            ApplyWorldScale(system, system.scaleReference, observation.DesiredScale);
            for (int i = 0; i < targets.Count; i++)
                ApplyWorldScale(system, targets[i], observation.DesiredScale);
        }
        if (undo >= 0) UnityEditor.Undo.CollapseUndoOperations(undo);
        system.scaleSynchronizationInitialized = true;
        system.synchronizedWorldScale = observation.DesiredScale;
        system.synchronizedScaleReference = system.scaleReference;
        system.synchronizedHandleSignature = signature;
    }

    public static void MirrorEnabledPairs(LiveMirroringSystem system)
    {
        if (!CanEvaluate(system)) return;
        CustomerMirroringEvaluationBuffers evaluation = Evaluate(system);
        Mirror(system, evaluation.EnabledPairs, ResolveRoot(system));
    }

    public static List<Transform> CollectAllTargets(LiveMirroringSystem system) =>
        new List<Transform>(system == null ? System.Array.Empty<Transform>() : Evaluate(system).Targets);

    public static void CollectAllTargets(LiveMirroringSystem system, List<Transform> output, HashSet<Transform> seen)
    {
        if (output == null || seen == null) return;
        output.Clear(); seen.Clear();
        if (system == null) return;
        IReadOnlyList<Transform> targets = Evaluate(system).Targets;
        for (int i = 0; i < targets.Count; i++) if (targets[i] != null && seen.Add(targets[i])) output.Add(targets[i]);
    }

    public static Transform ResolveRoot(LiveMirroringSystem system) => system == null ? null :
        system.mirrorCenter != null ? system.mirrorCenter :
        system.transform.parent != null ? system.transform.parent : system.transform.root;

    internal static CustomerMirroringEvaluationBuffers AnalyzePairs(LiveMirroringSystem system) => Evaluate(system);
    internal static CustomerMirroringEvaluationBuffers Evaluate(LiveMirroringSystem system)
    {
        CustomerMirroringEvaluationBuffers evaluation = system.evaluationBuffers ??= new CustomerMirroringEvaluationBuffers();
        evaluation.Analyze(system);
        return evaluation;
    }

    private static void Mirror(LiveMirroringSystem system, IReadOnlyList<LiveMirroringSystem.MirrorPair> pairs,
        Transform center)
    {
        if (center == null) return;
        system.mirrorOptions ??= new LiveMirroringSystem.MirrorOptions();
        for (int i = 0; i < pairs.Count; i++) MirrorPair(system, pairs[i], center);
    }

    private static void MirrorPair(LiveMirroringSystem system, LiveMirroringSystem.MirrorPair pair, Transform center)
    {
        Transform source = pair.sourceTarget, target = pair.mirroredTarget;
        if (system.mirrorOptions.mirrorPosition)
        {
            Vector3 position = center.TransformPoint(MirrorVector(system, center.InverseTransformPoint(source.position)));
            if ((target.position - position).sqrMagnitude > .00000001f)
            {
                RecordTransformUndo(system, target);
                target.position = position;
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
        }
        if (system.mirrorOptions.mirrorRotation)
        {
            Vector3 forward = center.TransformDirection(MirrorVector(system, center.InverseTransformDirection(source.forward)));
            Vector3 up = center.TransformDirection(MirrorVector(system, center.InverseTransformDirection(source.up)));
            if (forward.sqrMagnitude >= .0001f && up.sqrMagnitude >= .0001f)
            {
                Quaternion rotation = Quaternion.LookRotation(forward, up) * Quaternion.Euler(pair.mirroredRotationOffset);
                if (Quaternion.Angle(target.rotation, rotation) > .001f)
                {
                    RecordTransformUndo(system, target);
                    target.rotation = rotation;
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                }
            }
        }
        if (system.mirrorOptions.mirrorScale) ApplyWorldScale(system, target, source.lossyScale);
    }

    private static Vector3 MirrorVector(LiveMirroringSystem system, Vector3 value)
    {
        if (system.mirrorOptions.mirrorAxis == LiveMirroringSystem.Axis.X) value.x *= -1;
        else if (system.mirrorOptions.mirrorAxis == LiveMirroringSystem.Axis.Y) value.y *= -1;
        else value.z *= -1;
        return value;
    }

    private static void ApplyWorldScale(LiveMirroringSystem system, Transform target, Vector3 worldScale)
    {
        if (target == null) return;
        Vector3 scale = worldScale;
        if (target.parent != null)
        {
            Vector3 parent = target.parent.lossyScale;
            scale = new Vector3(Divide(worldScale.x, parent.x), Divide(worldScale.y, parent.y), Divide(worldScale.z, parent.z));
        }
        if ((target.localScale - scale).sqrMagnitude > .00000001f)
        {
            RecordTransformUndo(system, target);
            target.localScale = scale;
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }

    private static void RecordTransformUndo(LiveMirroringSystem system, Transform target)
    {
        if (!system.undoRefreshQueued)
            UnityEditor.Undo.RecordObject(target, "Update Live Mirroring Targets");
    }

    private static ScaleObservation ObserveScale(
        IReadOnlyList<Transform> targets,
        Vector3 referenceScale,
        Vector3 previousScale,
        bool topologyChanged,
        bool preferReference)
    {
        bool referenceChanged = !Approximately(referenceScale, previousScale);
        bool allMatchReference = true;
        bool allMatchEdited = true;
        bool sawUneditedTarget = false;
        bool foundEditedTarget = false;
        Vector3 editedScale = previousScale;
        for (int i = 0; i < targets.Count; i++)
        {
            Transform target = targets[i];
            if (target == null) continue;
            Vector3 scale = target.lossyScale;
            if (!Approximately(scale, referenceScale))
                allMatchReference = false;
            if (!foundEditedTarget)
            {
                if (Approximately(scale, previousScale))
                    sawUneditedTarget = true;
                else
                {
                    foundEditedTarget = true;
                    editedScale = scale;
                    allMatchEdited = !sawUneditedTarget;
                }
            }
            else if (!Approximately(scale, editedScale))
                allMatchEdited = false;
        }
        Vector3 desired = referenceScale;
        if (!topologyChanged && !preferReference)
        {
            if (foundEditedTarget)
                desired = referenceChanged && allMatchReference
                    ? referenceScale : editedScale;
            else if (!referenceChanged)
                desired = previousScale;
        }
        bool writesNeeded = Approximately(desired, referenceScale)
            ? !allMatchReference
            : !allMatchEdited || !Approximately(referenceScale, desired);
        return new ScaleObservation(desired, writesNeeded);
    }

    private static int RecordScaleUndo(
        LiveMirroringSystem system, IReadOnlyList<Transform> targets)
    {
        if (system.undoRefreshQueued || UnityEditor.EditorUtility.IsPersistent(system)) return -1;
        List<Object> objects = new List<Object> { system.scaleReference };
        for (int i = 0; i < targets.Count; i++)
            if (targets[i] != null && targets[i] != system.scaleReference) objects.Add(targets[i]);
        int group = UnityEditor.Undo.GetCurrentGroup();
        UnityEditor.Undo.SetCurrentGroupName("Scale Live Mirroring Setup");
        UnityEditor.Undo.RecordObjects(objects.ToArray(), "Scale Live Mirroring Setup");
        return group;
    }

    public static bool HasValidScaleReferenceTopology(LiveMirroringSystem system, IReadOnlyList<Transform> targets)
        => TryCaptureScaleTopology(system, targets, out _);

    internal static bool TryCaptureScaleTopology(LiveMirroringSystem system,
        IReadOnlyList<Transform> targets, out int signature)
    {
        signature = 17;
        if (system == null || system.scaleReference == null || targets == null)
            return false;
        Transform reference = system.scaleReference;
        bool valid = reference != system.transform && reference != ResolveRoot(system) &&
            !system.transform.IsChildOf(reference) &&
            !UnityEditor.EditorUtility.IsPersistent(reference) &&
            reference.gameObject.scene == system.gameObject.scene;
        unchecked
        {
            for (int i = 0; i < targets.Count; i++)
            {
                Transform target = targets[i];
                signature = signature * 31 +
                    (target != null ? target.GetInstanceID() : 0);
                if (target == reference || target != null &&
                    (UnityEditor.EditorUtility.IsPersistent(target) ||
                     target.gameObject.scene != system.gameObject.scene ||
                     target.IsChildOf(reference) || reference.IsChildOf(target)))
                    valid = false;
            }
        }
        return valid;
    }

    internal static bool HasValidScaleReferenceTopology(LiveMirroringSystem system) =>
        system != null && Evaluate(system).ScaleTopologyValid;

    private static float Divide(float value, float divisor) => Mathf.Abs(divisor) < .00001f ? value : value / divisor;
    private static bool Approximately(Vector3 left, Vector3 right) => (left - right).sqrMagnitude <= .00000001f;
}
}
#endif
