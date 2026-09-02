namespace Threadlight.Authoring
{
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class GeneratedHierarchyMetadata : AuthoringOnlyComponent
{
    [SerializeField, HideInInspector]
    private string ownerId;

    [SerializeField, HideInInspector]
    private string moduleId;

    [SerializeField, HideInInspector]
    private string stableId;

    [SerializeField, HideInInspector]
    private string role;

    [SerializeField, HideInInspector]
    private bool createdByBuilder;

    public string OwnerId => ownerId;
    public string ModuleId => moduleId;
    public string StableId => stableId;
    public string Role => role;
    public bool CreatedByBuilder => createdByBuilder;

    public bool IsOwnedBy(string expectedOwnerId, string expectedModuleId = null)
    {
        if (string.IsNullOrWhiteSpace(expectedOwnerId) ||
            !string.Equals(ownerId, expectedOwnerId))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(expectedModuleId) ||
               string.Equals(moduleId, expectedModuleId);
    }

    public bool Matches(
        string expectedOwnerId,
        string expectedModuleId,
        string expectedStableId)
    {
        return IsOwnedBy(expectedOwnerId, expectedModuleId) &&
               !string.IsNullOrWhiteSpace(expectedStableId) &&
               string.Equals(stableId, expectedStableId);
    }

    public void Configure(
        string generatedOwnerId,
        string generatedModuleId,
        string generatedStableId,
        string generatedRole,
        bool builderCreated)
    {
        ownerId = generatedOwnerId?.Trim() ?? string.Empty;
        moduleId = generatedModuleId?.Trim() ?? string.Empty;
        stableId = generatedStableId?.Trim() ?? string.Empty;
        role = generatedRole?.Trim() ?? string.Empty;
        createdByBuilder = builderCreated;
        hideFlags |= HideFlags.HideInInspector;
    }

    private void Reset()
    {
        hideFlags |= HideFlags.HideInInspector;
    }

    private void OnValidate()
    {
        hideFlags |= HideFlags.HideInInspector;
    }
}
}
