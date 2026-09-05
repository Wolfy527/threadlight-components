using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Threadlight.EditorUI;
using CustomerSystem = Threadlight.Mirroring.LiveMirroringSystem;

namespace Threadlight.Components.Editor
{
    [CustomEditor(typeof(CustomerSystem), true), CanEditMultipleObjects]
    public sealed class CustomerLiveMirroringEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = ThreadlightEditorElements.CreateInspectorRoot();
            root.Add(ThreadlightEditorElements.CreateInspectorBanner("Live Mirroring System",
                "Position your prop with live targets and ghost previews.", ThreadlightEditorTheme.WorkspacePrefabAccent));
            var warnings = new VisualElement();
            root.Add(warnings);
            var controls = new VisualElement();
            root.Add(controls);
            var tooltips = new ThreadlightEditorTooltipLayer(root);
            var form = new ThreadlightSerializedForm(serializedObject, ApplyChange,
                path => serializedObject.FindProperty(path)?.boolValue ?? false,
                tooltipForPath: TooltipForPath,
                decorateTooltip: (element, label, tip) => { tooltips.Register(element, label, tip); },
                interactionAccent: () => ThreadlightEditorTheme.WorkspacePrefabAccent);
            controls.Add(ThreadlightEditorElements.CreatePageSection("Customer setup",
                "Move a source target to position the prop. Scale any target or the scale container to synchronize their sizes.",
                ThreadlightEditorTheme.WorkspacePrefabAccent, out VisualElement basic));
            form.AddFields(basic,
                ThreadlightFormField.Toggle("Live mirroring", "liveMirror"),
                ThreadlightFormField.Toggle("Show ghosts", "showScenePreview"),
                ThreadlightFormField.Toggle("Shared scaling", "applyScaleReference"));
            var setup = new VisualElement();
            setup.style.display = DisplayStyle.None;
            bool expanded = false;
            Button settingsButton = null;
            settingsButton = ThreadlightEditorElements.CreateCompactButton("Show setup settings", () => {
                expanded = !expanded;
                setup.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                settingsButton.text = expanded ? "Hide setup settings" : "Show setup settings";
            });
            controls.Add(settingsButton);
            controls.Add(setup);
            setup.Add(ThreadlightEditorElements.CreatePageSection("Mirroring settings", "Configure the existing targets and preview.",
                ThreadlightEditorTheme.WorkspacePrefabAccent, out VisualElement settings));
            form.AddFields(settings,
                ThreadlightFormField.ObjectReference("Mirror center", "mirrorCenter", typeof(Transform)),
                ThreadlightFormField.ObjectReference("Scale container", "scaleReference", typeof(Transform)),
                ThreadlightFormField.Enum("Mirror axis", "mirrorOptions.mirrorAxis"),
                ThreadlightFormField.Toggle("Mirror position", "mirrorOptions.mirrorPosition"),
                ThreadlightFormField.Toggle("Mirror rotation", "mirrorOptions.mirrorRotation"),
                ThreadlightFormField.Toggle("Mirror scale", "mirrorOptions.mirrorScale"),
                ThreadlightFormField.ObjectReference("Preview object", "previewSource", typeof(GameObject)),
                ThreadlightFormField.ObjectReference("Ghost material", "previewMaterial", typeof(Material)));
            SerializedProperty pairs = serializedObject.FindProperty("pairs");
            for (int i = 0; pairs != null && i < pairs.arraySize; i++)
            {
                string path = "pairs.Array.data[" + i + "]";
                string label = serializedObject.FindProperty(path + ".pairName")?.stringValue;
                setup.Add(ThreadlightEditorElements.CreatePageSection(
                    string.IsNullOrWhiteSpace(label) ? "Target pair " + (i + 1) : label,
                    "Adjust the existing pair without regenerating targets.", ThreadlightEditorTheme.WorkspacePrefabAccent,
                    out VisualElement pair));
                form.AddFields(pair,
                    ThreadlightFormField.Toggle("Mirror this pair", path + ".mirrorEnabled"),
                    ThreadlightFormField.ObjectReference("Source target", path + ".sourceTarget", typeof(Transform)),
                    ThreadlightFormField.ObjectReference("Mirrored target", path + ".mirroredTarget", typeof(Transform)),
                    ThreadlightFormField.Vector3("Rotation offset", path + ".mirroredRotationOffset"));
            }
            RefreshWarnings(warnings, controls);
            root.TrackSerializedObjectValue(serializedObject, _ => {
                RefreshWarnings(warnings, controls);
                SceneView.RepaintAll();
            });
            return root;
        }

        private string TooltipForPath(string path)
        {
            if (path == "liveMirror") return "Update mirrored targets automatically while editing your prop.";
            if (path == "showScenePreview") return "Show ghost copies of your prop at the configured targets in Scene view.";
            if (path == "applyScaleReference") return "Scale any target or the scale container to keep the whole setup synchronized.";
            if (path == "scaleReference") return "The prefab container that shares its scale with the targets. Existing SCALE ME containers remain supported.";
            if (path == "mirrorCenter") return "The transform defining the position and orientation of the mirror plane.";
            if (path == "previewSource") return "The object whose meshes are drawn as ghosts. Previewing does not clone its scripts.";
            if (path == "previewMaterial") return "An optional ghost material. A built-in preview material is used when this is unavailable.";
            return ThreadlightEditorTooltips.Get(string.Empty, serializedObject.FindProperty(path));
        }

        private void ApplyChange(string path, string undoName, Action<SerializedProperty> change)
        {
            serializedObject.Update();
            SerializedProperty property = serializedObject.FindProperty(path);
            if (property == null) return;
            Undo.SetCurrentGroupName(undoName);
            change(property);
            serializedObject.ApplyModifiedProperties();
            SceneView.RepaintAll();
        }

        private void RefreshWarnings(VisualElement warnings, VisualElement controls)
        {
            bool unsupported = false, unsafeScale = false;
            foreach (UnityEngine.Object inspected in targets)
            {
                var system = inspected as CustomerSystem;
                if (system == null) continue;
                unsupported |= !system.HasSupportedDataVersion;
                unsafeScale |= system.applyScaleReference && system.scaleReference != null &&
                    !CustomerLiveMirroringService.HasValidScaleReferenceTopology(system,
                        CustomerLiveMirroringService.CollectAllTargets(system));
            }
            warnings.Clear();
            if (unsupported) warnings.Add(ThreadlightEditorElements.CreateMessage("Update required",
                "This setup uses an unsupported data version. Update ThreadLight Components before editing it.", MessageType.Warning));
            if (unsafeScale) warnings.Add(ThreadlightEditorElements.CreateMessage("Scale reference needs attention",
                "This scale reference overlaps the target hierarchy. Shared scaling is paused to preserve your setup; position and rotation mirroring remain available.", MessageType.Warning));
            controls.SetEnabled(!unsupported);
        }
    }
}
