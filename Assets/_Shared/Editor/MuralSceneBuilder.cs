using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARMurals.EditorTools
{
    /// <summary>
    /// Builds the complete M1 scene: AR rig, reference image library, mural surface,
    /// the emerging bird rig, particles, audio and the full five-screen UI flow.
    ///
    /// Re-runnable. It always rebuilds from the AR Mobile template's SampleScene, so
    /// Unity's own AR rig is never hand-assembled and nothing drifts.
    /// </summary>
    public static class MuralSceneBuilder
    {
        const string TemplateScene = "Assets/Scenes/SampleScene.unity";
        const string Root = "Assets/Murals/M1_Emergence";
        const string OutScene = Root + "/Scenes/M1_Emergence.unity";
        const string PrefabPath = Root + "/Prefabs/Mural_M1_Root.prefab";
        const string LibraryPath = Root + "/M1_ReferenceImages.asset";
        const string MuralTex = Root + "/Textures/M1_target.jpg";
        const string OverlayTex = Root + "/Textures/M1_painted_region.png";
        const string MatFolder = Root + "/Materials";

        // Real-world mural size in metres and where the bird is painted on it.
        const float MuralW = 2.00f;
        const float MuralH = 1.40f;
        static readonly Vector3 PaintedLocal = new Vector3(-0.4004f, 0.1689f, 0.012f);
        static readonly Vector2 OverlaySize = new Vector2(0.918f, 0.917f);

        [MenuItem("AR Murals/2. Build M1 Emergence Scene", priority = 1)]
        public static void BuildM1()
        {
            if (!File.Exists(TemplateScene))
            {
                EditorUtility.DisplayDialog("AR Murals",
                    "Could not find " + TemplateScene + ".\nThis builder expects the AR Mobile template's SampleScene.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            PrepareTextureImports();
            var library = BuildReferenceLibrary();

            var scene = EditorSceneManager.OpenScene(TemplateScene, OpenSceneMode.Single);
            Directory.CreateDirectory(Path.GetDirectoryName(OutScene));
            EditorSceneManager.SaveScene(scene, OutScene);
            StripTemplateContent(scene);

            var origin = FindInScene<XROrigin>();
            if (origin == null)
            {
                EditorUtility.DisplayDialog("AR Murals", "No XR Origin in the template scene.", "OK");
                return;
            }

            DisableIfPresent<ARPlaneManager>(origin.gameObject);
            DisableIfPresent<ARPointCloudManager>(origin.gameObject);

            var imageManager = origin.GetComponent<ARTrackedImageManager>()
                               ?? origin.gameObject.AddComponent<ARTrackedImageManager>();
            var imo = new SerializedObject(imageManager);
            imo.FindProperty("m_SerializedLibrary").objectReferenceValue = library;
            imo.FindProperty("m_MaxNumberOfMovingImages").intValue = 1;
            imo.ApplyModifiedPropertiesWithoutUndo();

            EnsureEventSystem();
            TuneCamera();

            var ui = BuildUIShell();
            var mural = BuildMuralRoot(ui);

            var manager = origin.GetComponent<ARMuralManager>() ?? origin.gameObject.AddComponent<ARMuralManager>();
            var mo = new SerializedObject(manager);
            var list = mo.FindProperty("experiences");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = mural;
            mo.FindProperty("ui").objectReferenceValue = ui;
            mo.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAssetAndConnect(mural.gameObject, PrefabPath, InteractionMode.AutomatedAction);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, OutScene);
            AddToBuildSettings(OutScene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[AR Murals] M1 scene built.\n" +
                      "Open " + OutScene + ", press Play, then:\n" +
                      "  F  tracking found  (runs the full emergence)\n" +
                      "  L  tracking lost   (content hides, state kept)\n" +
                      "  R  reset           (mural back to untouched)\n" +
                      "Click the bird to inspect it. Drag left/right to steer it.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(OutScene));
        }

        // ------------------------------------------------------------------ assets

        static void PrepareTextureImports()
        {
            Import(MuralTex, false);
            Import(OverlayTex, true);
        }

        internal static void Import(string path, bool alpha)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti) return;
            ti.textureType = TextureImporterType.Default;
            ti.isReadable = true;                 // the reference image library needs to read it
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            ti.alphaIsTransparency = alpha;
            ti.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.SaveAndReimport();
        }

        static XRReferenceImageLibrary BuildReferenceLibrary()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MuralTex);
            if (tex == null)
            {
                Debug.LogWarning("[AR Murals] " + MuralTex + " not found, skipping the reference image library.");
                return null;
            }

            var lib = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            while (lib.count > 0) lib.RemoveAt(lib.count - 1);

            lib.Add();
            lib.SetTexture(0, tex, true);
            lib.SetName(0, "M1");
            lib.SetSpecifySize(0, true);
            lib.SetSize(0, new Vector2(MuralW, MuralH));

            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        // ------------------------------------------------------------------ scene scaffolding

        internal static void StripTemplateContent(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                bool keep = root.GetComponentInChildren<ARSession>(true) != null
                         || root.GetComponentInChildren<XROrigin>(true) != null
                         || root.GetComponentInChildren<Light>(true) != null
                         || root.GetComponentInChildren<EventSystem>(true) != null;
                if (!keep) Object.DestroyImmediate(root);
            }
        }

        /// <summary>First component of this type anywhere in the open scene, active or not.</summary>
        internal static T FindInScene<T>() where T : Component
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var c = root.GetComponentInChildren<T>(true);
                if (c != null) return c;
            }
            return null;
        }

        internal static void DisableIfPresent<T>(GameObject go) where T : MonoBehaviour
        {
            var c = go.GetComponent<T>();
            if (c != null) c.enabled = false;
        }

        internal static void EnsureEventSystem()
        {
            if (FindInScene<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        internal static void TuneCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.nearClipPlane = 0.05f;      // the bird comes within half a metre
            cam.farClipPlane = 25f;
        }

        // ------------------------------------------------------------------ mural + bird

        static MuralExperienceBase BuildMuralRoot(UIShellController ui)
        {
            var old = GameObject.Find("Mural_M1_Root");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("Mural_M1_Root");
            var content = new GameObject("Content");
            content.transform.SetParent(root.transform, false);

            // --- the mural surface (placeholder only; the real wall replaces this)
            var muralMat = TexturedMaterial("M1_Mural", MuralTex, false, Color.white);
            var plane = Quad("MuralPlane_PLACEHOLDER", content.transform, MuralW, MuralH, muralMat);

            // --- the painted bird, as a photographic patch that will dim away
            var overlayMat = TexturedMaterial("M1_PaintedOverlay", OverlayTex, true, Color.white);
            var overlay = Quad("PaintedOverlay", content.transform, OverlaySize.x, OverlaySize.y, overlayMat);
            overlay.transform.localPosition = new Vector3(PaintedLocal.x, PaintedLocal.y, 0.004f);

            // --- the bird
            var bodyMat = ColorMaterial("M1_BirdBody", new Color(0.937f, 0.925f, 0.886f), true);
            var accentMat = ColorMaterial("M1_BirdAccent", new Color(0.886f, 0.573f, 0.173f), true);
            var darkMat = ColorMaterial("M1_BirdDark", new Color(0.110f, 0.118f, 0.180f), true);

            var subject = new GameObject("EmergingSubject");
            subject.transform.SetParent(content.transform, false);
            subject.transform.localPosition = PaintedLocal;

            var renderers = new List<Renderer>();
            Part(subject.transform, "Body", PrimitiveType.Sphere, new Vector3(0, 0, 0),
                 new Vector3(0.135f, 0.115f, 0.265f), bodyMat, renderers);
            Part(subject.transform, "Head", PrimitiveType.Sphere, new Vector3(0, 0.055f, 0.145f),
                 new Vector3(0.095f, 0.095f, 0.105f), bodyMat, renderers);
            Part(subject.transform, "Beak", PrimitiveType.Cube, new Vector3(0, 0.045f, 0.215f),
                 new Vector3(0.030f, 0.028f, 0.085f), accentMat, renderers);
            Part(subject.transform, "EyeL", PrimitiveType.Sphere, new Vector3(-0.034f, 0.078f, 0.175f),
                 new Vector3(0.020f, 0.020f, 0.020f), darkMat, renderers);
            Part(subject.transform, "EyeR", PrimitiveType.Sphere, new Vector3(0.034f, 0.078f, 0.175f),
                 new Vector3(0.020f, 0.020f, 0.020f), darkMat, renderers);
            Part(subject.transform, "Tail", PrimitiveType.Cube, new Vector3(0, 0.010f, -0.215f),
                 new Vector3(0.105f, 0.014f, 0.165f), accentMat, renderers);

            var wingL = Wing(subject.transform, "WingL", -1f, bodyMat, darkMat, renderers);
            var wingR = Wing(subject.transform, "WingR", 1f, bodyMat, darkMat, renderers);

            var box = subject.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = new Vector3(0.85f, 0.26f, 0.55f);   // generous: this is a tap target on a phone
            box.enabled = false;

            var lightGO = new GameObject("SubjectLight");
            lightGO.transform.SetParent(subject.transform, false);
            var pLight = lightGO.AddComponent<Light>();
            pLight.type = LightType.Point;
            pLight.color = new Color(1f, 0.86f, 0.62f);
            pLight.range = 1.4f;
            pLight.intensity = 0f;
            pLight.enabled = false;

            // --- transition particles, at the point the bird leaves the wall
            var pGO = new GameObject("EmergeParticles");
            pGO.transform.SetParent(content.transform, false);
            pGO.transform.localPosition = PaintedLocal;
            var ps = pGO.AddComponent<ParticleSystem>();
            ConfigureParticles(ps, accentMat);

            // --- the four named audio slots, with the placeholder clips loaded
            BindAudio(root.transform, "SFX_Detect", Root + "/Audio/SFX_Detect.wav", false, 0f);
            BindAudio(root.transform, "SFX_Lost", Root + "/Audio/SFX_Lost.wav", false, 0f);
            BindAudio(root.transform, "SFX_Confirm", Root + "/Audio/SFX_Confirm.wav", false, 0f);
            BindAudio(root.transform, "Ambient_Bed", Root + "/Audio/Ambient_Bed.wav", true, 1f);

            // --- wire the experience
            var exp = root.AddComponent<M1EmergenceExperience>();
            var so = new SerializedObject(exp);
            so.FindProperty("muralSlot").stringValue = "M1";
            so.FindProperty("muralWidthMeters").floatValue = MuralW;
            so.FindProperty("contentRoot").objectReferenceValue = content.transform;
            so.FindProperty("placeholderMuralPlane").objectReferenceValue = plane;
            so.FindProperty("paintedOverlay").objectReferenceValue = overlay.GetComponent<MeshRenderer>();
            so.FindProperty("subject").objectReferenceValue = subject.transform;
            so.FindProperty("wingLeft").objectReferenceValue = wingL;
            so.FindProperty("wingRight").objectReferenceValue = wingR;
            so.FindProperty("subjectLight").objectReferenceValue = pLight;
            so.FindProperty("subjectCollider").objectReferenceValue = box;
            so.FindProperty("emergeParticles").objectReferenceValue = ps;
            so.FindProperty("ui").objectReferenceValue = ui;

            var rp = so.FindProperty("subjectRenderers");
            rp.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; i++)
                rp.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];

            so.FindProperty("paintedLocalPos").vector3Value = PaintedLocal;
            so.ApplyModifiedPropertiesWithoutUndo();

            return exp;
        }

        static Transform Wing(Transform parent, string name, float side, Material skin, Material dark, List<Renderer> sink)
        {
            // Pivot at the shoulder so a single Z rotation flaps the whole wing.
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = new Vector3(side * 0.055f, 0.030f, 0.020f);

            Part(pivot.transform, "Inner", PrimitiveType.Cube, new Vector3(side * 0.150f, 0f, 0f),
                 new Vector3(0.300f, 0.016f, 0.150f), skin, sink);
            Part(pivot.transform, "Outer", PrimitiveType.Cube, new Vector3(side * 0.330f, 0.012f, -0.030f),
                 new Vector3(0.230f, 0.013f, 0.100f), skin, sink);
            Part(pivot.transform, "Edge", PrimitiveType.Cube, new Vector3(side * 0.260f, 0.009f, -0.082f),
                 new Vector3(0.420f, 0.011f, 0.022f), dark, sink);
            return pivot.transform;
        }

        internal static void Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale,
                         Material mat, List<Renderer> sink)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);     // one box on the root is the tap target
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            sink.Add(r);
        }

        internal static void ConfigureParticles(ParticleSystem ps, Material mat)
        {
            var main = ps.main;
            main.duration = 2.0f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.10f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.028f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.86f, 0.62f), new Color(0.93f, 0.56f, 0.20f));
            main.maxParticles = 220;
            main.gravityModifier = -0.04f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.00f, 70),
                new ParticleSystem.Burst(0.35f, 45),
                new ParticleSystem.Burst(0.80f, 30),
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.22f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = mat;
            psr.shadowCastingMode = ShadowCastingMode.Off;
        }

        internal static void BindAudio(Transform parent, string slot, string clipPath, bool loop, float spatial)
        {
            var go = new GameObject(slot);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = spatial;
            src.volume = loop ? 0.45f : 0.85f;
            src.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (src.clip == null) Debug.LogWarning("[AR Murals] Missing placeholder clip: " + clipPath);
        }

        internal static GameObject Quad(string name, Transform parent, float w, float h, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(w, h, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            return go;
        }

        // ------------------------------------------------------------------ materials

        internal static Material TexturedMaterial(string name, string texPath, bool transparent, Color tint, string folder = null)
        {
            var m = MakeMaterial(name, "Universal Render Pipeline/Unlit", transparent, tint, folder);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        internal static Material ColorMaterial(string name, Color c, bool transparent, string folder = null)
        {
            var m = MakeMaterial(name, "Universal Render Pipeline/Lit", transparent, c, folder);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.18f);
            EditorUtility.SetDirty(m);
            return m;
        }

        internal static Material MakeMaterial(string name, string shaderName, bool transparent, Color color, string folder = null)
        {
            var dir = string.IsNullOrEmpty(folder) ? MatFolder : folder;
            Directory.CreateDirectory(dir);
            var path = dir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }

            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_AlphaClip", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_ZWrite", 1f);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Geometry;
            }

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        internal static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ------------------------------------------------------------------ UI shell

        static readonly Color Ink = new Color(0.063f, 0.071f, 0.110f, 1f);
        static readonly Color Paper = new Color(0.949f, 0.945f, 0.925f, 1f);
        static readonly Color Accent = new Color(0.988f, 0.737f, 0.251f, 1f);

        internal static UIShellController BuildUIShell()
        {
            var old = GameObject.Find("UI_Shell");
            if (old != null) Object.DestroyImmediate(old);

            var canvasGO = new GameObject("UI_Shell", typeof(Canvas), typeof(CanvasScaler),
                                          typeof(GraphicRaycaster), typeof(UIShellController));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var shell = canvasGO.GetComponent<UIShellController>();

            // ---------- START
            var start = Panel(canvasGO.transform, "StartPanel", new Color(0.047f, 0.055f, 0.086f, 0.97f));
            Rule(start.transform, new Vector2(0, 470), new Vector2(120, 8), Accent);
            Label(start.transform, "Kicker", "ALU CAMPUS", 34, new Vector2(0, 400), new Vector2(900, 60),
                  new Color(0.65f, 0.67f, 0.74f, 1f));
            Label(start.transform, "Title", "MURALS,\nIN MOTION", 110, new Vector2(0, 230), new Vector2(950, 300), Paper);
            Label(start.transform, "Blurb",
                  "Point your phone at a mural on campus and watch the artwork step off the wall.",
                  40, new Vector2(0, -40), new Vector2(820, 220), new Color(0.78f, 0.79f, 0.84f, 1f));
            var begin = MakeButton(start.transform, "BeginButton", "START SCANNING", new Vector2(0, -330), null, Accent, Ink);
            Label(start.transform, "Credit", "M1  ·  EMERGENCE  ·  PLACEHOLDER BUILD", 28,
                  new Vector2(0, -760), new Vector2(900, 60), new Color(0.45f, 0.47f, 0.55f, 1f));

            // ---------- SCANNING
            var scan = Panel(canvasGO.transform, "ScanPanel", new Color(0f, 0f, 0f, 0.42f));
            var reticle = new GameObject("Reticle", typeof(RectTransform), typeof(CanvasGroup), typeof(UIPulse));
            reticle.transform.SetParent(scan.transform, false);
            var rrt = (RectTransform)reticle.transform;
            rrt.sizeDelta = new Vector2(760, 620);
            rrt.anchoredPosition = new Vector2(0, 90);
            var rp = new SerializedObject(reticle.GetComponent<UIPulse>());
            rp.FindProperty("fade").objectReferenceValue = reticle.GetComponent<CanvasGroup>();
            rp.ApplyModifiedPropertiesWithoutUndo();
            Bracket(reticle.transform, "TL", new Vector2(0, 1), new Vector2(1, 1));
            Bracket(reticle.transform, "TR", new Vector2(1, 1), new Vector2(-1, 1));
            Bracket(reticle.transform, "BL", new Vector2(0, 0), new Vector2(1, -1));
            Bracket(reticle.transform, "BR", new Vector2(1, 0), new Vector2(-1, -1));
            Label(scan.transform, "ScanHint", "FIND A MURAL", 48, new Vector2(0, -440), new Vector2(900, 80), Paper);
            Label(scan.transform, "ScanSub", "Hold steady and fill the frame with the artwork",
                  34, new Vector2(0, -520), new Vector2(880, 90), new Color(0.80f, 0.82f, 0.88f, 1f));

            // ---------- IN-AR CONTROLS
            var ar = Panel(canvasGO.transform, "ARControls", new Color(0, 0, 0, 0));
            var bar = Panel(ar.transform, "Bar", new Color(0.047f, 0.055f, 0.086f, 0.80f));
            var brt = (RectTransform)bar.transform;
            brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 0);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(0, 0); brt.offsetMax = new Vector2(0, 210);
            var infoBtn = MakeButton(bar.transform, "InfoButton", "INFO", new Vector2(-320, 0), new Vector2(280, 110));
            var resetBtn = MakeButton(bar.transform, "ResetButton", "REPLAY", new Vector2(0, 0), new Vector2(280, 110));
            var finishBtn = MakeButton(bar.transform, "FinishButton", "DONE", new Vector2(320, 0), new Vector2(280, 110));
            Label(ar.transform, "Nudge", "Tap the bird  ·  Drag to steer it", 30,
                  new Vector2(0, -660), new Vector2(900, 60), new Color(0.88f, 0.89f, 0.93f, 0.85f));

            // ---------- INFO
            var info = Panel(canvasGO.transform, "InfoPanel", new Color(0.047f, 0.055f, 0.086f, 0.90f));
            var card = Panel(info.transform, "Card", new Color(0.078f, 0.090f, 0.137f, 0.98f));
            var crt = (RectTransform)card.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(920, 920);
            crt.anchoredPosition = Vector2.zero;
            Rule(card.transform, new Vector2(0, 360), new Vector2(100, 8), Accent);
            Label(card.transform, "InfoTitle", "THE BIRD", 60, new Vector2(0, 280), new Vector2(820, 90), Paper);
            var infoBody = Label(card.transform, "InfoBody", "PLACEHOLDER", 38,
                                 new Vector2(0, 20), new Vector2(800, 460), new Color(0.82f, 0.84f, 0.89f, 1f));
            infoBody.alignment = TextAnchor.UpperLeft;
            var infoClose = MakeButton(card.transform, "InfoCloseButton", "CLOSE", new Vector2(0, -340), new Vector2(360, 110));

            // ---------- COMPLETION / EXIT
            var exit = Panel(canvasGO.transform, "ExitPanel", new Color(0.047f, 0.055f, 0.086f, 0.97f));
            Rule(exit.transform, new Vector2(0, 360), new Vector2(120, 8), Accent);
            Label(exit.transform, "ExitTitle", "THAT'S ONE MURAL", 72, new Vector2(0, 230), new Vector2(950, 200), Paper);
            Label(exit.transform, "ExitBlurb",
                  "Four more are waiting on campus. Head to the next wall, or run this one again.",
                  38, new Vector2(0, 20), new Vector2(820, 220), new Color(0.78f, 0.79f, 0.84f, 1f));
            var restart = MakeButton(exit.transform, "RestartButton", "BACK TO START", new Vector2(0, -260), null, Accent, Ink);

            var so = new SerializedObject(shell);
            so.FindProperty("startPanel").objectReferenceValue = start;
            so.FindProperty("scanPanel").objectReferenceValue = scan;
            so.FindProperty("arControls").objectReferenceValue = ar;
            so.FindProperty("infoPanel").objectReferenceValue = info;
            so.FindProperty("exitPanel").objectReferenceValue = exit;
            so.FindProperty("beginButton").objectReferenceValue = begin;
            so.FindProperty("infoButton").objectReferenceValue = infoBtn;
            so.FindProperty("infoCloseButton").objectReferenceValue = infoClose;
            so.FindProperty("resetButton").objectReferenceValue = resetBtn;
            so.FindProperty("finishButton").objectReferenceValue = finishBtn;
            so.FindProperty("restartButton").objectReferenceValue = restart;
            so.FindProperty("infoBody").objectReferenceValue = infoBody;
            so.ApplyModifiedPropertiesWithoutUndo();

            return shell;
        }

        static GameObject Panel(Transform parent, string name, Color bg)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = bg.a > 0.02f;
            return go;
        }

        static void Rule(Transform parent, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("Rule", typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = c; img.raycastTarget = false;
        }

        static void Bracket(Transform parent, string name, Vector2 anchor, Vector2 dir)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(120, 120);
            rt.anchoredPosition = Vector2.zero;

            Arm(rt, "H", new Vector2(dir.x > 0 ? 0 : 1, dir.y > 0 ? 1 : 0), new Vector2(120, 10));
            Arm(rt, "V", new Vector2(dir.x > 0 ? 0 : 1, dir.y > 0 ? 1 : 0), new Vector2(10, 120));
        }

        static void Arm(Transform parent, string name, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = pivot;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = Paper;
            img.raycastTarget = false;
        }

        static Text Label(Transform parent, string name, string text, int size, Vector2 pos, Vector2 box, Color? color = null)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;

            var t = go.GetComponent<Text>();
            t.font = DefaultFont();
            t.fontSize = size;
            t.lineSpacing = 1.05f;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color ?? Paper;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(Transform parent, string name, string label, Vector2 pos,
                                 Vector2? size = null, Color? fill = null, Color? ink = null)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size ?? new Vector2(620, 140);

            var img = go.GetComponent<Image>();
            img.color = fill ?? new Color(1f, 1f, 1f, 0.14f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.95f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.88f, 1f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var t = Label(go.transform, "Label", label, 40, Vector2.zero, rt.sizeDelta, ink ?? Paper);
            t.fontStyle = FontStyle.Bold;
            return btn;
        }

        internal static Font DefaultFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }
    }
}
