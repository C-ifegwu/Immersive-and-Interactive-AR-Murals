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
    /// the emerging mask rig, particles, audio and the full five-screen UI flow.
    ///
    /// Re-runnable. It always rebuilds from the AR Mobile template's SampleScene, so
    /// Unity's own AR rig is never hand-assembled and nothing drifts.
    /// </summary>
    public static class MuralSceneBuilder
    {
        const string TemplateScene = "Assets/Scenes/SampleScene.unity";
        const string Root = "Assets/Murals/M1_Chibueze";
        const string OutScene = Root + "/Scenes/M1_Emergence.unity";
        const string PrefabPath = Root + "/Prefabs/Mural_M1_Root.prefab";
        const string LibraryPath = Root + "/M1_ReferenceImages.asset";
        const string MuralTex = Root + "/Textures/M1_target.jpg";
        const string OverlayTex = Root + "/Textures/M1_painted_region.png";
        const string MatFolder = Root + "/Materials";

        // Real-world mural size in metres and where the cubist mask sits on it.
        // MuralW is ESTIMATED from the brick courses beside the mural (+/- 8%) - change this one
        // number and everything else rescales with it.
        const float MuralW = 4.60f;
        const float MuralH = 2.950f;
        static readonly Vector3 PaintedLocal = new Vector3(-0.9782f, -0.0281f, 0.012f);
        static readonly Vector2 OverlaySize = new Vector2(2.5063f, 2.8313f);

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
            AssetDatabase.Refresh();
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

            MuralExperienceBase mural;
            try
            {
                mural = BuildMuralRoot(ui);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[AR Murals] Building the mural root failed - the scene is incomplete. " +
                               "The exception below names the line.");
                Debug.LogException(e);
                return;
            }

            var manager = origin.GetComponent<ARMuralManager>() ?? origin.gameObject.AddComponent<ARMuralManager>();
            var mo = new SerializedObject(manager);
            var list = mo.FindProperty("experiences");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = mural;
            mo.FindProperty("ui").objectReferenceValue = ui;
            mo.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAssetAndConnect(mural.gameObject, PrefabPath, InteractionMode.AutomatedAction);

            // --- reference image library, assigned last so nothing downstream can clear it
            if (library != null)
            {
                var im2 = origin.GetComponent<ARTrackedImageManager>();
                var so2 = new SerializedObject(im2);
                so2.FindProperty("m_SerializedLibrary").objectReferenceValue = library;
                so2.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(im2);

                so2.Update();
                var got = so2.FindProperty("m_SerializedLibrary").objectReferenceValue;
                Debug.Log(got != null
                    ? "[AR Murals] Reference image library attached: " + got.name
                    : "[AR Murals] WARNING - the reference image library did not attach. " +
                      "Run AR Murals > Check My Scene, or drag M1_ReferenceImages onto the " +
                      "AR Tracked Image Manager on XR Origin.");
            }
            else
            {
                Debug.LogWarning("[AR Murals] No reference image library was created - check " + MuralTex);
            }

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
                      "Tap the mask to open it and reveal the figures. Drag left/right to turn it.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(OutScene));
        }

        // ------------------------------------------------------------------ assets

        static void PrepareTextureImports()
        {
            Import(MuralTex, false);
            Import(OverlayTex, true);
        }

        /// <summary>
        /// Loads a texture, surviving the fact that SaveAndReimport invalidates the asset
        /// handle so the next LoadAssetAtPath on the same path comes back null.
        /// </summary>
        internal static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;

            // The main asset can read back null right after a reimport. Ask for every
            // object at the path instead of just the main one.
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Texture2D t) return t;

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;

            AssetDatabase.Refresh();
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;

            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Texture2D t2) return t2;

            Debug.LogWarning("[AR Murals] Could not load a Texture2D at " + path);
            return null;
        }

        internal static void Import(string path, bool alpha)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti) return;

            bool changed = false;
            if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; changed = true; }
            if (!ti.isReadable) { ti.isReadable = true; changed = true; }      // the image library reads the pixels
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
            if (ti.maxTextureSize < 2048) { ti.maxTextureSize = 2048; changed = true; }
            if (ti.alphaIsTransparency != alpha) { ti.alphaIsTransparency = alpha; changed = true; }

            var wantAlpha = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            if (ti.alphaSource != wantAlpha) { ti.alphaSource = wantAlpha; changed = true; }

            if (changed) ti.SaveAndReimport();
        }

        static XRReferenceImageLibrary BuildReferenceLibrary()
        {
            var tex = LoadTexture(MuralTex);
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
            cam.nearClipPlane = 0.05f;      // the mask comes within half a metre
            cam.farClipPlane = 25f;
        }

        // ------------------------------------------------------------------ mural + mask

        struct Piece
        {
            public string Name; public float U, V, W, H;
            public Piece(string n, float u, float v, float w, float h) { Name = n; U = u; V = v; W = w; H = h; }
        }

        // Shards of the painted mask and the five painted figures, cut from the mural
        // photograph itself. U/V are the centre and W/H the size, as fractions of the mural.
        static readonly Piece[] Shards =
        {
            new Piece("M1_shard_00", 0.10564f, 0.12659f, 0.18139f, 0.19386f),
            new Piece("M1_shard_01", 0.29110f, 0.13083f, 0.19226f, 0.20233f),
            new Piece("M1_shard_02", 0.47011f, 0.13559f, 0.17935f, 0.21186f),
            new Piece("M1_shard_03", 0.09307f, 0.32044f, 0.15625f, 0.24682f),
            new Piece("M1_shard_04", 0.26325f, 0.29025f, 0.18818f, 0.23517f),
            new Piece("M1_shard_05", 0.45414f, 0.30720f, 0.21128f, 0.26695f),
            new Piece("M1_shard_06", 0.10394f, 0.51536f, 0.17799f, 0.21928f),
            new Piece("M1_shard_07", 0.27548f, 0.51430f, 0.20584f, 0.23835f),
            new Piece("M1_shard_08", 0.46467f, 0.50689f, 0.19022f, 0.24682f),
            new Piece("M1_shard_09", 0.11583f, 0.70710f, 0.20177f, 0.21081f),
            new Piece("M1_shard_10", 0.30740f, 0.67903f, 0.19361f, 0.30508f),
            new Piece("M1_shard_11", 0.47792f, 0.79343f, 0.16372f, 0.39195f),
            new Piece("M1_shard_12", 0.10190f, 0.87023f, 0.17391f, 0.23835f),
            new Piece("M1_shard_13", 0.31793f, 0.87606f, 0.29484f, 0.22669f),
        };

        static readonly Piece[] Figures =
        {
            new Piece("M1_fig_queen",     0.60632f, 0.20922f, 0.13927f, 0.30191f),
            new Piece("M1_fig_scholar",   0.79654f, 0.28072f, 0.15965f, 0.29661f),
            new Piece("M1_fig_beret",     0.75068f, 0.51377f, 0.12228f, 0.21186f),
            new Piece("M1_fig_statesman", 0.88655f, 0.49258f, 0.10870f, 0.21186f),
            new Piece("M1_fig_elder",     0.81182f, 0.81568f, 0.14946f, 0.28602f),
        };

        const string ShardTex = Root + "/Textures/Shards/";

        /// <summary>Mural-local metres from a normalised position on the mural.</summary>
        static Vector3 Local(float u, float v, float z) =>
            new Vector3((u - 0.5f) * MuralW, (0.5f - v) * MuralH, z);

        static MuralExperienceBase BuildMuralRoot(UIShellController ui)
        {
            var old = GameObject.Find("Mural_M1_Root");
            if (old != null) Object.DestroyImmediate(old);

            var mats = Root + "/Materials";
            var root = new GameObject("Mural_M1_Root");

            // Editor-testing position only. With no real image target the content would
            // otherwise spawn at world origin, inside the camera. On device the manager
            // reparents this to the tracked image and zeroes the local transform, so this
            // offset never reaches the phone.
            root.transform.position = new Vector3(0f, 0f, 5f);

            var content = new GameObject("Content");
            content.transform.SetParent(root.transform, false);

            // --- the mural surface (placeholder; the physical wall replaces it on site)
            var muralMat = TexturedMaterial("M1_Mural", MuralTex, false, Color.white, mats);
            var plane = Quad("MuralPlane", content.transform, MuralW, MuralH, muralMat);

            // --- the painted mask, as a photographic patch that will dim away
            var overlayMat = TexturedMaterial("M1_PaintedOverlay", OverlayTex, true, Color.white, mats);
            var overlay = Quad("PaintedOverlay", content.transform, OverlaySize.x, OverlaySize.y, overlayMat);
            overlay.transform.localPosition = new Vector3(PaintedLocal.x, PaintedLocal.y, 0.004f);

            // --- the mask: one quad per painted shard, each starting exactly where it is painted
            var maskRoot = new GameObject("MaskRoot");
            maskRoot.transform.SetParent(content.transform, false);
            // Pivot sits at the centre of the PAINTED mask, so the assembled mask hovers in
            // front of where it was painted and turns about its own axis, not the mural's.
            maskRoot.transform.localPosition = new Vector3(PaintedLocal.x, PaintedLocal.y, 0f);
            var pivot = maskRoot.transform.localPosition;

            var shardTs = new List<Transform>();
            var shardRs = new List<Renderer>();
            foreach (var p in Shards)
            {
                var texPath = ShardTex + p.Name + ".png";
                if (LoadTexture(texPath) == null)
                {
                    Debug.LogWarning("[AR Murals] Missing shard texture: " + texPath + " - skipped.");
                    continue;
                }
                var mat = TexturedMaterial(p.Name, texPath, true, Color.white, mats);
                var q = Quad(p.Name, maskRoot.transform, p.W * MuralW, p.H * MuralH, mat);
                q.transform.localPosition = Local(p.U, p.V, 0.010f) - pivot;
                shardTs.Add(q.transform);
                shardRs.Add(q.GetComponent<MeshRenderer>());
            }

            // One collider for the whole mask: a reliable tap target on a phone.
            var maskCol = maskRoot.AddComponent<BoxCollider>();
            maskCol.center = Vector3.zero;
            maskCol.size = new Vector3(OverlaySize.x * 0.95f, OverlaySize.y * 0.95f, 0.9f);
            maskCol.enabled = false;

            var lightGO = new GameObject("MaskLight");
            lightGO.transform.SetParent(maskRoot.transform, false);
            lightGO.transform.localPosition = new Vector3(0f, 0f, 0.35f);
            var maskLight = lightGO.AddComponent<Light>();
            maskLight.type = LightType.Point;
            maskLight.color = new Color(1f, 0.88f, 0.66f);
            maskLight.range = 3.2f;
            maskLight.intensity = 0f;
            maskLight.enabled = false;

            // --- the five painted figures, cut from the mural's right half
            var figRoot = new GameObject("FigureRoot");
            figRoot.transform.SetParent(maskRoot.transform, false);

            var figTs = new List<Transform>();
            var figRs = new List<Renderer>();
            var figCs = new List<Collider>();
            foreach (var p in Figures)
            {
                var figPath = ShardTex + p.Name + ".png";
                if (LoadTexture(figPath) == null)
                {
                    Debug.LogWarning("[AR Murals] Missing figure texture: " + figPath + " - skipped.");
                    continue;
                }
                var mat = TexturedMaterial(p.Name, figPath, true, Color.white, mats);
                var holder = new GameObject(p.Name);
                holder.transform.SetParent(figRoot.transform, false);

                var q = Quad("Art", holder.transform, p.W * MuralW * 1.6f, p.H * MuralH * 1.6f, mat);
                q.transform.localPosition = Vector3.zero;

                var col = holder.AddComponent<BoxCollider>();
                col.size = new Vector3(p.W * MuralW * 1.7f, p.H * MuralH * 1.7f, 0.25f);
                col.enabled = false;

                figTs.Add(holder.transform);
                figRs.Add(q.GetComponent<MeshRenderer>());
                figCs.Add(col);
            }

            // --- highlight quads sitting over each painted figure on the wall
            var hlRoot = new GameObject("WallHighlights");
            hlRoot.transform.SetParent(content.transform, false);
            var hlMat = ColorMaterial("M1_Highlight", new Color(1f, 0.92f, 0.70f, 0f), true, mats);
            var hls = new List<Renderer>();
            foreach (var p in Figures)
            {
                var q = Quad("HL_" + p.Name, hlRoot.transform, p.W * MuralW * 1.15f, p.H * MuralH * 1.15f, hlMat);
                q.transform.localPosition = Local(p.U, p.V, 0.006f);
                hls.Add(q.GetComponent<MeshRenderer>());
            }

            // --- transition particles, at the centre of the painted mask
            var pGO = new GameObject("EmergeParticles");
            pGO.transform.SetParent(content.transform, false);
            pGO.transform.localPosition = new Vector3(PaintedLocal.x, PaintedLocal.y, 0.05f);
            var ps = pGO.AddComponent<ParticleSystem>();
            ConfigureParticles(ps, ColorMaterial("M1_Spark", new Color(1f, 0.86f, 0.62f), true, mats));
            var psShape = ps.shape;
            psShape.radius = OverlaySize.x * 0.45f;

            // --- the four named audio slots
            BindAudio(root.transform, "SFX_Detect", Root + "/Audio/SFX_Detect.wav", false, 0f);
            BindAudio(root.transform, "SFX_Lost", Root + "/Audio/SFX_Lost.wav", false, 0f);
            BindAudio(root.transform, "SFX_Confirm", Root + "/Audio/SFX_Confirm.wav", false, 0f);
            BindAudio(root.transform, "Ambient_Bed", Root + "/Audio/Ambient_Bed.wav", true, 1f);

            // --- wire it up
            var exp = root.AddComponent<M1EmergenceExperience>();
            var so = new SerializedObject(exp);
            SetStr(so, "muralSlot", "M1");
            SetFloat(so, "muralWidthMeters", MuralW);
            SetRef(so, "contentRoot", content.transform);
            SetRef(so, "muralPlane", plane);
            SetRef(so, "paintedOverlay", overlay.GetComponent<MeshRenderer>());
            SetRef(so, "maskRoot", maskRoot.transform);
            SetRef(so, "maskCollider", maskCol);
            SetRef(so, "maskLight", maskLight);
            SetRef(so, "figureRoot", figRoot.transform);
            SetRef(so, "emergeParticles", ps);
            SetRef(so, "ui", ui);

            FillList(so, "shards", shardTs);
            FillList(so, "shardRenderers", shardRs);
            FillList(so, "figures", figTs);
            FillList(so, "figureRenderers", figRs);
            FillList(so, "figureColliders", figCs);
            FillList(so, "wallHighlights", hls);

            // Panel text for each painted figure, in the same order as the Figures table.
            // All five identifications confirmed with the ALU guest relations team.
            var figureText = new[]
            {
                "YAA ASANTEWAA\n\n" +
                "Queen mother of Ejisu in the Asante kingdom. In 1900 the British governor " +
                "demanded to sit on the Golden Stool, the soul of the Asante nation, and the " +
                "chiefs hesitated. She stood and told them that if the men of Asante would not " +
                "go forward, then the women would. She led the siege of the British fort at " +
                "Kumasi at around sixty years old, in the last major war the Asante fought " +
                "against colonial rule. She was exiled to the Seychelles and died there in 1921.",

                "MANSA MUSA\n\n" +
                "Ruler of the Mali Empire in the early fourteenth century. His pilgrimage to Mecca " +
                "in 1324 carried so much gold that its distribution is said to have unsettled " +
                "economies along the route for years. He returned to build Timbuktu into a centre " +
                "of scholarship. The mural paints him as medieval European maps first drew him: " +
                "crowned, enthroned, holding gold.",

                "THOMAS SANKARA\n\n" +
                "President of Burkina Faso from 1983 until his assassination in 1987. He renamed " +
                "the country Burkina Faso, land of upright people, and in four years pushed through " +
                "land redistribution, mass vaccination, and some of the continent's earliest " +
                "government action against forced marriage and for women in public office. " +
                "He was thirty-seven when he was killed.",

                "JULIUS NYERERE\n\n" +
                "First president of Tanzania, and the only one of his generation of African " +
                "leaders to hand over power voluntarily and walk away. Tanzanians called him " +
                "Mwalimu, the teacher. He argued that independence meant nothing without " +
                "economic independence, pushed literacy from a small minority to most of the " +
                "country, and gave shelter and backing to liberation movements fighting across " +
                "southern Africa while they had nowhere else to go.",

                "WINNIE MADIKIZELA-MANDELA\n\n" +
                "South African organiser who held the anti-apartheid movement together through " +
                "decades of banning orders, solitary detention and internal exile while Nelson " +
                "Mandela was imprisoned. Called the Mother of the Nation, revered and contested " +
                "in equal measure, and never quiet.",
            };

            var info = so.FindProperty("figureInfo");
            if (info != null)
            {
                info.arraySize = figureText.Length;
                for (int i = 0; i < figureText.Length; i++)
                    info.GetArrayElementAtIndex(i).stringValue = figureText[i];
            }

            var maskText = so.FindProperty("maskInfo");
            if (maskText != null)
                maskText.stringValue =
                    "THE MASK\n\n" +
                    "The left half of this mural is not a portrait. It is a form - a mask built from " +
                    "interlocking planes of ochre, blue and cream.\n\n" +
                    "In the masquerade traditions this comes out of, a mask is not a disguise and it " +
                    "is not a likeness. It is a vessel. Hanging on a wall it is only wood and paint. " +
                    "It becomes an ancestor at the moment somebody puts it on and dances - and the " +
                    "person inside is never named. That is the point of it.\n\n" +
                    "So this is not a monument to the five people painted beside it. They are its " +
                    "previous wearers.\n\n" +
                    "Open it. Meet all five. Then tap it one more time.";

            var wornText = so.FindProperty("wornInfo");
            if (wornText != null)
                wornText.stringValue =
                    "THE MASK IS EMPTY.\n\n" +
                    "A masquerade mask is not a portrait and not a disguise. It is a vessel. " +
                    "On a wall it is only paint. It becomes an ancestor while somebody is " +
                    "inside it, dancing - and the wearer is never named.\n\n" +
                    "Yaa Asantewaa. Mansa Musa. Thomas Sankara. Julius Nyerere. " +
                    "Winnie Madikizela-Mandela. Five people who wore this, and set it down.\n\n" +
                    "You are looking straight through it now. This wall is not a monument. " +
                    "It is a queue.";

            so.ApplyModifiedPropertiesWithoutUndo();

            figRoot.SetActive(false);
            return exp;
        }

        static void SetRef(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[AR Murals] No serialized field named '" + field + "'."); return; }
            p.objectReferenceValue = value;
        }

        static void SetStr(SerializedObject so, string field, string value)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[AR Murals] No serialized field named '" + field + "'."); return; }
            p.stringValue = value;
        }

        static void SetFloat(SerializedObject so, string field, float value)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[AR Murals] No serialized field named '" + field + "'."); return; }
            p.floatValue = value;
        }

        static void FillList<T>(SerializedObject so, string field, List<T> items) where T : Object
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[AR Murals] No serialized list named '" + field + "'."); return; }
            p.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
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
            var tex = LoadTexture(texPath);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            }
            else Debug.LogWarning("[AR Murals] Material '" + name + "' has no texture - could not load " + texPath);
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

        /// <summary>
        /// The mural scene must be index 0, or the APK launches the AR template's demo
        /// instead of the experience. The template scene is dropped entirely.
        /// </summary>
        internal static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(path, true) };
            foreach (var existing in EditorBuildSettings.scenes)
            {
                if (existing.path == path) continue;
                if (existing.path == "Assets/Scenes/SampleScene.unity") continue;
                scenes.Add(existing);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[AR Murals] Build settings: launch scene is now " + path);
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
                  "Point your phone at the mask on the wall and find out who has worn it.",
                  40, new Vector2(0, -40), new Vector2(820, 220), new Color(0.78f, 0.79f, 0.84f, 1f));
            var begin = MakeButton(start.transform, "BeginButton", "START SCANNING", new Vector2(0, -330), null, Accent, Ink);
            Label(start.transform, "Credit", "M1  ·  EMERGENCE  ·  ALU KIGALI", 28,
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
            Label(ar.transform, "Nudge", "Tap the mask  ·  Drag to turn it", 30,
                  new Vector2(0, -660), new Vector2(900, 60), new Color(0.88f, 0.89f, 0.93f, 0.85f));

            // ---------- INFO
            var info = Panel(canvasGO.transform, "InfoPanel", new Color(0.047f, 0.055f, 0.086f, 0.90f));
            var card = Panel(info.transform, "Card", new Color(0.078f, 0.090f, 0.137f, 0.98f));
            var crt = (RectTransform)card.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(920, 920);
            crt.anchoredPosition = Vector2.zero;
            Rule(card.transform, new Vector2(0, 360), new Vector2(100, 8), Accent);
            Label(card.transform, "InfoTitle", "THE MASK", 60, new Vector2(0, 280), new Vector2(820, 90), Paper);
            var infoBody = Label(card.transform, "InfoBody", "", 38,
                                 new Vector2(0, 20), new Vector2(800, 460), new Color(0.82f, 0.84f, 0.89f, 1f));
            infoBody.alignment = TextAnchor.UpperLeft;
            // Figure biographies and the closing text differ a lot in length; best-fit
            // keeps every one of them inside the card instead of spilling past CLOSE.
            infoBody.resizeTextForBestFit = true;
            infoBody.resizeTextMinSize = 20;
            infoBody.resizeTextMaxSize = 38;
            infoBody.verticalOverflow = VerticalWrapMode.Truncate;
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
