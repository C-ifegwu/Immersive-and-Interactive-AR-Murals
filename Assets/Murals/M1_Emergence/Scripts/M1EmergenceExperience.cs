using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARMurals
{
    /// <summary>
    /// M1 - EMERGENCE.
    ///
    /// The mural depicts a bird in flight. When the mural is detected, the painted bird
    /// dims out of the wall, a solid bird fades up in exactly that position at exactly
    /// that scale, gains depth, unfolds its wings, detaches from the wall and flies a
    /// figure-eight in the space in front of the artwork.
    ///
    /// The dimming of the painted original is what sells it: the viewer reads the paint
    /// as having left the wall, not a model as having appeared on top of it.
    ///
    /// INTERACTION 1 - tap the bird: it banks toward the viewer, hovers close, and the
    ///                 information panel opens with the mural's story. Tap again to release.
    /// INTERACTION 2 - drag left/right: steer the bird around its flight path; it banks
    ///                 into the turn and the flap rate follows how hard you push it.
    ///
    /// All meshes are primitives for now. The timing, the sequence, the interactions and
    /// the tracking behaviour are final; only the art is placeholder.
    /// </summary>
    public class M1EmergenceExperience : MuralExperienceBase
    {
        enum Stage { Untouched, Dimming, GainingDepth, Detaching, Flying, Inspecting }

        [Header("Placeholder scaffolding (untick for the real wall)")]
        [Tooltip("The quad showing the placeholder mural image. On the real wall the physical " +
                 "mural provides this, so it must be OFF in the final build.")]
        [SerializeField] GameObject placeholderMuralPlane;
        [SerializeField] bool showPlaceholderMural = true;

        [Header("Painted region (the bird as it is painted on the wall)")]
        [Tooltip("Quad carrying a photographic cut-out of the painted bird, aligned over it. " +
                 "Fading this to the dim tint is what makes the real paint appear to drain away.")]
        [SerializeField] Renderer paintedOverlay;
        [SerializeField] Color dimTint = new Color(0.16f, 0.15f, 0.20f, 0.92f);
        [SerializeField] float dimDuration = 0.9f;

        [Header("Subject")]
        [SerializeField] Transform subject;
        [SerializeField] Transform wingLeft;
        [SerializeField] Transform wingRight;
        [SerializeField] Renderer[] subjectRenderers;
        [SerializeField] Light subjectLight;
        [SerializeField] Collider subjectCollider;

        [Tooltip("Where the bird is painted, in mural-local metres. Mural centre is (0,0,0).")]
        [SerializeField] Vector3 paintedLocalPos = new Vector3(-0.40f, 0.169f, 0.012f);
        [SerializeField] Vector3 solidScale = Vector3.one;

        [Header("Emergence")]
        [Tooltip("Out of the wall, in mural-local space. Flip to (0,0,-1) if the bird emerges into the wall.")]
        [SerializeField] Vector3 wallNormal = new Vector3(0f, 0f, 1f);
        [SerializeField] float depthDuration = 1.2f;
        [SerializeField] float detachDuration = 1.5f;
        [SerializeField] ParticleSystem emergeParticles;

        [Header("Flight")]
        [SerializeField] float orbitWidth = 0.78f;
        [SerializeField] float orbitHeight = 0.26f;
        [SerializeField] float orbitDepth = 0.65f;
        [SerializeField] float orbitSpeed = 0.13f;
        [SerializeField] float flapSpeed = 6.0f;
        [SerializeField] float flapAmplitude = 38f;
        [SerializeField] float wingFoldedAngle = -72f;

        [Header("Inspect (interaction 1)")]
        [SerializeField] float inspectDistance = 0.42f;
        [SerializeField] float inspectHeight = 0.10f;
        [SerializeField] float inspectLerp = 2.6f;

        [Header("UI")]
        [SerializeField] UIShellController ui;
        [SerializeField, TextArea(4, 8)]
        string infoBody =
            "PLACEHOLDER STORY.\n\nReplace this with what the mural actually depicts and why it " +
            "is on this wall, from the guest relations team. Two or three sentences is right - " +
            "the viewer is holding a phone up, not reading an essay.";

        // ---- runtime state
        Stage stage = Stage.Untouched;
        Coroutine sequence;
        float phase;              // position around the flight path, 0..1
        float steerBoost;         // extra speed from the user dragging
        float wingSpread;         // 0 = folded into the wall, 1 = fully open
        float alpha;
        bool hasEmerged;
        bool dragging;
        Vector2 lastPointer;
        float dragDistance;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock mpb;
        readonly Dictionary<Renderer, Color> baseColors = new Dictionary<Renderer, Color>();
        Color overlayBaseColor = Color.white;

        // ------------------------------------------------------------------ lifecycle

        protected override void Awake()
        {
            mpb = new MaterialPropertyBlock();
            CacheColors();
            base.Awake();     // calls HandleReset
        }

        void CacheColors()
        {
            if (subjectRenderers != null)
                foreach (var r in subjectRenderers)
                {
                    if (r == null || r.sharedMaterial == null) continue;
                    baseColors[r] = ReadColor(r.sharedMaterial);
                }
            if (paintedOverlay != null && paintedOverlay.sharedMaterial != null)
                overlayBaseColor = ReadColor(paintedOverlay.sharedMaterial);
        }

        static Color ReadColor(Material m)
        {
            if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
            if (m.HasProperty(ColorId)) return m.GetColor(ColorId);
            return Color.white;
        }

        protected override void HandleReset()
        {
            if (sequence != null) { StopCoroutine(sequence); sequence = null; }

            stage = Stage.Untouched;
            hasEmerged = false;
            dragging = false;
            phase = 0f;
            steerBoost = 0f;
            wingSpread = 0f;

            if (placeholderMuralPlane != null)
                placeholderMuralPlane.SetActive(showPlaceholderMural);

            // The painted bird is fully present: the wall looks untouched.
            SetOverlay(overlayBaseColor, 1f);

            if (subject != null)
            {
                subject.localPosition = paintedLocalPos;
                subject.localRotation = Quaternion.identity;
                subject.localScale = new Vector3(solidScale.x, solidScale.y, 0.02f);  // flat: still paint
            }
            ApplyWings(0f, 0f);
            SetSubjectAlpha(0f);
            if (subjectLight != null) { subjectLight.enabled = false; subjectLight.intensity = 0f; }
            if (subjectCollider != null) subjectCollider.enabled = false;

            if (emergeParticles != null)
                emergeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (contentRoot != null) contentRoot.gameObject.SetActive(true);
            if (ui != null) ui.SetInfoText(infoBody);
        }

        protected override void HandleTrackingFound()
        {
            if (contentRoot != null) contentRoot.gameObject.SetActive(true);

            if (hasEmerged)
            {
                // Resume where it was. A momentary wobble must not restart the experience.
                stage = Stage.Flying;
                return;
            }

            if (sequence != null) StopCoroutine(sequence);
            sequence = StartCoroutine(EmergeSequence());
        }

        protected override void HandleTrackingLost()
        {
            // Hide rather than freeze content in mid-air. State is kept for the resume.
            if (contentRoot != null) contentRoot.gameObject.SetActive(false);
            dragging = false;
            if (ui != null && ui.State == UIShellController.ShellState.Info) ui.ToggleInfo();
        }

        // ------------------------------------------------------------------ the sequence

        IEnumerator EmergeSequence()
        {
            var normal = wallNormal.sqrMagnitude < 1e-4f ? Vector3.forward : wallNormal.normalized;

            // 1. THE PAINT LEAVES THE WALL.
            //    The photographic patch over the painted bird fades to the dim tint while the
            //    solid bird fades up in exactly the same place, at exactly the same size.
            stage = Stage.Dimming;
            for (float t = 0f; t < dimDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / dimDuration);
                SetOverlay(Color.Lerp(overlayBaseColor, dimTint, k), 1f);
                SetSubjectAlpha(k);
                yield return null;
            }
            SetOverlay(dimTint, 1f);
            SetSubjectAlpha(1f);

            if (emergeParticles != null) emergeParticles.Play();
            if (subjectLight != null) subjectLight.enabled = true;

            // 2. IT GAINS DEPTH. Flat paint thickens into a solid body and the wings unfold,
            //    still in the plane of the wall.
            var flat = new Vector3(solidScale.x, solidScale.y, 0.02f);
            stage = Stage.GainingDepth;
            for (float t = 0f; t < depthDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / depthDuration);
                if (subject != null) subject.localScale = Vector3.Lerp(flat, solidScale, k);
                wingSpread = k;
                ApplyWings(k, 0f);
                if (subjectLight != null) subjectLight.intensity = Mathf.Lerp(0f, 1.1f, k);
                yield return null;
            }
            if (subject != null) subject.localScale = solidScale;
            wingSpread = 1f;

            // 3. IT DETACHES. Out of the wall and up, first wingbeats, settling onto the path.
            stage = Stage.Detaching;
            var from = paintedLocalPos;
            var to = FlightPosition(0f);
            float flapClock = 0f;
            for (float t = 0f; t < detachDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / detachDuration);
                flapClock += Time.deltaTime * flapSpeed * Mathf.Lerp(0.35f, 1f, k);

                if (subject != null)
                {
                    subject.localPosition = Vector3.Lerp(from, to, k) + normal * (0.12f * Mathf.Sin(k * Mathf.PI));
                    var look = Quaternion.LookRotation(FlightPosition(0.02f) - FlightPosition(0f), Vector3.up);
                    subject.localRotation = Quaternion.Slerp(Quaternion.identity, look, k);
                }
                ApplyWings(1f, flapClock);
                yield return null;
            }

            if (subjectCollider != null) subjectCollider.enabled = true;
            hasEmerged = true;
            stage = Stage.Flying;
            sequence = null;
            if (ui != null) ui.ShowExperience();
        }

        // ------------------------------------------------------------------ per-frame

        protected override void Update()
        {
            base.Update();                 // debug keys F / L / R
            if (!IsActive || subject == null) return;

            ReadPointer();

            switch (stage)
            {
                case Stage.Flying: FlyUpdate(); break;
                case Stage.Inspecting: InspectUpdate(); break;
            }
        }

        void FlyUpdate()
        {
            float speed = orbitSpeed * (1f + steerBoost);
            phase = Mathf.Repeat(phase + speed * Time.deltaTime, 1f);
            steerBoost = Mathf.Lerp(steerBoost, 0f, Time.deltaTime * 1.8f);

            var here = FlightPosition(phase);
            var ahead = FlightPosition(phase + 0.015f);
            var dir = ahead - here;

            subject.localPosition = here;
            if (dir.sqrMagnitude > 1e-6f)
            {
                // Bank into the turn: the sharper the horizontal change, the harder the roll.
                float bank = Mathf.Clamp(-dir.x * 120f, -45f, 45f);
                var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
                subject.localRotation = Quaternion.Slerp(
                    subject.localRotation, look * Quaternion.Euler(0f, 0f, bank), Time.deltaTime * 6f);
            }

            ApplyWings(1f, Time.time * flapSpeed * (1f + steerBoost * 0.6f));
        }

        void InspectUpdate()
        {
            var normal = wallNormal.sqrMagnitude < 1e-4f ? Vector3.forward : wallNormal.normalized;
            var target = new Vector3(0f, paintedLocalPos.y + inspectHeight, 0f) + normal * inspectDistance;

            subject.localPosition = Vector3.Lerp(subject.localPosition, target, Time.deltaTime * inspectLerp);

            // Face the viewer, hovering, wings beating slowly to hold station.
            var toCamera = Camera.main != null
                ? transform.InverseTransformPoint(Camera.main.transform.position) - subject.localPosition
                : normal;
            if (toCamera.sqrMagnitude > 1e-6f)
                subject.localRotation = Quaternion.Slerp(
                    subject.localRotation, Quaternion.LookRotation(toCamera.normalized, Vector3.up),
                    Time.deltaTime * 3f);

            ApplyWings(1f, Time.time * flapSpeed * 0.55f);
        }

        /// <summary>Figure-eight in front of the mural, in mural-local metres.</summary>
        Vector3 FlightPosition(float p)
        {
            var normal = wallNormal.sqrMagnitude < 1e-4f ? Vector3.forward : wallNormal.normalized;
            float a = p * Mathf.PI * 2f;
            float x = Mathf.Sin(a) * orbitWidth;
            float y = paintedLocalPos.y + Mathf.Sin(a * 2f) * orbitHeight;
            float z = (0.45f + 0.55f * (0.5f + 0.5f * Mathf.Cos(a * 2f))) * orbitDepth;
            return new Vector3(x, y, 0f) + normal * z;
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

                // INTERACTION 2: steer the bird around its path.
                if (stage == Stage.Flying && Mathf.Abs(dx) > 0.5f)
                {
                    float push = dx / Mathf.Max(Screen.width, 1) * 1.4f;
                    phase = Mathf.Repeat(phase + push, 1f);
                    steerBoost = Mathf.Clamp(steerBoost + Mathf.Abs(push) * 14f, 0f, 3.5f);
                }
                lastPointer = pos;
            }
            else if (pointer.press.wasReleasedThisFrame && dragging)
            {
                dragging = false;

                // INTERACTION 1: a tap (not a drag) on the bird.
                bool isTap = dragDistance < Screen.width * 0.02f;
                if (isTap && hasEmerged && HitSubject(pos)) ToggleInspect();
            }
        }

        void ToggleInspect()
        {
            PlayConfirm();

            if (stage == Stage.Inspecting)
            {
                stage = Stage.Flying;
                // Re-enter the path at whatever point is nearest, so it does not snap.
                phase = NearestPhase(subject.localPosition);
                if (ui != null && ui.State == UIShellController.ShellState.Info) ui.ToggleInfo();
            }
            else
            {
                stage = Stage.Inspecting;
                if (ui != null) { ui.SetInfoText(infoBody); ui.ToggleInfo(); }
            }
        }

        float NearestPhase(Vector3 localPos)
        {
            float best = 0f, bestD = float.MaxValue;
            for (int i = 0; i < 48; i++)
            {
                float p = i / 48f;
                float dsq = (FlightPosition(p) - localPos).sqrMagnitude;
                if (dsq < bestD) { bestD = dsq; best = p; }
            }
            return best;
        }

        bool HitSubject(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null || subject == null) return false;
            var ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 60f)) return false;
            return hit.transform == subject || hit.transform.IsChildOf(subject);
        }

        // ------------------------------------------------------------------ helpers

        void ApplyWings(float spread, float flapClock)
        {
            float open = Mathf.Lerp(wingFoldedAngle, 0f, spread);
            float flap = spread * flapAmplitude * Mathf.Sin(flapClock);

            if (wingLeft != null) wingLeft.localRotation = Quaternion.Euler(0f, 0f, -(open + flap));
            if (wingRight != null) wingRight.localRotation = Quaternion.Euler(0f, 0f, open + flap);
        }

        void SetSubjectAlpha(float a)
        {
            alpha = a;
            if (subjectRenderers == null) return;
            foreach (var r in subjectRenderers)
            {
                if (r == null) continue;
                var c = baseColors.TryGetValue(r, out var bc) ? bc : Color.white;
                c.a = a;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, c);
                mpb.SetColor(ColorId, c);
                r.SetPropertyBlock(mpb);
            }
        }

        void SetOverlay(Color c, float a)
        {
            if (paintedOverlay == null) return;
            c.a = a;
            paintedOverlay.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(ColorId, c);
            paintedOverlay.SetPropertyBlock(mpb);
        }
    }
}
