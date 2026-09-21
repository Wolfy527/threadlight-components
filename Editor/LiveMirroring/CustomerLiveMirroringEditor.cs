using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Threadlight.EditorUI;
using Threadlight.Mirroring;
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
                "Configure live target positioning, shared scaling, and ghost previews.", ThreadlightEditorTheme.WorkspacePrefabAccent));
            var warnings = new VisualElement();
            root.Add(warnings);
            var controls = new VisualElement();
            controls.name = "threadlight-customer-mirroring-controls";
            root.Add(controls);
            var tooltips = new ThreadlightEditorTooltipLayer(root);
            var form = new ThreadlightSerializedForm(serializedObject, ApplyChange,
                path => serializedObject.FindProperty(path)?.boolValue ?? false,
                tooltipForPath: TooltipForPath,
                decorateTooltip: (element, label, tip) => { tooltips.Register(element, label, tip); },
                interactionAccent: () => ThreadlightEditorTheme.WorkspacePrefabAccent);
            controls.Add(ThreadlightEditorElements.CreatePageSection("Mirroring Setup",
                "Move a source target to update its mirrored target. Scale any target or the Prefab Scale Object to synchronize the setup.",
                ThreadlightEditorTheme.WorkspacePrefabAccent, out VisualElement basic));
            form.AddFields(basic,
                ThreadlightFormField.Toggle("Live Mirroring", "liveMirror"),
                ThreadlightFormField.Toggle("Show Scene Preview", "showScenePreview"),
                ThreadlightFormField.Toggle("Synchronize Scale", "applyScaleReference"));
            const string setupSurface = "customer-mirroring.inspector";
            const string setupKey = "setup-settings";
            var setupCard = new ThreadlightDisclosureCard("Mirroring Settings",
                "Configure target mirroring and Scene view previews.",
                ThreadlightEditorTheme.WorkspacePrefabAccent, ThreadlightEditorTheme.PanelInset,
                ThreadlightEditorPreferences.GetSessionState(setupSurface, target, setupKey, false),
                kind: null, expansionChanged: expanded =>
                    ThreadlightEditorPreferences.SetSessionState(setupSurface, target, setupKey, expanded));
            setupCard.style.marginTop = 8;
            controls.Add(setupCard);
            VisualElement setup = setupCard.Content;
            VisualElement settings = setup;
            form.AddFields(settings,
                ThreadlightFormField.ObjectReference("Mirror Center", "mirrorCenter", typeof(Transform)),
                ThreadlightFormField.ObjectReference("Prefab Scale Object", "scaleReference", typeof(Transform)),
                ThreadlightFormField.Enum("Mirror Axis", "mirrorOptions.mirrorAxis"),
                ThreadlightFormField.Toggle("Mirror Position", "mirrorOptions.mirrorPosition"),
                ThreadlightFormField.Toggle("Mirror Rotation", "mirrorOptions.mirrorRotation"),
                ThreadlightFormField.Toggle("Mirror Scale", "mirrorOptions.mirrorScale"),
                ThreadlightFormField.ObjectReference("Preview Object", "previewSource", typeof(GameObject)),
                ThreadlightFormField.ObjectReference("Preview Material", "previewMaterial", typeof(Material)));
            SerializedProperty pairs = serializedObject.FindProperty("pairs");
            if (pairs != null && pairs.arraySize > 0)
                setup.Add(ThreadlightEditorElements.CreateDescription(
                    "Select the source for each mirrored target and set an optional rotation offset."));
            for (int i = 0; pairs != null && i < pairs.arraySize; i++)
            {
                string path = "pairs.Array.data[" + i + "]";
                string label = serializedObject.FindProperty(path + ".pairName")?.stringValue;
                setup.Add(ThreadlightEditorElements.CreatePageSection(
                    string.IsNullOrWhiteSpace(label) ? "Target pair " + (i + 1) : label,
                    null, ThreadlightEditorTheme.WorkspacePrefabAccent,
                    out VisualElement pair));
                form.AddFields(pair,
                    ThreadlightFormField.Toggle("Enable mirroring", path + ".mirrorEnabled"),
                    ThreadlightFormField.ObjectReference("Source target", path + ".sourceTarget", typeof(Transform)),
                    ThreadlightFormField.ObjectReference("Mirrored target", path + ".mirroredTarget", typeof(Transform)),
                    ThreadlightFormField.Vector3("Mirrored rotation offset", path + ".mirroredRotationOffset"));
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
            if (path == "liveMirror") return "Updates mirrored targets automatically while editing.";
            if (path == "showScenePreview") return "Displays ghost copies of the preview object at configured targets in the Scene view.";
            if (path == "applyScaleReference") return "Synchronizes the Prefab Scale Object and all targets when any one is scaled.";
            if (path == "scaleReference") return "Selects the object synchronized with all target scales. An existing SCALE ME object is supported.";
            if (path == "mirrorCenter") return "The center and orientation used to mirror targets to the other side.";
            if (path == "previewSource") return "Uses this object's meshes for ghost previews; scripts are not copied.";
            if (path == "previewMaterial") return "Overrides the built-in ghost material when assigned.";
            if (path == "mirrorOptions.mirrorAxis") return "The mirror center's local axis that separates the two sides.";
            if (path == "mirrorOptions.mirrorPosition") return "Place the mirrored target on the opposite side of the mirror center.";
            if (path == "mirrorOptions.mirrorRotation") return "Mirror the direction the source target faces.";
            if (path == "mirrorOptions.mirrorScale") return "Copy the source target's scale to its mirrored target.";
            if (path.EndsWith(".mirrorEnabled")) return "Mirrors this pair while Live Mirroring is enabled.";
            if (path.EndsWith(".sourceTarget")) return "Move this object to update the mirrored target.";
            if (path.EndsWith(".mirroredTarget")) return "Receives mirrored updates from the source target.";
            if (path.EndsWith(".mirroredRotationOffset")) return "Extra rotation in degrees applied to the mirrored target after mirroring.";
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
            bool damaged = false, newer = false, unsafeScale = false;
            var pairWarnings = new System.Collections.Generic.List<PairWarning>();
            foreach (UnityEngine.Object inspected in targets)
            {
                var system = inspected as CustomerSystem;
                if (system == null) continue;
                damaged |= system.SavedDataVersionStatus ==
                    CustomerSavedDataVersionStatus.Damaged;
                newer |= system.SavedDataVersionStatus ==
                    CustomerSavedDataVersionStatus.NewerThanSupported;
                if (system.HasSupportedDataVersion)
                    CollectPairWarnings(system, pairWarnings);
                unsafeScale |= system.applyScaleReference && system.scaleReference != null &&
                    !CustomerLiveMirroringService.HasValidScaleReferenceTopology(system,
                        CustomerLiveMirroringService.CollectAllTargets(system));
            }
            warnings.Clear();
            if (damaged) warnings.Add(ThreadlightEditorElements.CreateMessage(
                "Saved Setup Damaged",
                "The saved version is invalid. Restore or reimport an unaffected copy before editing.",
                MessageType.Error));
            if (newer) warnings.Add(ThreadlightEditorElements.CreateMessage(
                "ThreadLight Components Update Required",
                "This setup was saved by a newer version. Update ThreadLight Components, then reopen the setup.",
                MessageType.Warning));
            for (int i = 0; i < pairWarnings.Count; i++)
            {
                PairWarning warning = pairWarnings[i];
                warnings.Add(ThreadlightEditorElements.CreateMessage(
                    warning.Title, warning.Message, MessageType.Warning));
            }
            if (unsafeScale) warnings.Add(ThreadlightEditorElements.CreateMessage("Invalid Prefab Scale Object",
                "Choose a child content object inside the prefab root, not the prefab root itself, the Live Mirroring holder, or a target hierarchy. Shared scaling remains paused; position and rotation mirroring remain active.", MessageType.Warning));
            controls.SetEnabled(!damaged && !newer);
        }

        private static void CollectPairWarnings(CustomerSystem system,
            System.Collections.Generic.List<PairWarning> output)
        {
            System.Collections.Generic.IReadOnlyList<CustomerMirroringPairFact> facts =
                CustomerLiveMirroringService.AnalyzePairFacts(system);
            for (int i = 0; i < facts.Count; i++)
            {
                CustomerMirroringPairFact fact = facts[i];
                if (TryDescribePairWarning(fact, out PairWarning warning))
                    output.Add(warning);
            }
        }

        private static bool TryDescribePairWarning(
            CustomerMirroringPairFact fact, out PairWarning warning)
        {
            string pair = PairLabel(fact);
            switch (fact.Status)
            {
                case CustomerMirroringPairStatus.MissingPair:
                    warning = new PairWarning(pair + " is empty",
                        "Remove the empty entry or recreate the pair.");
                    return true;
                case CustomerMirroringPairStatus.MissingReference:
                    warning = new PairWarning(pair + " needs both targets",
                        "Assign a Source target and Mirrored target. Mirroring remains paused until both are assigned.");
                    return true;
                case CustomerMirroringPairStatus.SameObject:
                    warning = new PairWarning(pair + " uses the same object twice",
                        "Choose two different scene objects. A target cannot mirror itself.");
                    return true;
                case CustomerMirroringPairStatus.NestedTargets:
                    warning = new PairWarning(pair + " has nested targets",
                        "Choose targets that are not parented to each other. Mirroring remains paused for this pair.");
                    return true;
                case CustomerMirroringPairStatus.PersistentReference:
                    warning = new PairWarning(pair + " uses a prefab asset",
                        "Choose scene objects. Prefab asset references cannot be mirrored.");
                    return true;
                case CustomerMirroringPairStatus.CrossSceneReference:
                    warning = new PairWarning(pair + " points to another scene",
                        "Choose targets from the same scene as this Live Mirroring setup. Objects in other scenes remain unchanged.");
                    return true;
                case CustomerMirroringPairStatus.DuplicateTarget:
                    warning = new PairWarning(pair + " reuses a mirrored target",
                        "Another pair already controls this target. Choose a different target or keep only one controlling pair.");
                    return true;
                case CustomerMirroringPairStatus.Cycle:
                    warning = new PairWarning(pair + " creates a loop",
                        "The target chain returns to an earlier source target. Choose a different source or mirrored target.");
                    return true;
                default:
                    warning = default;
                    return false;
            }
        }

        private static string PairLabel(CustomerMirroringPairFact fact)
        {
            string name = fact.Pair?.pairName;
            return string.IsNullOrWhiteSpace(name)
                ? "Target pair " + (fact.Index + 1)
                : "Target pair '" + name.Trim() + "'";
        }

        private readonly struct PairWarning
        {
            internal readonly string Title;
            internal readonly string Message;

            internal PairWarning(string title, string message)
            {
                Title = title;
                Message = message;
            }
        }
    }
}
