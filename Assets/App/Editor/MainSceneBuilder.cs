using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARMurals.EditorTools
{
    /// <summary>
    /// Builds the app's main scene in one click: AR rig, ONE reference image library made
    /// from every mural's target photo, every mural prefab found under Assets/Murals, the
    /// shared UI shell, and a campus map start screen with level tabs and one pin per mural.
    ///
    /// Re-runnable, and it is also the merge: when someone pushes a new Mural_Mx_Root
    /// prefab, run this again and it is in the app. Nobody hand-edits the main scene.
    ///
    /// Pins, levels, titles, locations and map images live in Assets/App/MuralDirectory.asset,
    /// which this builder creates once and then keeps.
    /// </summary>
    public static class MainSceneBuilder
    {
        const string TemplateScene = "Assets/Scenes/SampleScene.unity";
        const string AppRoot = "Assets/App";
        const string OutScene = AppRoot + "/Scenes/Main.unity";
        const string LibraryPath = AppRoot + "/MainReferenceImages.asset";
        const string DirectoryPath = AppRoot + "/MuralDirectory.asset";
        const string CampusMap = AppRoot + "/Textures/CampusMap.png";
        const string MuralsRoot = "Assets/Murals";

        // Same palette as the shell, so the map reads as part of the same app.
        static readonly Color Ink = new Color(0.063f, 0.071f, 0.110f, 1f);
        static readonly Color Paper = new Color(0.949f, 0.945f, 0.925f, 1f);
        static readonly Color Muted = new Color(0.65f, 0.67f, 0.74f, 1f);
        static readonly Color Accent = new Color(0.988f, 0.737f, 0.251f, 1f);
        static readonly Color CardBg = new Color(0.078f, 0.090f, 0.137f, 0.98f);

        [MenuItem("AR Murals/3. Build Main Scene (map + all murals)", priority = 2)]
        public static void Build()
        {
            if (!File.Exists(TemplateScene))
            {
                EditorUtility.DisplayDialog("AR Murals",
                    "Could not find " + TemplateScene + ".\nThis builder expects the AR Mobile template's SampleScene.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(AppRoot + "/Scenes");

            var directory = LoadOrCreateDirectory();
            var muralPrefabs = FindMuralPrefabs();
            var library = BuildReferenceLibrary(muralPrefabs);

            var scene = EditorSceneManager.OpenScene(TemplateScene, OpenSceneMode.Single);
            EditorSceneManager.SaveScene(scene, OutScene);
            MuralSceneBuilder.StripTemplateContent(scene);

            var origin = MuralSceneBuilder.FindInScene<XROrigin>();
            if (origin == null)
            {
                EditorUtility.DisplayDialog("AR Murals", "No XR Origin in the template scene.", "OK");
                return;
            }

            MuralSceneBuilder.DisableIfPresent<ARPlaneManager>(origin.gameObject);
            MuralSceneBuilder.DisableIfPresent<ARPointCloudManager>(origin.gameObject);

            var imageManager = origin.GetComponent<ARTrackedImageManager>()
                               ?? origin.gameObject.AddComponent<ARTrackedImageManager>();
            var imo = new SerializedObject(imageManager);
            imo.FindProperty("m_SerializedLibrary").objectReferenceValue = library;
            imo.FindProperty("m_MaxNumberOfMovingImages").intValue = 1;
            imo.ApplyModifiedPropertiesWithoutUndo();

            MuralSceneBuilder.EnsureEventSystem();
            MuralSceneBuilder.TuneCamera();

            var shell = MuralSceneBuilder.BuildUIShell();
            var experiences = PlaceMurals(muralPrefabs, shell);

            var manager = origin.GetComponent<ARMuralManager>() ?? origin.gameObject.AddComponent<ARMuralManager>();
            var mo = new SerializedObject(manager);
            SetList(mo.FindProperty("experiences"), experiences);
            mo.FindProperty("ui").objectReferenceValue = shell;
            mo.ApplyModifiedPropertiesWithoutUndo();

            BuildCampusMap(shell, directory, experiences);
            GeneraliseShellText(shell);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, OutScene);
            MakeFirstInBuild(OutScene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var slots = string.Join(", ", experiences.Select(e => e.MuralSlot));
            Debug.Log("[AR Murals] Main scene built with " + experiences.Count + " mural(s): " + slots + "\n" +
                      "Edit pins, levels, titles and map images in " + DirectoryPath + " - changes show on the next Play, " +
                      "no rebuild needed.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(OutScene));
        }

        // ------------------------------------------------------------------ murals

        /// <summary>Every Mural_Mx_Root prefab under Assets/Murals that carries an experience, in slot order.</summary>
        static List<MuralExperienceBase> FindMuralPrefabs()
        {
            var naming = new Regex(@"^Mural_M\d+_Root$");
            var found = new List<MuralExperienceBase>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { MuralsRoot }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go == null || !naming.IsMatch(go.name)) continue;
                var exp = go.GetComponent<MuralExperienceBase>();
                if (exp != null) found.Add(exp);
            }
            return found.OrderBy(e => e.MuralSlot).ToList();
        }

        static List<MuralExperienceBase> PlaceMurals(List<MuralExperienceBase> prefabs, UIShellController shell)
        {
            var placed = new List<MuralExperienceBase>();
            foreach (var prefab in prefabs)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
                var exp = go.GetComponent<MuralExperienceBase>();

                // A prefab cannot hold a reference to the scene's UI shell, so re-wire it
                // here for any experience that follows M1's convention of a field named "ui".
                var so = new SerializedObject(exp);
                var ui = so.FindProperty("ui");
                if (ui != null && ui.propertyType == SerializedPropertyType.ObjectReference)
                {
                    ui.objectReferenceValue = shell;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                placed.Add(exp);
            }
            return placed;
        }

        static string MuralFolder(string slot)
        {
            if (!Directory.Exists(MuralsRoot)) return null;
            var dir = Directory.GetDirectories(MuralsRoot, slot + "_*").FirstOrDefault();
            return dir?.Replace('\\', '/');
        }

        /// <summary>The mural's target photo: Assets/Murals/Mx_*/Textures/Mx_target.(jpg|png).</summary>
        static string TargetImagePath(string slot)
        {
            var folder = MuralFolder(slot);
            if (folder == null || !Directory.Exists(folder + "/Textures")) return null;
            return Directory.GetFiles(folder + "/Textures", slot + "_target.*")
                .Where(p => !p.EndsWith(".meta"))
                .Select(p => p.Replace('\\', '/'))
                .FirstOrDefault();
        }

        static XRReferenceImageLibrary BuildReferenceLibrary(List<MuralExperienceBase> murals)
        {
            var lib = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            while (lib.count > 0) lib.RemoveAt(lib.count - 1);

            foreach (var mural in murals)
            {
                var path = TargetImagePath(mural.MuralSlot);
                if (path == null)
                {
                    Debug.LogWarning($"[AR Murals] {mural.MuralSlot}: no {mural.MuralSlot}_target.jpg/png in its Textures folder, " +
                                     "so it is in the app but cannot be detected yet.");
                    continue;
                }

                MuralSceneBuilder.Import(path, false);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                float width = mural.MuralWidthMeters;
                float height = width * tex.height / Mathf.Max(1, tex.width);

                lib.Add();
                int i = lib.count - 1;
                lib.SetTexture(i, tex, true);
                lib.SetName(i, mural.MuralSlot);
                lib.SetSpecifySize(i, true);
                lib.SetSize(i, new Vector2(width, height));
            }

            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        // ------------------------------------------------------------------ directory + map image

        static MuralDirectory LoadOrCreateDirectory()
        {
            var dir = AssetDatabase.LoadAssetAtPath<MuralDirectory>(DirectoryPath);
            if (dir == null)
            {
                dir = ScriptableObject.CreateInstance<MuralDirectory>();
                dir.murals = DefaultEntries();
                AssetDatabase.CreateAsset(dir, DirectoryPath);
            }

            // Fill gaps only - never overwrite what the team has typed in.
            if (dir.campusMap == null) dir.campusMap = LoadMapTexture(CampusMap);
            foreach (var e in dir.murals)
            {
                if (e.photo != null) continue;
                var path = TargetImagePath(e.slot);
                if (path != null) e.photo = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            EditorUtility.SetDirty(dir);
            AssetDatabase.SaveAssets();
            return dir;
        }

        // Levels and pin positions are placeholders until the team confirms where each mural is.
        static List<MuralDirectory.Entry> DefaultEntries() => new List<MuralDirectory.Entry>
        {
            new MuralDirectory.Entry { slot = "M1", title = "Emergence", level = 1, mapPosition = new Vector2(0.18f, 0.52f),
                                       blurb = "A bird in flight that steps off the wall and takes to the air." },
            new MuralDirectory.Entry { slot = "M2", title = "Reconstruct", level = 1, mapPosition = new Vector2(0.48f, 0.61f) },
            new MuralDirectory.Entry { slot = "M3", title = "Expansion", level = 2, mapPosition = new Vector2(0.72f, 0.60f) },
            new MuralDirectory.Entry { slot = "M4", title = "Story", level = 2, mapPosition = new Vector2(0.25f, 0.28f) },
            new MuralDirectory.Entry { slot = "M5", title = "Living Paint", level = 3, mapPosition = new Vector2(0.65f, 0.22f) },
        };

        static Texture2D LoadMapTexture(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                // A crisp UI image: no mipmaps, no compression blur on the thin outlines.
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) Debug.LogWarning("[AR Murals] No campus map at " + path + ". Assign one on " + DirectoryPath + ".");
            return tex;
        }

        // ------------------------------------------------------------------ start screen = campus map

        static void BuildCampusMap(UIShellController shell, MuralDirectory directory, List<MuralExperienceBase> experiences)
        {
            var start = shell.transform.Find("StartPanel");
            var scan = shell.transform.Find("ScanPanel");

            // The map replaces the shell's title-and-button start screen.
            for (int i = start.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(start.GetChild(i).gameObject);

            Label(start, "Kicker", "ALU CAMPUS", 30, new Vector2(0, 840), new Vector2(900, 50), Muted);
            Label(start, "Title", "MURALS, IN MOTION", 76, new Vector2(0, 755), new Vector2(1000, 110), Paper);
            Label(start, "Subtitle", "Pick a level, then tap a pin", 34, new Vector2(0, 670), new Vector2(900, 60), Muted);

            // ---- level tabs: ALL, L1, L2, L3
            var tabs = new List<Button>();
            int tabCount = directory.levelCount + 1;
            const float tabW = 200f, tabGap = 20f;
            float x0 = -(tabCount * tabW + (tabCount - 1) * tabGap) / 2f + tabW / 2f;
            for (int i = 0; i < tabCount; i++)
            {
                var label = i == 0 ? "ALL" : "L" + i;
                tabs.Add(MakeButton(start, "Tab_" + label, label, new Vector2(x0 + i * (tabW + tabGap), 580),
                                    new Vector2(tabW, 90)));
            }

            var box = Box(start, "MapBox", new Vector2(0, 60), new Vector2(940, 940), new Color(1f, 1f, 1f, 0f));
            var mapGO = new GameObject("MapImage", typeof(RawImage), typeof(AspectRatioFitter));
            mapGO.transform.SetParent(box, false);
            var map = mapGO.GetComponent<RawImage>();
            map.raycastTarget = false;
            var mapFit = mapGO.GetComponent<AspectRatioFitter>();
            mapFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            mapFit.aspectRatio = 1f;

            var pinLayer = new GameObject("PinLayer", typeof(RectTransform)).GetComponent<RectTransform>();
            pinLayer.SetParent(mapGO.transform, false);
            Stretch(pinLayer);

            var pinTemplate = Pin(pinLayer);
            var progress = Label(start, "Progress", "0 of 5 murals found", 32, new Vector2(0, -470), new Vector2(900, 60), Muted);
            var scanAny = MakeButton(start, "ScanAnyButton", "SCAN ANY MURAL", new Vector2(0, -620), new Vector2(620, 130), Accent, Ink);

            // ---- mural card, a bottom sheet over the lower part of the map
            var card = Box(start, "MuralCard", Vector2.zero, Vector2.zero, CardBg);
            card.anchorMin = new Vector2(0, 0); card.anchorMax = new Vector2(1, 0);
            card.pivot = new Vector2(0.5f, 0f);
            card.offsetMin = Vector2.zero; card.offsetMax = new Vector2(0, 720);
            Rule(card, new Vector2(0, 320), new Vector2(100, 8), Accent);

            var photoBox = Box(card, "PhotoBox", new Vector2(-330, 90), new Vector2(320, 320), new Color(1f, 1f, 1f, 0.05f));
            var cardPhoto = Photo(photoBox, "Photo", AspectRatioFitter.AspectMode.EnvelopeParent);
            photoBox.gameObject.AddComponent<RectMask2D>();

            var cardTitle = Label(card, "CardTitle", "MURAL", 46, new Vector2(150, 215), new Vector2(640, 70), Paper);
            cardTitle.alignment = TextAnchor.MiddleLeft;
            cardTitle.fontStyle = FontStyle.Bold;
            var cardLocation = Label(card, "CardLocation", "Location", 30, new Vector2(150, 155), new Vector2(640, 50), Accent);
            cardLocation.alignment = TextAnchor.MiddleLeft;
            var cardBlurb = Label(card, "CardBlurb", "", 30, new Vector2(150, 20), new Vector2(640, 200), new Color(0.82f, 0.84f, 0.89f, 1f));
            cardBlurb.alignment = TextAnchor.UpperLeft;

            var cardScan = MakeButton(card, "ScanThisButton", "SCAN THIS MURAL", new Vector2(-90, -220), new Vector2(620, 130), Accent, Ink);
            var cardClose = MakeButton(card, "CloseButton", "CLOSE", new Vector2(340, -220), new Vector2(240, 130));

            // ---- scanning screen: back to the map, and a ghost of the mural being looked for
            var thumbBox = Box(scan, "ThumbBox", new Vector2(0, 630), new Vector2(380, 260), new Color(0, 0, 0, 0));
            var scanThumb = Photo(thumbBox, "ScanThumb", AspectRatioFitter.AspectMode.FitInParent);
            var back = MakeButton(scan, "BackToMapButton", "< MAP", new Vector2(-390, 860), new Vector2(240, 96));

            // ---- wire the controller
            var ctrl = shell.gameObject.AddComponent<CampusMapController>();
            var so = new SerializedObject(ctrl);
            so.FindProperty("shell").objectReferenceValue = shell;
            so.FindProperty("directory").objectReferenceValue = directory;
            SetList(so.FindProperty("experiences"), experiences);
            so.FindProperty("mapImage").objectReferenceValue = map;
            so.FindProperty("mapFitter").objectReferenceValue = mapFit;
            so.FindProperty("pinLayer").objectReferenceValue = pinLayer;
            so.FindProperty("pinTemplate").objectReferenceValue = pinTemplate;
            so.FindProperty("progressLabel").objectReferenceValue = progress;
            so.FindProperty("scanAnyButton").objectReferenceValue = scanAny;
            SetList(so.FindProperty("levelTabs"), tabs);
            so.FindProperty("card").objectReferenceValue = card.gameObject;
            so.FindProperty("cardPhoto").objectReferenceValue = cardPhoto;
            so.FindProperty("cardTitle").objectReferenceValue = cardTitle;
            so.FindProperty("cardLocation").objectReferenceValue = cardLocation;
            so.FindProperty("cardBlurb").objectReferenceValue = cardBlurb;
            so.FindProperty("cardScanButton").objectReferenceValue = cardScan;
            so.FindProperty("cardCloseButton").objectReferenceValue = cardClose;
            so.FindProperty("scanThumb").objectReferenceValue = scanThumb;
            so.FindProperty("scanHint").objectReferenceValue = scan.Find("ScanHint")?.GetComponent<Text>();
            so.FindProperty("scanSub").objectReferenceValue = scan.Find("ScanSub")?.GetComponent<Text>();
            so.FindProperty("backToMapButton").objectReferenceValue = back;
            so.ApplyModifiedPropertiesWithoutUndo();

            // The shell's own begin button went with the old start screen; SCAN ANY takes its place.
            var shellSO = new SerializedObject(shell);
            shellSO.FindProperty("beginButton").objectReferenceValue = scanAny;
            shellSO.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The shell was written for M1 alone; make its shared text fit every mural.</summary>
        static void GeneraliseShellText(UIShellController shell)
        {
            SetText(shell, "ARControls/Nudge", "Tap and drag to explore the mural");
            SetText(shell, "InfoPanel/Card/InfoTitle", "ABOUT THIS MURAL");
            SetText(shell, "ExitPanel/ExitBlurb", "More murals are waiting on campus. Head back to the map and pick the next one.");
            SetText(shell, "ExitPanel/RestartButton/Label", "BACK TO MAP");
        }

        // ------------------------------------------------------------------ UI helpers

        static Button Pin(Transform parent)
        {
            var go = new GameObject("PinTemplate", typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(92, 92);

            var img = go.GetComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            img.color = Accent;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Color.white;     // the controller sets the base colour
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            btn.colors = colors;

            var number = Label(go.transform, "Number", "1", 40, Vector2.zero, new Vector2(92, 92), Ink);
            number.fontStyle = FontStyle.Bold;
            // Dark label with a white halo, so it reads on the white line map.
            var name = Label(go.transform, "Name", "Mural", 26, new Vector2(0, -78), new Vector2(360, 50), Ink);
            name.fontStyle = FontStyle.Bold;
            var halo = name.gameObject.AddComponent<Outline>();
            halo.effectColor = Color.white;
            halo.effectDistance = new Vector2(2f, -2f);
            return btn;
        }

        static RawImage Photo(Transform parent, string name, AspectRatioFitter.AspectMode mode)
        {
            var go = new GameObject(name, typeof(RawImage), typeof(AspectRatioFitter));
            go.transform.SetParent(parent, false);
            var fit = go.GetComponent<AspectRatioFitter>();
            fit.aspectMode = mode;
            fit.aspectRatio = 1f;
            var img = go.GetComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        static RectTransform Box(Transform parent, string name, Vector2 pos, Vector2 size, Color bg)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = bg.a > 0.5f;    // the card blocks taps on the map beneath it
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static void Rule(Transform parent, Vector2 pos, Vector2 size, Color c)
        {
            var img = Box(parent, "Rule", pos, size, c).GetComponent<Image>();
            img.raycastTarget = false;
        }

        static Text Label(Transform parent, string name, string text, int size, Vector2 pos, Vector2 box, Color color)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;

            var t = go.GetComponent<Text>();
            t.font = MuralSceneBuilder.DefaultFont();
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size,
                                 Color? fill = null, Color? ink = null)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = fill ?? new Color(1f, 1f, 1f, 0.14f);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            var t = Label(go.transform, "Label", label, 38, Vector2.zero, size, ink ?? Paper);
            t.fontStyle = FontStyle.Bold;
            return btn;
        }

        static void SetText(UIShellController shell, string path, string text)
        {
            var t = shell.transform.Find(path);
            if (t != null && t.TryGetComponent<Text>(out var label)) label.text = text;
        }

        // ------------------------------------------------------------------ plumbing

        static void SetList<T>(SerializedProperty list, List<T> items) where T : Object
        {
            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>The main scene has to be scene 0 so the phone build opens on the map.</summary>
        static void MakeFirstInBuild(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
