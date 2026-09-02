namespace Threadlight.Authoring
{
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Assembly-CSharp", "GeneratedTargetMetadata")]
public class GeneratedTargetMetadata : AuthoringOnlyComponent
{
    public enum TargetRole
    {
        Source,
        Opposite
    }

    [HideInInspector] public string stableId;
    [HideInInspector] public TargetRole role;
    [HideInInspector] public string displayName;
    [HideInInspector] public string ownerId;
    [HideInInspector] public string moduleId;

    [SerializeField, HideInInspector]
    private bool removeGeneratedObject;

    [SerializeField, HideInInspector]
    private bool createdByBuilder = true;

    public override bool RemoveGameObjectWithComponent => removeGeneratedObject;
    public bool CreatedByBuilder => createdByBuilder;

    private void Reset()
    {
        HideInternalComponent();
    }

    private void OnValidate()
    {
        HideInternalComponent();
    }

    public bool IsOwnedBy(string expectedOwnerId)
    {
        return !string.IsNullOrWhiteSpace(expectedOwnerId) && ownerId == expectedOwnerId;
    }

    public bool IsUnclaimed => string.IsNullOrWhiteSpace(ownerId);

    public void ConfigureIdentity(
        string generatedOwnerId,
        string generatedModuleId,
        string generatedStableId,
        TargetRole generatedRole,
        string generatedDisplayName)
    {
        ownerId = generatedOwnerId;
        moduleId = generatedModuleId;
        stableId = generatedStableId;
        role = generatedRole;
        displayName = generatedDisplayName;
        HideInternalComponent();
    }

    public void ConfigureCleanup(bool removeOwnerGameObject)
    {
        removeGeneratedObject = removeOwnerGameObject;
        HideInternalComponent();
    }

    public void ConfigureOwnership(bool builderCreated)
    {
        createdByBuilder = builderCreated;
        HideInternalComponent();
    }

    private void HideInternalComponent()
    {
        hideFlags |= HideFlags.HideInInspector;
    }
}
}
