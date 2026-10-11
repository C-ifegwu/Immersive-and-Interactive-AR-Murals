using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARMurals
{
    /// <summary>
    /// Shared plumbing for murals built from layers cut out of the mural's own photograph.
    /// Handles the wall-space rotation, the editor stand-in photo, dimming the painted wall
    /// before anything digital appears, tap/drag input, the info panel, and resuming after
    /// tracking loss. Each mural only writes its reveal, its live loop and its two interactions.
    ///
    /// Wall space: X right, Y up, -Z out of the wall toward the viewer.
    /// </summary>
    public abstract class LayeredMuralExperience : MuralExperienceBase
    {
        [Header("Wall")]
        [Tooltip("Rotates wall space onto the tracked image (AR Foundation images lie in their local XZ plane).")]
        [SerializeField] protected Transform wall;
        [Tooltip("Editor only: stand the wall upright this far in front of the camera, so F/L/R testing works.")]
        [SerializeField] float editorPreviewDistance = 4f;
        [Tooltip("The full mural photo, standing in for the real wall. Shown in the editor only.")]
        [SerializeField] GameObject editorWallPhoto;
        [SerializeField] Renderer dimmer;
        [SerializeField] Color dimTint = new Color(0.04f, 0.05f, 0.08f, 0.7f);
        [SerializeField] float dimDuration = 1f;

        [Header("UI")]
        [SerializeField] protected UIShellController ui;
        [SerializeField, TextArea(4, 8)] protected string infoBody = "";

        protected bool Opened { get; private set; }
        Coroutine sequence;
        bool dragging;
        Vector2 lastPointer;
        float dragDistance;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock mpb;

        Coroutine dissolve;
        readonly List<Renderer> fading = new List<Renderer>();
        readonly List<Color> fadeColors = new List<Color>();
        MaterialPropertyBlock fadeBlock;

        // ------------------------------------------------------------------ what each mural writes

        /// <summary>Put every layer back exactly over its paint, nothing moving.</summary>
        protected abstract void ResetLayers();
        /// <summary>The transformation, after the wall has dimmed.</summary>
        protected abstract IEnumerator Reveal();
        /// <summary>Every frame once revealed.</summary>
        protected abstract void Live();
        /// <summary>A tap (not a drag). Return true if it hit something of this mural.</summary>
        protected abstract bool Tap(Ray ray);
        /// <summary>Horizontal drag, as a fraction of the screen width this frame.</summary>
        protected abstract void Drag(float dx);

        // ------------------------------------------------------------------ lifecycle

        protected override void Awake()
        {
            mpb ??= new MaterialPropertyBlock();
            if (editorWallPhoto != null) editorWallPhoto.SetActive(Application.isEditor);
            if (Application.isEditor && wall != null && editorPreviewDistance > 0f)
            {
                wall.localRotation = Quaternion.identity;
                wall.localPosition = new Vector3(0f, 0f, editorPreviewDistance);
            }
            base.Awake();     // calls HandleReset
        }

        protected override void HandleReset()
        {
            StopDissolve();
            if (sequence != null) { StopCoroutine(sequence); sequence = null; }
            Opened = false;
            dragging = false;
            SetColor(dimmer, dimTint, 0f);
            ResetLayers();
            if (contentRoot != null) contentRoot.gameObject.SetActive(true);
            if (ui != null) ui.SetInfoText(infoBody);
        }

        protected override void HandleTrackingFound()
        {
            StopDissolve();
            if (contentRoot != null) contentRoot.gameObject.SetActive(true);
            if (ui != null) ui.SetInfoText(infoBody);
            if (Opened) return;                       // resume where it was
            if (sequence != null) StopCoroutine(sequence);
            sequence = StartCoroutine(Open());
        }

        protected override void HandleTrackingLost()
        {
            StopDissolve();
            if (contentRoot != null && contentRoot.gameObject.activeInHierarchy) dissolve = StartCoroutine(Dissolve());
            else if (contentRoot != null) contentRoot.gameObject.SetActive(false);
            dragging = false;
            if (ui != null && ui.State == UIShellController.ShellState.Info) ui.ToggleInfo();
        }

        IEnumerator Dissolve()
        {
            fadeBlock ??= new MaterialPropertyBlock();
            fading.Clear();
            fadeColors.Clear();
            foreach (var r in contentRoot.GetComponentsInChildren<Renderer>())
            {
                r.GetPropertyBlock(fadeBlock);
                var c = fadeBlock.HasColor(BaseColorId) ? fadeBlock.GetColor(BaseColorId)
                      : r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId) ? r.sharedMaterial.GetColor(BaseColorId)
                      : Color.white;
                fading.Add(r);
                fadeColors.Add(c);
            }

            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                FadeTo(1f - Ease(t / 0.4f));
                yield return null;
            }
            contentRoot.gameObject.SetActive(false);
            FadeTo(1f);
            fading.Clear();
            dissolve = null;
        }

        void FadeTo(float k)
        {
            for (int i = 0; i < fading.Count; i++)
            {
                if (fading[i] == null) continue;
                var c = fadeColors[i];
                c.a *= k;
                fading[i].GetPropertyBlock(fadeBlock);
                fadeBlock.SetColor(BaseColorId, c);
                fadeBlock.SetColor(ColorId, c);
                fading[i].SetPropertyBlock(fadeBlock);
            }
        }

        void StopDissolve()
        {
            if (dissolve == null) return;
            StopCoroutine(dissolve);
            dissolve = null;
            FadeTo(1f);
            fading.Clear();
        }

        IEnumerator Open()
        {
            // The painted original dims first, so what follows reads as coming out of the paint.
            for (float t = 0f; t < dimDuration; t += Time.deltaTime)
            {
                SetColor(dimmer, dimTint, dimTint.a * Ease(t / dimDuration));
                yield return null;
            }
            SetColor(dimmer, dimTint, dimTint.a);

            yield return Reveal();

            Opened = true;
            sequence = null;
            if (ui != null) ui.ShowExperience();
        }

        protected override void Update()
        {
            base.Update();                 // debug keys F / L / R
            if (!IsActive || !Opened) return;
            ReadPointer();
            Live();
        }

        // ------------------------------------------------------------------ input

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
                if (Mathf.Abs(dx) > 0.5f) Drag(dx / Mathf.Max(Screen.width, 1));
                lastPointer = pos;
            }
            else if (pointer.press.wasReleasedThisFrame && dragging)
            {
                dragging = false;
                var cam = Camera.main;
                if (dragDistance < Screen.width * 0.02f && cam != null && Tap(cam.ScreenPointToRay(pos)))
                {
                    PlayConfirm();
                    Buzz();
                }
            }
        }

        /// <summary>Open the info panel with this mural's story (or close it if open).</summary>
        protected void ToggleInfo()
        {
            if (ui == null) return;
            ui.SetInfoText(infoBody);
            ui.ToggleInfo();
        }

        // ------------------------------------------------------------------ helpers

        protected static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        protected static bool Hit(Collider c, Ray ray) => c != null && c.enabled && c.Raycast(ray, out _, 60f);

        /// <summary>Tint and fade a renderer without touching its shared material.</summary>
        protected void SetColor(Renderer r, Color c, float alpha)
        {
            if (r == null) return;
            mpb ??= new MaterialPropertyBlock();   // the manager may reset us before our Awake
            c.a = alpha;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>The viewer's camera position in wall space (null in edit mode).</summary>
        protected Vector3? ViewerInWall()
        {
            var cam = Camera.main;
            if (cam == null || wall == null) return null;
            return wall.InverseTransformPoint(cam.transform.position);
        }
    }
}
