namespace Threadlight.Authoring {
using System.Collections.Generic;
using UnityEngine;

public static class AuthoringBuildCleaner {
    public struct CleanupReport {
        public int ComponentsRemoved;
        public int GameObjectsRemoved;
        public bool HasChanges => ComponentsRemoved > 0 || GameObjectsRemoved > 0;
    }

    public static CleanupReport StripAuthoringComponentsFrom(GameObject root) {
        if (root == null) return default;
        if (!root.scene.IsValid()) {
            Debug.LogError(
                "ThreadLight Components refused to strip authoring data directly " +
                "from an asset. Run cleanup on a scene or upload copy instead.", root);
            return default;
        }
        StripComponents(root, out HashSet<GameObject> generatedObjects, out int componentCount);
        RemoveGeneratedObjects(generatedObjects);
        return new CleanupReport {
            ComponentsRemoved = componentCount,
            GameObjectsRemoved = generatedObjects.Count
        };
    }

    public static void StripAuthoringComponent(AuthoringOnlyComponent component) {
        if (component == null) return;
        if (component.RemoveGameObjectWithComponent)
            RemoveGeneratedObjectPreservingChildren(component.gameObject, null);
        else
            DestroyObject(component);
    }

    private static void StripComponents(GameObject root,
        out HashSet<GameObject> generatedObjects, out int componentCount) {
        generatedObjects = new HashSet<GameObject>();
        AuthoringOnlyComponent[] components =
            root.GetComponentsInChildren<AuthoringOnlyComponent>(true);
        componentCount = components.Length;
        foreach (AuthoringOnlyComponent component in components) {
            if (component == null) continue;
            if (component.RemoveGameObjectWithComponent) {
                generatedObjects.Add(component.gameObject);
                continue;
            }
            DestroyObject(component);
        }
    }

    private static void RemoveGeneratedObjects(HashSet<GameObject> generatedObjects) {
        List<GameObject> ordered = new List<GameObject>(generatedObjects);
        ordered.Sort((left, right) =>
            HierarchyDepth(right).CompareTo(HierarchyDepth(left)));
        foreach (GameObject generatedObject in ordered)
            if (generatedObject != null)
                RemoveGeneratedObjectPreservingChildren(generatedObject, generatedObjects);
    }

    private static int HierarchyDepth(GameObject item) {
        int depth = 0;
        for (Transform current = item != null ? item.transform.parent : null;
             current != null; current = current.parent)
            depth++;
        return depth;
    }

    // Lift creator content out before deleting editor-only holders. Processing
    // deepest-first also preserves creator grandchildren below nested helpers.
    private static void RemoveGeneratedObjectPreservingChildren(
        GameObject generatedObject, HashSet<GameObject> generatedObjects) {
        Transform generatedTransform = generatedObject.transform;
        Transform parent = generatedTransform.parent;
        int insertionIndex = generatedTransform.GetSiblingIndex();
        List<Transform> retainedChildren = new List<Transform>();
        for (int index = 0; index < generatedTransform.childCount; index++) {
            Transform child = generatedTransform.GetChild(index);
            if (generatedObjects == null || !generatedObjects.Contains(child.gameObject))
                retainedChildren.Add(child);
        }
        foreach (Transform child in retainedChildren) {
            child.SetParent(parent, true);
            child.SetSiblingIndex(insertionIndex++);
        }
        DestroyObject(generatedObject);
    }

    private static void DestroyObject(Object target) {
        if (target == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) {
            Object.DestroyImmediate(target, true);
            return;
        }
#endif
        Object.Destroy(target);
    }
}
}
