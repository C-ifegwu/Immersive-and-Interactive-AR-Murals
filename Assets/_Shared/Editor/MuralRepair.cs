using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARMurals.EditorTools
{
    /// <summary>
    /// Small repairs for the open scene. Safe to run any time; it only fills in
    /// references that are missing and reports what it found.
    /// </summary>
    public static class MuralRepair
    {
        [MenuItem("AR Murals/Check My Scene", priority = 40)]
        public static void Check()
        {
            var origin = MuralSceneBuilder.FindInScene<XROrigin>();
            if (origin == null)
            {
                EditorUtility.DisplayDialog("AR Murals",
                    "No XR Origin in the open scene. Open your mural scene first.", "OK");
                return;
            }

            var report = "XR Origin: found\n";

            var session = MuralSceneBuilder.FindInScene<ARSession>();
            report += "AR Session: " + (session != null ? "found" : "MISSING") + "\n";

            var img = origin.GetComponent<ARTrackedImageManager>();
            if (img == null)
            {
                img = origin.gameObject.AddComponent<ARTrackedImageManager>();
                report += "AR Tracked Image Manager: was missing, ADDED\n";
            }
            else report += "AR Tracked Image Manager: found\n";

            // The library is the thing that most often ends up unassigned.
            if (img.referenceLibrary == null)
            {
                var lib = FindLibrary();
                if (lib != null)
                {
                    var so = new SerializedObject(img);
                    so.FindProperty("m_SerializedLibrary").objectReferenceValue = lib;
                    so.FindProperty("m_MaxNumberOfMovingImages").intValue = 1;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    report += "Reference image library: was EMPTY, assigned " + lib.name + "\n";
                }
                else report += "Reference image library: EMPTY and none found in the project\n";
            }
            else report += "Reference image library: " + ((Object)img.referenceLibrary).name + "\n";

            var mgr = origin.GetComponent<ARMuralManager>();
            report += "AR Mural Manager: " + (mgr != null ? "found" : "MISSING") + "\n";

            var ui = MuralSceneBuilder.FindInScene<UIShellController>();
            report += "UI Shell: " + (ui != null ? "found" : "MISSING") + "\n";

            var exp = MuralSceneBuilder.FindInScene<MuralExperienceBase>();
            report += "Mural experience: " + (exp != null ? exp.GetType().Name + " (slot " + exp.MuralSlot + ")" : "MISSING") + "\n";

            // The launch scene must be this one, or the APK opens the AR template demo instead.
            var scenePath = EditorSceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(scenePath))
            {
                var first = EditorBuildSettings.scenes.Length > 0 ? EditorBuildSettings.scenes[0].path : null;
                if (first != scenePath)
                {
                    MuralSceneBuilder.AddToBuildSettings(scenePath);
                    report += "Launch scene: was " + (first ?? "none") + ", FIXED to this scene\n";
                }
                else report += "Launch scene: this scene\n";
            }

            report += FixInputModule();

            var ok = img.referenceLibrary != null && mgr != null && ui != null && exp != null && session != null;
            report += ok ? "\nEverything required is present." : "\nSomething above is missing - see the lines marked MISSING.";

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[AR Murals] Scene check:\n" + report);
            EditorUtility.DisplayDialog("AR Murals - scene check", report, "OK");
        }

        /// <summary>
        /// The AR template ships an EventSystem driven by XRUIInputModule, which routes UI
        /// through XR ray interactors. This app's UI is a plain screen-space canvas you tap
        /// with a finger, so it needs InputSystemUIInputModule instead - without it every
        /// button in the app is dead, on the phone as well as in Play mode.
        /// </summary>
        static string FixInputModule()
        {
            var es = MuralSceneBuilder.FindInScene<EventSystem>();
            if (es == null) return "UI input module: NO EVENTSYSTEM\n";

            if (es.GetComponent<InputSystemUIInputModule>() != null)
                return "UI input module: InputSystemUIInputModule\n";

            var removed = 0;
            foreach (var m in es.GetComponents<BaseInputModule>())
            {
                if (m == null) continue;
                Object.DestroyImmediate(m);
                removed++;
            }

            var mod = es.gameObject.AddComponent<InputSystemUIInputModule>();
            mod.AssignDefaultActions();
            return "UI input module: replaced " + removed + " module(s) with InputSystemUIInputModule\n";
        }

        static XRReferenceImageLibrary FindLibrary()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:XRReferenceImageLibrary"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var lib = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(path);
                if (lib != null && lib.count > 0) return lib;
            }
            return null;
        }
    }
}
