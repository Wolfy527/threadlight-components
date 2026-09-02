namespace Threadlight.Authoring
{
using UnityEngine;

#if VRC_SDK_VRCSDK3
using VRC.SDKBase;
#endif

[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Assembly-CSharp", "AuthoringOnlyComponent")]
/// <summary>
/// Base class for creator-only components. It provides both play-mode cleanup
/// and the VRChat SDK preprocessing hook; the global build cleaner remains a
/// defense-in-depth fallback for every authoring component.
/// </summary>
public abstract class AuthoringOnlyComponent : MonoBehaviour
#if VRC_SDK_VRCSDK3
    , IEditorOnly, IPreprocessCallbackBehaviour
#endif
{
    private bool playModeRemovalQueued;

    public virtual bool RemoveGameObjectWithComponent => false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StripLoadedAuthoringComponents()
    {
        AuthoringOnlyComponent[] components =
            Object.FindObjectsOfType<AuthoringOnlyComponent>(true);

        foreach (AuthoringOnlyComponent component in components)
        {
            if (component == null || !component.gameObject.scene.IsValid())
                continue;

            component.QueuePlayModeRemoval();
        }
    }

    protected virtual void Awake()
    {
        QueuePlayModeRemoval();
    }

    protected virtual void OnEnable()
    {
        QueuePlayModeRemoval();
    }

    private void QueuePlayModeRemoval()
    {
        if (!Application.isPlaying || playModeRemovalQueued)
            return;

        playModeRemovalQueued = true;

        AuthoringBuildCleaner.StripAuthoringComponent(this);
    }

#if VRC_SDK_VRCSDK3
    public virtual int PreprocessOrder => -10000;

    public virtual bool OnPreprocess()
    {
        AuthoringBuildCleaner.StripAuthoringComponent(this);
        return true;
    }
#endif
}
}
