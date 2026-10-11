using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARMurals
{
    /// <summary>
    /// M1 - EMERGENCE. "The Mask Remembers".
    ///
    /// The mural's left half is a cubist mask; its right half is five historical figures
    /// joined by a gold map of Africa. The experience connects the two.
    ///
    /// When the mural is detected, the painted mask breaks into the shards it is already
    /// drawn as. Each shard lifts off the wall and takes its place on a curved surface in
    /// the air, so the flat painting becomes a carved mask with real form. The shards carry
    /// the mural's own paint - they ARE the photograph - so nothing reads as imported.
    ///
    /// INTERACTION 1 - tap the mask: it opens along its own fracture lines and the five
    ///                 painted figures rise out of it and orbit. Tap a figure to bring it
    ///                 forward and read who they were.
    /// INTERACTION 2 - drag: turn the mask. Whichever figure is facing you is the one lit
    ///                 on the wall, so turning the abstract thing navigates the real people.
    /// INTERACTION 3 - tap the mask once all five have been met: THE WEARING. The figures
    ///                 go back inside, the mask turns square to you and its paint drains to
    ///                 an outline, so the live camera feed shows through where a face would
    ///                 be. A masquerade mask is a vessel, not a portrait: it is only an
    ///                 ancestor while somebody is inside it. The five are previous wearers.
    ///                 The wall is not a monument, it is a queue - and you are next in it.
    /// </summary>
    public class M1EmergenceExperience : MuralExperienceBase
    {
        enum Stage { Untouched, Breaking, Assembled, Opened, Inspecting, Worn }

        [Header("Placeholder scaffolding (untick for the real wall)")]
        [Tooltip("Quad showing the mural photograph. The physical wall replaces this on site.")]
        [SerializeField] GameObject muralPlane;
        [SerializeField] bool showMuralPlane = true;

        [Header("Painted region (the mask as it is painted on the wall)")]
        [Tooltip("Photographic patch over the painted mask. Fading it to the dim tint is what " +
                 "makes the real paint appear to drain out of the wall.")]
        [SerializeField] Renderer paintedOverlay;
        [SerializeField] Color dimTint = new Color(0.14f, 0.13f, 0.17f, 1f);
        [SerializeField] float dimDuration = 1.0f;

        [Header("The mask")]
        [Tooltip("Parent of the shards. Starts flat in the wall, ends hovering in front of it.")]
        [SerializeField] Transform maskRoot;
        [SerializeField] List<Transform> shards = new List<Transform>();
        [SerializeField] List<Renderer> shardRenderers = new List<Renderer>();
        [SerializeField] Collider maskCollider;

        [Tooltip("Out of the wall, in mural-local space.")]
        [SerializeField] Vector3 wallNormal = new Vector3(0f, 0f, 1f);
        [Tooltip("Work out which side of the wall the viewer is on and build the mask towards them. " +
                 "Leave this on: it is what stops the mask assembling backwards into the brickwork.")]
        [SerializeField] bool autoResolveNormal = true;
        [Tooltip("How far the finished mask floats off the wall, in metres.")]
        [SerializeField] float standoff = 0.85f;
        [Tooltip("How deep the mask's curvature is, in metres. This is what gives it form.")]
        [SerializeField] float maskDepth = 0.55f;
        [SerializeField] float assembleDuration = 2.6f;
        [SerializeField] float shardStagger = 0.085f;
        [SerializeField] float idleSpin = 4.5f;

        [Header("The wearing (interaction 3)")]
        [Tooltip("How transparent the mask goes once it turns to you. Low, so the camera " +
                 "feed reads through the silhouette and the mask is visibly empty.")]
        [SerializeField, Range(0f, 0.6f)] float wornAlpha = 0.12f;
        [SerializeField] float wearDuration = 1.6f;
        [SerializeField, TextArea(4, 12)]
        string wornInfo =
            "THE MASK IS EMPTY.\n\n" +
            "A masquerade mask is not a portrait and not a disguise. It is a vessel. On a " +
            "wall it is only paint. It becomes an ancestor while somebody is inside it, " +
            "dancing - and the wearer is never named.\n\n" +
            "Yaa Asantewaa. Mansa Musa. Thomas Sankara. Julius Nyerere. " +
            "Winnie Madikizela-Mandela. Five people who wore this, and set it down.\n\n" +
            "You are looking straight through it now. This wall is not a monument. " +
            "It is a queue.";

        [Header("The figures")]
        [SerializeField] Transform figureRoot;
        [SerializeField] List<Transform> figures = new List<Transform>();
        [SerializeField] List<Renderer> figureRenderers = new List<Renderer>();
        [SerializeField] List<Collider> figureColliders = new List<Collider>();
        [SerializeField] float figureOrbitRadius = 1.15f;
        [SerializeField] float figureOrbitSpeed = 0.055f;
        [SerializeField] float openDuration = 1.3f;

        [Header("Wall highlight")]
        [Tooltip("Quads sitting over each painted figure on the wall; the selected one lights up.")]
        [SerializeField] List<Renderer> wallHighlights = new List<Renderer>();

        [Header("Effects")]
        [SerializeField] ParticleSystem emergeParticles;
        [SerializeField] Light maskLight;

        [Header("UI")]
        [SerializeField] UIShellController ui;
        [SerializeField, TextArea(3, 6)]
        string maskInfo =
            "The mask is the mural's own paint, lifted off the wall. Tap it to open it.";
        [SerializeField]
        string[] figureInfo = new string[5];

        // ---- runtime
        Stage stage = Stage.Untouched;
        Coroutine sequence;
        float orbitPhase;
        float spin;
        int selected = -1;
        bool dragging;
        Vector2 lastPointer;
        float dragDistance;

        readonly HashSet<int> visited = new HashSet<int>();   // figures the viewer has met
        readonly List<Vector3> flatPos = new List<Vector3>();
        readonly List<Vector3> targetPos = new List<Vector3>();
        readonly List<Quaternion> targetRot = new List<Quaternion>();
        readonly List<Vector3> figureHome = new List<Vector3>();
        Vector3 maskHome;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock mpbCache;
        MaterialPropertyBlock Mpb => mpbCache ??= new MaterialPropertyBlock();

        // +1 keeps wallNormal as authored, -1 mirrors it. Resolved from the camera the
        // moment the image is found, so the mask always builds out towards whoever is looking.
        float normalSign = 1f;
        Vector3 RawNormal => wallNormal.sqrMagnitude < 1e-4f ? Vector3.forward : wallNormal.normalized;
        Vector3 Normal => RawNormal * normalSign;

        /// <summary>
        /// The tracked image's axes can point either way out of a wall depending on how the
        /// image was registered. Rather than hard-code a guess, read it off the camera: the
        /// viewer is always in front of the mural, so whichever way they are is "out".
        /// </summary>
        void ResolveNormal()
        {
            normalSign = 1f;
            if (!autoResolveNormal) return;
            var cam = Camera.main;
            if (cam == null) return;
            var camLocal = transform.InverseTransformPoint(cam.transform.position);
            float d = Vector3.Dot(camLocal, RawNormal);
            if (Mathf.Abs(d) > 0.05f) normalSign = Mathf.Sign(d);
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void Awake()
        {
            CachePoses();
            base.Awake();
        }

        /// <summary>
        /// Each shard's flat pose is where it is painted. Its target pose is that same
        /// position pushed out onto a curved shell and tilted to face outward - so the
        /// assembled mask is the painting with depth, not a different object.
        /// </summary>
        void CachePoses()
        {
            flatPos.Clear(); targetPos.Clear(); targetRot.Clear();
            if (maskRoot == null) return;
            maskHome = maskRoot.localPosition;

            // The mask's own extent, used to normalise each shard's position within it.
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var s in shards)
            {
                if (s == null) continue;
                min = Vector2.Min(min, new Vector2(s.localPosition.x, s.localPosition.y));
                max = Vector2.Max(max, new Vector2(s.localPosition.x, s.localPosition.y));
            }
            var centre = (min + max) * 0.5f;
            var half = Vector2.Max((max - min) * 0.5f, new Vector2(0.01f, 0.01f));

            foreach (var s in shards)
            {
                if (s == null) continue;
                var p = s.localPosition;
                flatPos.Add(p);

                float su = Mathf.Clamp((p.x - centre.x) / half.x, -1f, 1f);
                float sv = Mathf.Clamp((p.y - centre.y) / half.y, -1f, 1f);

                // Shell: centre shards push furthest forward, edge shards stay back.
                float bulge = Mathf.Cos(su * Mathf.PI * 0.45f) * Mathf.Cos(sv * Mathf.PI * 0.45f);
                // Stay at the painted x/y; only push out along the wall normal. The mask
                // is the painting given depth, not a new object in a new place.
                targetPos.Add(new Vector3(p.x, p.y, 0f) + Normal * (maskDepth * bulge));

                // Tilt each shard to lie along the shell, so the silhouette reads as carved.
                targetRot.Add(Quaternion.Euler(sv * 26f, -su * 32f, 0f));
            }

            figureHome.Clear();
            foreach (var f in figures) figureHome.Add(f != null ? f.localPosition : Vector3.zero);
        }

        protected override void HandleReset()
        {
            if (sequence != null) { StopCoroutine(sequence); sequence = null; }

            stage = Stage.Untouched;
            orbitPhase = 0f; spin = 0f; selected = -1; dragging = false;
            visited.Clear();

            if (muralPlane != null) muralPlane.SetActive(showMuralPlane);
            SetTint(paintedOverlay, Color.white);

            if (maskRoot != null)
            {
                maskRoot.localPosition = maskHome;
                maskRoot.localRotation = Quaternion.identity;
            }
            for (int i = 0; i < shards.Count; i++)
            {
                if (shards[i] == null || i >= flatPos.Count) continue;
                shards[i].localPosition = flatPos[i];
                shards[i].localRotation = Quaternion.identity;
            }
            SetAlpha(shardRenderers, 0f);
            if (maskCollider != null) maskCollider.enabled = false;

            if (figureRoot != null) figureRoot.gameObject.SetActive(false);
            for (int i = 0; i < figures.Count; i++)
                if (figures[i] != null && i < figureHome.Count) figures[i].localPosition = figureHome[i];
            SetAlpha(figureRenderers, 0f);
            foreach (var c in figureColliders) if (c != null) c.enabled = false;
            foreach (var h in wallHighlights) SetTint(h, new Color(1f, 1f, 1f, 0f));

            if (maskLight != null) { maskLight.enabled = false; maskLight.intensity = 0f; }
            if (emergeParticles != null)
                emergeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (contentRoot != null) contentRoot.gameObject.SetActive(true);
            if (ui != null) ui.SetInfoText(maskInfo);
        }

        protected override void HandleTrackingFound()
        {
            if (contentRoot != null) contentRoot.gameObject.SetActive(true);
            if (stage != Stage.Untouched)                  // resume, never restart
            {
                if (ui != null) ui.ShowExperience();
                return;
            }
            PlaceForEditorTesting();
            ResolveNormal();                               // which way is "off the wall"?
            CachePoses();                                  // re-bake the shell for that direction
            if (sequence != null) StopCoroutine(sequence);
            sequence = StartCoroutine(BreakAndAssemble());
        }

        /// <summary>
        /// Editor only. In Play mode there is no printed wall to track, so the mural would
        /// otherwise sit wherever the scene happens to leave it - usually off to one side of
        /// the simulated room. Park it squarely in front of the simulated camera instead so
        /// the whole sequence can be rehearsed before the APK goes on a phone. On device the
        /// manager has already reparented this to the tracked image, so the guard skips it.
        /// </summary>
        void PlaceForEditorTesting()
        {
#if UNITY_EDITOR
            var cam = Camera.main;
            if (cam == null || transform.parent != null) return;
            var fwd = cam.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            transform.position = cam.transform.position + fwd * 4.2f - Vector3.up * 0.3f;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
#endif
        }

        protected override void HandleTrackingLost()
        {
            if (contentRoot != null) contentRoot.gameObject.SetActive(false);
            dragging = false;
            if (ui != null && ui.State == UIShellController.ShellState.Info) ui.ToggleInfo();
            // Put the scanning prompt back. On device the manager does this too; doing it
            // here as well means the L debug key behaves exactly like a real loss.
            if (ui != null) ui.ShowScanning();
        }

        // ------------------------------------------------------------------ the sequence

        IEnumerator BreakAndAssemble()
        {
            stage = Stage.Breaking;

            // 1. The paint drains out of the wall while the shards fade up in its place.
            for (float t = 0f; t < dimDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / dimDuration);
                SetTint(paintedOverlay, Color.Lerp(Color.white, dimTint, k));
                SetAlpha(shardRenderers, k);
                yield return null;
            }
            SetTint(paintedOverlay, dimTint);
            SetAlpha(shardRenderers, 1f);

            if (emergeParticles != null) emergeParticles.Play();
            if (maskLight != null) maskLight.enabled = true;

            // 2. Shard by shard, the flat painting becomes a carved mask hovering off the wall.
            float total = assembleDuration + shardStagger * shards.Count;
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                for (int i = 0; i < shards.Count; i++)
                {
                    if (shards[i] == null || i >= targetPos.Count) continue;
                    float local = Mathf.Clamp01((t - i * shardStagger) / assembleDuration);
                    float k = Mathf.SmoothStep(0f, 1f, local);
                    // Slight overshoot outward so each piece feels pulled, not slid.
                    float arc = Mathf.Sin(local * Mathf.PI) * 0.10f;
                    shards[i].localPosition = Vector3.Lerp(flatPos[i], targetPos[i], k) + Normal * arc;
                    shards[i].localRotation = Quaternion.Slerp(Quaternion.identity, targetRot[i], k);
                }

                float m = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / total));
                if (maskRoot != null) maskRoot.localPosition = maskHome + Normal * (standoff * m);
                if (maskLight != null) maskLight.intensity = Mathf.Lerp(0f, 1.4f, m);
                yield return null;
            }

            for (int i = 0; i < shards.Count; i++)
            {
                if (shards[i] == null || i >= targetPos.Count) continue;
                shards[i].localPosition = targetPos[i];
                shards[i].localRotation = targetRot[i];
            }
            if (maskRoot != null) maskRoot.localPosition = maskHome + Normal * standoff;
            if (maskCollider != null) maskCollider.enabled = true;

            stage = Stage.Assembled;
            sequence = null;
            if (ui != null) ui.ShowExperience();
        }

        /// <summary>INTERACTION 1: the mask opens and the painted figures rise out of it.</summary>
        IEnumerator OpenMask()
        {
            stage = Stage.Opened;
            if (figureRoot != null) figureRoot.gameObject.SetActive(true);
            foreach (var c in figureColliders) if (c != null) c.enabled = true;

            for (float t = 0f; t < openDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / openDuration);

                // Shards spread apart along the shell: the mask opens along its own fractures.
                for (int i = 0; i < shards.Count; i++)
                {
                    if (shards[i] == null || i >= targetPos.Count) continue;
                    var outward = (targetPos[i] - Normal * (maskDepth * 0.5f)).normalized;
                    shards[i].localPosition = targetPos[i] + outward * (0.28f * k);
                }
                SetAlpha(shardRenderers, Mathf.Lerp(1f, 0.55f, k));
                SetAlpha(figureRenderers, k);
                yield return null;
            }
            sequence = null;
        }

        IEnumerator CloseMask()
        {
            foreach (var c in figureColliders) if (c != null) c.enabled = false;
            for (float t = 0f; t < openDuration * 0.7f; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / (openDuration * 0.7f));
                for (int i = 0; i < shards.Count; i++)
                {
                    if (shards[i] == null || i >= targetPos.Count) continue;
                    var outward = (targetPos[i] - Normal * (maskDepth * 0.5f)).normalized;
                    shards[i].localPosition = targetPos[i] + outward * (0.28f * (1f - k));
                }
                SetAlpha(shardRenderers, Mathf.Lerp(0.55f, 1f, k));
                SetAlpha(figureRenderers, 1f - k);
                yield return null;
            }
            if (figureRoot != null) figureRoot.gameObject.SetActive(false);
            foreach (var h in wallHighlights) SetTint(h, new Color(1f, 1f, 1f, 0f));
            selected = -1;
            stage = Stage.Assembled;
            sequence = null;
        }

        // ------------------------------------------------------------------ per frame

        protected override void Update()
        {
            base.Update();                           // debug keys F / L / R
            if (!IsActive) return;

            ReadPointer();

            if (stage == Stage.Assembled || stage == Stage.Opened || stage == Stage.Inspecting)
            {
                // Idle turn, plus whatever the user flicked.
                if (maskRoot != null && !dragging)
                {
                    spin = Mathf.Lerp(spin, idleSpin, Time.deltaTime * 1.5f);
                    maskRoot.Rotate(Vector3.up, spin * Time.deltaTime, Space.Self);
                }
            }

            if (stage == Stage.Opened || stage == Stage.Inspecting) OrbitFigures();

            // Debug key, same spirit as F / L / R on the base class: M forces the wearing
            // without having to tap all five first. Editor convenience only.
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame
                && sequence == null
                && (stage == Stage.Assembled || stage == Stage.Opened || stage == Stage.Inspecting))
            {
                if (figureRoot != null) figureRoot.gameObject.SetActive(true);
                sequence = StartCoroutine(WearMask());
            }
        }

        void OrbitFigures()
        {
            orbitPhase = Mathf.Repeat(orbitPhase + figureOrbitSpeed * Time.deltaTime, 1f);
            int n = Mathf.Max(figures.Count, 1);

            for (int i = 0; i < figures.Count; i++)
            {
                if (figures[i] == null) continue;
                float a = (orbitPhase + i / (float)n) * Mathf.PI * 2f;

                var pos = new Vector3(Mathf.Sin(a) * figureOrbitRadius,
                                      Mathf.Cos(a * 2f) * 0.18f,
                                      Mathf.Cos(a) * figureOrbitRadius * 0.45f);

                if (i == selected)
                {
                    // Selected figure comes forward and holds still in front of the mask.
                    pos = Normal * (figureOrbitRadius * 0.55f) + Vector3.up * 0.12f;
                }

                figures[i].localPosition = Vector3.Lerp(figures[i].localPosition, pos, Time.deltaTime * 3f);

                // Always face the viewer: these are painted portraits, not sculptures.
                var cam = Camera.main;
                if (cam != null)
                {
                    var look = figures[i].parent.InverseTransformPoint(cam.transform.position) - figures[i].localPosition;
                    if (look.sqrMagnitude > 1e-5f)
                        figures[i].localRotation = Quaternion.Slerp(figures[i].localRotation,
                            Quaternion.LookRotation(-look.normalized, Vector3.up), Time.deltaTime * 5f);
                }
            }
        }

        // ------------------------------------------------------------------ interactions

        void ReadPointer()
        {
            var pointer = Pointer.current;
            if (pointer == null) return;
            var pos = pointer.position.ReadValue();

            if (pointer.press.wasPressedThisFrame)
            {
                lastPointer = pos; dragDistance = 0f; dragging = true;
            }
            else if (pointer.press.isPressed && dragging)
            {
                float dx = pos.x - lastPointer.x;
                dragDistance += Mathf.Abs(dx);

                // INTERACTION 2: turn the mask.
                if (maskRoot != null && stage != Stage.Untouched && stage != Stage.Breaking
                    && Mathf.Abs(dx) > 0.4f)
                {
                    float degrees = -dx * 0.32f;
                    maskRoot.Rotate(Vector3.up, degrees, Space.Self);
                    spin = degrees / Mathf.Max(Time.deltaTime, 0.0001f) * 0.05f;
                    HighlightFacingFigure();
                }
                lastPointer = pos;
            }
            else if (pointer.press.wasReleasedThisFrame && dragging)
            {
                dragging = false;
                if (dragDistance < Screen.width * 0.02f) HandleTap(pos);
            }
        }

        void HandleTap(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.ScreenPointToRay(screenPos), out var hit, 60f)) return;

            // A figure?
            for (int i = 0; i < figureColliders.Count; i++)
            {
                if (figureColliders[i] == null || !figureColliders[i].enabled) continue;
                if (hit.collider != figureColliders[i]) continue;
                PlayConfirm();
                selected = (selected == i) ? -1 : i;
                if (selected >= 0)
                {
                    stage = Stage.Inspecting;
                    visited.Add(i);
                    if (ui != null)
                    {
                        ui.SetInfoText(selected < figureInfo.Length && !string.IsNullOrEmpty(figureInfo[selected])
                            ? figureInfo[selected]
                            : "This figure's story has not been written in yet.");
                        if (ui.State != UIShellController.ShellState.Info) ui.ToggleInfo();
                    }
                    LightWall(i);
                }
                else
                {
                    stage = Stage.Opened;
                    if (ui != null && ui.State == UIShellController.ShellState.Info) ui.ToggleInfo();
                    LightWall(-1);
                }
                return;
            }

            // INTERACTION 1: the mask itself.
            if (maskCollider != null && hit.collider == maskCollider && sequence == null)
            {
                PlayConfirm();
                if (stage == Stage.Assembled) sequence = StartCoroutine(OpenMask());
                else if (stage == Stage.Opened || stage == Stage.Inspecting)
                {
                    if (ui != null && ui.State == UIShellController.ShellState.Info) ui.ToggleInfo();
                    // Met all five? Then the mask has nothing left to show you but yourself.
                    sequence = StartCoroutine(AllFiguresMet ? WearMask() : CloseMask());
                }
            }
        }

        /// <summary>True once the viewer has brought every figure forward at least once.</summary>
        bool AllFiguresMet
        {
            get
            {
                int live = 0;
                for (int i = 0; i < figures.Count; i++) if (figures[i] != null) live++;
                return live > 0 && visited.Count >= live;
            }
        }

        /// <summary>
        /// INTERACTION 3: THE WEARING. The five go back inside, the mask squares up to the
        /// viewer and its paint drains away until the camera feed shows through it. The
        /// silhouette stays; the face does not. What the viewer sees in the hole is the
        /// room they are standing in - and, if they are holding the phone up, themselves.
        /// </summary>
        IEnumerator WearMask()
        {
            stage = Stage.Worn;
            selected = -1;
            foreach (var c in figureColliders) if (c != null) c.enabled = false;
            LightWall(-1);
            if (emergeParticles != null) emergeParticles.Play();

            var cam = Camera.main;
            var startRot = maskRoot != null ? maskRoot.localRotation : Quaternion.identity;
            var endRot = startRot;
            if (maskRoot != null && cam != null)
            {
                // Aim the mask's outward axis straight down the line to the viewer.
                var camLocal = transform.InverseTransformPoint(cam.transform.position);
                var toCam = camLocal - maskRoot.localPosition;
                if (toCam.sqrMagnitude > 1e-4f)
                    endRot = Quaternion.FromToRotation(Normal, toCam.normalized);
            }
            var startPos = maskRoot != null ? maskRoot.localPosition : Vector3.zero;
            var endPos = startPos + Normal * 0.12f;   // a small step towards whoever is looking

            for (float t = 0f; t < wearDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / wearDuration);

                // The five fold back into the middle of the mask and go out.
                for (int i = 0; i < figures.Count; i++)
                {
                    if (figures[i] == null) continue;
                    figures[i].localPosition = Vector3.Lerp(figures[i].localPosition, Vector3.zero,
                                                            Time.deltaTime * 4f);
                }
                SetAlpha(figureRenderers, 1f - k);

                // The mask closes its own fractures, turns to face you, and empties out.
                for (int i = 0; i < shards.Count; i++)
                {
                    if (shards[i] == null || i >= targetPos.Count) continue;
                    shards[i].localPosition = Vector3.Lerp(shards[i].localPosition, targetPos[i],
                                                           Time.deltaTime * 6f);
                }
                if (maskRoot != null)
                {
                    maskRoot.localRotation = Quaternion.Slerp(startRot, endRot, k);
                    maskRoot.localPosition = Vector3.Lerp(startPos, endPos, k);
                }
                SetAlpha(shardRenderers, Mathf.Lerp(0.55f, wornAlpha, k));
                if (maskLight != null) maskLight.intensity = Mathf.Lerp(1.4f, 2.2f, Mathf.Sin(k * Mathf.PI));
                yield return null;
            }

            SetAlpha(figureRenderers, 0f);
            if (figureRoot != null) figureRoot.gameObject.SetActive(false);
            SetAlpha(shardRenderers, wornAlpha);
            if (maskLight != null) maskLight.intensity = 1.0f;

            if (ui != null)
            {
                ui.SetInfoText(wornInfo);
                if (ui.State != UIShellController.ShellState.Info) ui.ToggleInfo();
            }
            sequence = null;
        }

        /// <summary>Turning the mask lights the figure now facing the viewer, on the wall.</summary>
        void HighlightFacingFigure()
        {
            if (stage != Stage.Opened && stage != Stage.Inspecting) return;
            if (figures.Count == 0 || Camera.main == null) return;

            // Whichever figure the viewer is standing in front of, measured along the wall.
            // figureHome is used rather than the live position because the figures orbit
            // once the mask is open, but their painted homes on the wall do not move.
            var camLocal = transform.InverseTransformPoint(Camera.main.transform.position);
            int best = -1; float bestDist = float.MaxValue;
            for (int i = 0; i < figures.Count && i < figureHome.Count; i++)
            {
                if (figures[i] == null) continue;
                float d = Mathf.Abs(figureHome[i].x - camLocal.x);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            LightWall(best);
        }

        void LightWall(int index)
        {
            for (int i = 0; i < wallHighlights.Count; i++)
                SetTint(wallHighlights[i], new Color(1f, 0.92f, 0.70f, i == index ? 0.55f : 0f));
        }

        // ------------------------------------------------------------------ helpers

        void SetAlpha(List<Renderer> list, float a)
        {
            foreach (var r in list)
            {
                if (r == null) continue;
                r.GetPropertyBlock(Mpb);
                Mpb.SetColor(BaseColorId, new Color(1f, 1f, 1f, a));
                r.SetPropertyBlock(Mpb);
            }
        }

        void SetTint(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(Mpb);
            Mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(Mpb);
        }
    }
}
