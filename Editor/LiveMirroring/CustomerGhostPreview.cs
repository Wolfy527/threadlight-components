using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CustomerSystem = Threadlight.Mirroring.LiveMirroringSystem;

namespace Threadlight.Components.Editor
{
    // Draw source meshes directly: no cloned scripts, hierarchy objects or saved preview state.
    [InitializeOnLoad]
    public static class CustomerGhostPreview
    {
        private const int MaximumTargets = 128;
        private static readonly List<Transform> targets = new List<Transform>();
        private static readonly HashSet<Transform> seen = new HashSet<Transform>();
        private static CustomerSystem[] systems = new CustomerSystem[0];
        private static bool dirty = true;
        private static Mesh bakedMesh;
        private static Material fallbackMaterial;

        static CustomerGhostPreview()
        {
            SceneView.duringSceneGui += Draw;
            EditorApplication.hierarchyChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
        }

        private static void OnPrefabStageChanged(PrefabStage stage) => Invalidate();

        private static void Invalidate() { dirty = true; SceneView.RepaintAll(); }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            Dispose();
            dirty = true;
        }

        private static void Draw(SceneView view)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Event.current.type != EventType.Repaint) return;
            if (dirty)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                systems = stage != null && stage.prefabContentsRoot != null
                    ? stage.prefabContentsRoot.GetComponentsInChildren<CustomerSystem>(true)
                    : Object.FindObjectsOfType<CustomerSystem>(true);
                dirty = false;
            }
            foreach (CustomerSystem system in systems)
                if (system != null && StageUtility.GetStageHandle(system.gameObject).Equals(StageUtility.GetCurrentStageHandle()))
                    DrawSystem(system);
        }

        /// <summary>
        /// Draws one setup in the current camera render context. Call from a Scene view
        /// repaint or camera render callback; it never creates or modifies scene objects.
        /// </summary>
        public static void DrawSystem(CustomerSystem system)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || system == null ||
                !system.isActiveAndEnabled || !system.showScenePreview ||
                system.previewSource == null || system.DataVersion < 0 || system.DataVersion > 3) return;
            CollectTargets(system);
            if (targets.Count == 0) return;
            Material material = ResolveMaterial(system.previewMaterial);
            if (material == null) return;
            Transform source = system.previewSource.transform;
            Matrix4x4 sourceLocal = Matrix4x4.TRS(source.localPosition, source.localRotation, source.localScale);
            foreach (Renderer renderer in system.previewSource.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !VisibleBelowRoot(renderer.transform, source)) continue;
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                {
                    if (bakedMesh == null) bakedMesh = new Mesh { name = "ThreadLight ghost mesh", hideFlags = HideFlags.HideAndDontSave };
                    skin.BakeMesh(bakedMesh, false);
                    mesh = bakedMesh;
                }
                else if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) mesh = filter.sharedMesh;
                }
                if (mesh == null) continue;
                Matrix4x4 local = sourceLocal * source.worldToLocalMatrix * renderer.localToWorldMatrix;
                foreach (Transform target in targets)
                {
                    Matrix4x4 matrix = Matrix4x4.TRS(target.position, target.rotation, target.lossyScale) * local;
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                        if (material.SetPass(0)) Graphics.DrawMeshNow(mesh, matrix, submesh);
                }
            }
        }

        private static bool VisibleBelowRoot(Transform renderer, Transform root)
        {
            for (Transform current = renderer; current != null && current != root; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
        }

        private static void CollectTargets(CustomerSystem system)
        {
            targets.Clear(); seen.Clear();
            if (system.scaleHandles != null) foreach (Transform target in system.scaleHandles) AddTarget(target);
            if (system.pairs == null) return;
            foreach (CustomerSystem.MirrorPair pair in system.pairs)
            {
                if (pair == null) continue;
                AddTarget(pair.sourceTarget);
                if (pair.mirrorEnabled) AddTarget(pair.mirroredTarget);
            }
        }

        private static void AddTarget(Transform target)
        {
            if (target != null && targets.Count < MaximumTargets && seen.Add(target)) targets.Add(target);
        }

        private static Material ResolveMaterial(Material requested)
        {
            if (requested != null && requested.shader != null && requested.shader.isSupported &&
                requested.shader.name != "Hidden/InternalErrorShader") return requested;
            if (fallbackMaterial != null) return fallbackMaterial;
            Shader shader = Shader.Find("Hidden/ThreadLight/CustomerGhostPreview");
            if (shader == null) return null;
            fallbackMaterial = new Material(shader) { name = "ThreadLight ghost preview", hideFlags = HideFlags.HideAndDontSave };
            fallbackMaterial.SetColor("_Color", new Color(.3f, .8f, 1f, .3f));
            return fallbackMaterial;
        }

        private static void Dispose()
        {
            if (bakedMesh != null) Object.DestroyImmediate(bakedMesh);
            if (fallbackMaterial != null) Object.DestroyImmediate(fallbackMaterial);
            bakedMesh = null; fallbackMaterial = null;
            systems = new CustomerSystem[0];
            dirty = true;
            targets.Clear(); seen.Clear();
        }
    }
}
