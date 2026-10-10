using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARMurals
{
    /// <summary>
    /// M3 - EXPANSION.
    ///
    /// The mural (Kigali's convention centre dome under a painted sky) becomes a doorway
    /// to a larger world. Everything digital is cut from the photograph of the mural
    /// itself, so what the viewer sees is the actual paint leaving the wall:
    ///
    ///   1. The wall dims, except the dome, the sculpture and the child, which stay lit.
    ///   2. The painted sky opens as a window over the wall.
    ///   3. The sky bends outward and wraps around the viewer, the dome, sculpture and
    ///      child lift off the wall to different depths, and the patterned border
    ///      unrolls across the floor toward the viewer, row by row.
    ///
    /// INTERACTION 1 - tap the dome: it comes forward toward the viewer and the
    ///                 information panel opens with the mural's story. Tap again to send it back.
    /// INTERACTION 2 - drag left/right: push the world wider around you, or fold it
    ///                 back toward the wall.
    ///
    /// Works in "wall space": X right, Y up, -Z out of the wall toward the viewer.
    /// </summary>
    public class M3ExpansionExperience : MuralExperienceBase
    {
        enum Stage { Untouched, Opening, Live, Inspecting }

        [Serializable]
        public class Layer
        {
            public Renderer renderer;
            [Tooltip("Where the cut-out sits exactly over its paint, in wall space.")]
            public Vector3 restPos;
            [Tooltip("How far it comes out of the wall when fully expanded, in metres.")]
            public float popDepth = 0.3f;
            public float popScale = 1.08f;
            [Tooltip("Seconds after the expansion starts before this layer moves.")]
            public float delay;
        }

        [Header("Wall")]
        [Tooltip("Rotates wall space onto the tracked image (AR Foundation images lie in their local XZ plane).")]
        [SerializeField] Transform wall;
        [Tooltip("Editor only: stand the wall upright this far in front of the camera, so F/L/R testing works without a tracked image.")]
        [SerializeField] float editorPreviewDistance = 4f;
        [Tooltip("The full mural photo, standing in for the real wall. Shown in the editor only.")]
        [SerializeField] GameObject editorWallPhoto;
        [SerializeField] Renderer dimmer;
        [SerializeField] Color dimTint = new Color(0.04f, 0.05f, 0.08f, 0.88f);
        [SerializeField] float dimDuration = 0.9f;

        [Header("Sky window")]
        [SerializeField] MeshFilter sky;
        [Tooltip("The painted wall the sky first covers, in wall space: x, y (bottom-left), width, height.")]
        [SerializeField] Rect paintedRect = new Rect(-2f, -1.5f, 4f, 3f);
        [SerializeField] float floorY = -1.6f;
        [SerializeField] float skyRadius = 2.6f;
        [SerializeField] float skyArc = 220f;
        [SerializeField] float skyHeight = 3.2f;
        [SerializeField] float skyTiles = 3f;
        [SerializeField] float cloudDrift = 0.008f;
        [SerializeField] float openDuration = 1.0f;
        [SerializeField] float expandDuration = 2.6f;

        [Header("Layers cut from the mural")]
        [SerializeField] Layer[] layers = Array.Empty<Layer>();
        [Tooltip("Index into layers of the dome (the tap target).")]
        [SerializeField] int domeIndex;
        [SerializeField] Collider domeCollider;

        [Header("Border rows (element 0 is the painted strip itself)")]
        [SerializeField] Renderer[] borderRows = Array.Empty<Renderer>();
        [SerializeField] Vector3 borderRestPos;
        [SerializeField] float rowSpacing = 0.12f;

        [Header("Interactions")]
        [SerializeField] float minExpansion = 0.25f;
        [SerializeField] float dragSensitivity = 1.6f;
        [SerializeField] float inspectDepth = 0.95f;
        [SerializeField] float inspectScale = 1.25f;

        [Header("UI")]
        [SerializeField] UIShellController ui;
        [SerializeField, TextArea(4, 8)]
        string infoBody =
            "The dome is the Kigali Convention Centre, painted beside the names of the people " +
            "who shaped Africa's story. The mural opens the wall onto the city and the sky above it: " +
            "a reminder that what we build here reaches far beyond this campus.";

        // ---- runtime state
        Stage stage = Stage.Untouched;
        Coroutine sequence;
        bool hasOpened;
        float expansion;          // 0 = flat on the wall, 1 = wrapped around the viewer
        float expansionTarget = 1f;
        float inspect;            // 0 = dome in its place, 1 = dome right in front of the viewer
        float[] layerProgress = Array.Empty<float>();
        float borderProgress;
        Mesh skyMesh;
        Vector3[] skyVerts;
        Renderer skyRenderer;

        bool dragging;
        Vector2 lastPointer;
        float dragDistance;

        const int SkySegments = 48;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        MaterialPropertyBlock mpb;

        // ------------------------------------------------------------------ lifecycle

        protected override void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (editorWallPhoto != null) editorWallPhoto.SetActive(Application.isEditor);
            if (Application.isEditor && wall != null && editorPreviewDistance > 0f)
            {
                wall.localRotation = Quaternion.identity;
                wall.localPosition = new Vector3(0f, 0f, editorPreviewDistance);
            }
            BuildSkyMesh();
            layerProgress = new float[layers.Length];
            base.Awake();     // calls HandleReset
        }

        protected override void HandleReset()
        {
            if (sequence != null) { StopCoroutine(sequence); sequence = null; }

            stage = Stage.Untouched;
            hasOpened = false;
            dragging = false;
            expansion = 0f;
            expansionTarget = 1f;
            inspect = 0f;
            borderProgress = 0f;
            for (int i = 0; i < layerProgress.Length; i++) layerProgress[i] = 0f;

            // The wall looks untouched: no dimming, no sky, cut-outs exactly over their paint.
            SetAlpha(dimmer, dimTint, 0f);
            SetAlpha(skyRenderer, Color.white, 0f);
            for (int i = 0; i < borderRows.Length; i++) SetAlpha(borderRows[i], Color.white, i == 0 ? 1f : 0f);
            if (domeCollider != null) domeCollider.enabled = false;
            ApplyPose();

            if (contentRoot != null) contentRoot.gameObject.SetActive(true);
            if (ui != null) ui.SetInfoText(infoBody);
        }

        protected override void HandleTrackingFound()
        {
            if (contentRoot != null) contentRoot.gameObject.SetActive(true);

            // Resume where it was: a momentary wobble must not restart the experience.
            if (hasOpened) { stage = Stage.Live; return; }

            if (sequence != null) StopCoroutine(sequence);
            sequence = StartCoroutine(OpenSequence());
        }

        protected override void HandleTrackingLost()
        {
            if (contentRoot != null) contentRoot.gameObject.SetActive(false);
            dragging = false;
            if (stage == Stage.Inspecting) stage = Stage.Live;
            inspect = 0f;
            if (ui != null && ui.State == UIShellController.ShellState.Info) ui.ToggleInfo();
        }

        // ------------------------------------------------------------------ the sequence

        IEnumerator OpenSequence()
        {
            stage = Stage.Opening;

            // 1. THE WALL DIMS. Only the dome, sculpture and child stay lit, so the eye
            //    goes to what is about to move.
            for (float t = 0f; t < dimDuration; t += Time.deltaTime)
            {
                SetAlpha(dimmer, dimTint, dimTint.a * Ease(t / dimDuration));
                yield return null;
            }
            SetAlpha(dimmer, dimTint, dimTint.a);

            // 2. THE SKY OPENS as a window over the wall, still flat.
            for (float t = 0f; t < openDuration; t += Time.deltaTime)
            {
                SetAlpha(skyRenderer, Color.white, Ease(t / openDuration));
                yield return null;
            }
            SetAlpha(skyRenderer, Color.white, 1f);
            for (int i = 1; i < borderRows.Length; i++) SetAlpha(borderRows[i], Color.white, 1f);

            // 3. IT EXPANDS. The sky wraps round the viewer, the cut-outs lift off at their
            //    own depths and moments, and the border unrolls across the floor.
            float longest = expandDuration;
            foreach (var l in layers) longest = Mathf.Max(longest, l.delay + expandDuration);
            for (float t = 0f; t < longest; t += Time.deltaTime)
            {
                expansion = Ease(t / expandDuration);
                for (int i = 0; i < layers.Length; i++)
                    layerProgress[i] = Ease((t - layers[i].delay) / expandDuration);
                borderProgress = Mathf.Clamp01(t / expandDuration);
                ApplyPose();
                yield return null;
            }
            expansion = 1f;
            borderProgress = 1f;
            for (int i = 0; i < layerProgress.Length; i++) layerProgress[i] = 1f;
            ApplyPose();

            if (domeCollider != null) domeCollider.enabled = true;
            hasOpened = true;
            stage = Stage.Live;
            sequence = null;
            if (ui != null) ui.ShowExperience();
        }

        // ------------------------------------------------------------------ per-frame

        protected override void Update()
        {
            base.Update();                 // debug keys F / L / R
            if (!IsActive || !hasOpened) return;

            ReadPointer();

            expansion = Mathf.Lerp(expansion, expansionTarget, Time.deltaTime * 3f);
            inspect = Mathf.Lerp(inspect, stage == Stage.Inspecting ? 1f : 0f, Time.deltaTime * 3f);

            // Clouds drift across the wrapped sky.
            if (skyRenderer != null)
            {
                skyRenderer.GetPropertyBlock(mpb);
                mpb.SetVector(BaseMapStId, new Vector4(skyTiles, 1f, Time.time * cloudDrift, 0f));
                skyRenderer.SetPropertyBlock(mpb);
            }
            ApplyPose();
        }

        /// <summary>Place everything for the current expansion, layer and border progress.</summary>
        void ApplyPose()
        {
            UpdateSkyMesh(expansion);

            float breathe = hasOpened ? Mathf.Sin(Time.time * 1.3f) : 0f;
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                if (l.renderer == null) continue;
                // Pop depth scales with the expansion, so dragging the world back folds them in too.
                float k = layerProgress.Length > i ? layerProgress[i] : 0f;
                float depth = l.popDepth * k * Mathf.Lerp(0.3f, 1f, expansion) + 0.012f * breathe * k * (i + 1);
                var pos = l.restPos + new Vector3(0f, 0f, -depth);
                float scale = Mathf.Lerp(1f, l.popScale, k);

                if (i == domeIndex && inspect > 0.001f)
                {
                    var inspectPos = new Vector3(0f, l.restPos.y * 0.5f, -inspectDepth);
                    pos = Vector3.Lerp(pos, inspectPos, Ease(inspect));
                    scale = Mathf.Lerp(scale, inspectScale, Ease(inspect));
                }

                var t = l.renderer.transform;
                t.localPosition = pos;
                var baseScale = BaseScale(t);
                t.localScale = new Vector3(baseScale.x * scale, baseScale.y * scale, 1f);
            }

            // Border: row 0 is the painted strip, hinging down onto the floor; the copies
            // follow it out one after another, like a carpet rolling toward the viewer.
            for (int i = 0; i < borderRows.Length; i++)
            {
                var r = borderRows[i];
                if (r == null) continue;
                float k = Ease(borderProgress * borderRows.Length - i * 0.6f);
                var floorPos = new Vector3(borderRestPos.x, floorY + 0.003f, -(i + 0.5f) * rowSpacing - 0.04f);
                var arc = new Vector3(0f, Mathf.Sin(k * Mathf.PI) * 0.06f, 0f);
                r.transform.localPosition = Vector3.Lerp(borderRestPos, floorPos, k) + arc;
                r.transform.localRotation = Quaternion.Euler(90f * k, 0f, 0f);
            }
        }

        // Quads keep their authored size in the x/y scale; remember it once.
        readonly System.Collections.Generic.Dictionary<Transform, Vector2> baseScales =
            new System.Collections.Generic.Dictionary<Transform, Vector2>();

        Vector2 BaseScale(Transform t)
        {
            if (!baseScales.TryGetValue(t, out var s))
            {
                s = new Vector2(t.localScale.x, t.localScale.y);
                baseScales[t] = s;
            }
            return s;
        }

        // ------------------------------------------------------------------ sky mesh

        void BuildSkyMesh()
        {
            if (sky == null) return;
            skyRenderer = sky.GetComponent<Renderer>();
            skyMesh = new Mesh { name = "M3_SkyWindow" };
            skyMesh.MarkDynamic();

            int cols = SkySegments + 1;
            skyVerts = new Vector3[cols * 2];
            var uvs = new Vector2[cols * 2];
            var tris = new int[SkySegments * 6];
            for (int c = 0; c < cols; c++)
            {
                float u = c / (float)SkySegments;
                uvs[c] = new Vector2(u, 0f);
                uvs[c + cols] = new Vector2(u, 1f);
            }
            for (int c = 0; c < SkySegments; c++)
            {
                int i = c * 6;
                tris[i] = c; tris[i + 1] = c + cols; tris[i + 2] = c + 1;
                tris[i + 3] = c + 1; tris[i + 4] = c + cols; tris[i + 5] = c + cols + 1;
            }
            skyMesh.vertices = skyVerts;
            skyMesh.uv = uvs;
            skyMesh.triangles = tris;
            sky.sharedMesh = skyMesh;
            UpdateSkyMesh(0f);
        }

        /// <summary>
        /// e = 0: a flat window exactly over the painted wall. e = 1: a tall curved panorama
        /// whose middle sits just behind the wall and whose sides wrap round toward the viewer.
        /// </summary>
        void UpdateSkyMesh(float e)
        {
            if (skyMesh == null) return;
            int cols = SkySegments + 1;
            float centreZ = -skyRadius + 0.25f;
            float arc = skyArc * Mathf.Deg2Rad;

            for (int c = 0; c < cols; c++)
            {
                float u = c / (float)SkySegments - 0.5f;

                var flatBottom = new Vector3(paintedRect.center.x + u * paintedRect.width, paintedRect.yMin, -0.003f);
                var flatTop = new Vector3(flatBottom.x, paintedRect.yMax, -0.003f);

                float a = u * arc;
                var ring = new Vector3(Mathf.Sin(a) * skyRadius, 0f, centreZ + Mathf.Cos(a) * skyRadius);
                var wideBottom = new Vector3(ring.x, floorY, ring.z);
                var wideTop = new Vector3(ring.x, floorY + skyHeight, ring.z);

                skyVerts[c] = Vector3.Lerp(flatBottom, wideBottom, e);
                skyVerts[c + cols] = Vector3.Lerp(flatTop, wideTop, e);
            }
            skyMesh.vertices = skyVerts;
            skyMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ interactions

        void ReadPointer()
        {
            var pointer = Pointer.current;
            if (pointer == null) return;
            var pos = pointer.position.ReadValue();

            if (pointer.press.wasPressedThisFrame)
            {
                lastPointer = pos;
                dragDistance = 0f;
                dragging = true;
            }
            else if (pointer.press.isPressed && dragging)
            {
                float dx = pos.x - lastPointer.x;
                dragDistance += Mathf.Abs(dx);

                // INTERACTION 2: push the world wider (right) or fold it back to the wall (left).
                if (stage == Stage.Live && Mathf.Abs(dx) > 0.5f)
                    expansionTarget = Mathf.Clamp(expansionTarget + dx / Mathf.Max(Screen.width, 1) * dragSensitivity,
                                                  minExpansion, 1f);
                lastPointer = pos;
            }
            else if (pointer.press.wasReleasedThisFrame && dragging)
            {
                dragging = false;

                // INTERACTION 1: a tap (not a drag) on the dome.
                bool isTap = dragDistance < Screen.width * 0.02f;
                if (isTap && HitDome(pos)) ToggleInspect();
            }
        }

        void ToggleInspect()
        {
            PlayConfirm();
            if (stage == Stage.Inspecting)
            {
                stage = Stage.Live;
                if (ui != null && ui.State == UIShellController.ShellState.Info) ui.ToggleInfo();
            }
            else
            {
                stage = Stage.Inspecting;
                if (ui != null) { ui.SetInfoText(infoBody); ui.ToggleInfo(); }
            }
        }

        bool HitDome(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null || domeCollider == null) return false;
            return domeCollider.Raycast(cam.ScreenPointToRay(screenPos), out _, 60f);
        }

        // ------------------------------------------------------------------ helpers

        static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        void SetAlpha(Renderer r, Color c, float a)
        {
            if (r == null) return;
            c.a = a;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}
