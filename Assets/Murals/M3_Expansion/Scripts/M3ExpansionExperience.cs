using System.Collections;
using UnityEngine;

namespace ARMurals
{
    /// <summary>
    /// M3 - THE CONVENTION CENTRE LIGHTS UP.
    ///
    /// The wall dims and the painted dome goes dark with it. Then the dome lights up band by
    /// band from the ground, the way the real Kigali Convention Centre does at night, and
    /// keeps a slow chase of light running up it. Riders of the Tour du Rwanda come round
    /// the base of the dome in front of the wall.
    ///
    /// INTERACTION 1 - tap the dome: a burst light show and the information panel opens.
    /// INTERACTION 2 - drag left/right: the riders sprint (or ease off).
    /// </summary>
    public class M3ExpansionExperience : LayeredMuralExperience
    {
        [Header("Dome (element 0 = tower, then bands from the top down)")]
        [SerializeField] Renderer[] bands = new Renderer[0];
        [SerializeField] Renderer glow;
        [SerializeField] Collider domeCollider;
        [SerializeField] float darkLevel = 0.3f;
        [SerializeField] Color[] lightColours =
        {
            new Color(0.75f, 0.90f, 1.00f),   // Rwanda blue
            new Color(1.00f, 0.95f, 0.65f),   // sun yellow
            new Color(0.75f, 1.00f, 0.80f),   // hill green
        };

        [Header("Tour du Rwanda")]
        [SerializeField] Renderer[] riders = new Renderer[0];
        [Tooltip("Centre of the riders' loop at the foot of the dome, in wall space.")]
        [SerializeField] Vector3 loopCentre;
        [SerializeField] Vector2 loopRadius = new Vector2(1.2f, 0.5f);
        [SerializeField] float riderSpeed = 0.35f;
        [SerializeField] float maxSprint = 3f;

        float lit;                 // 0 = dark, 1 = fully lit
        float showUntil = -10f;
        float sprint;
        float loop;                // position around the loop, radians
        Vector2[] riderScale = new Vector2[0];
        AudioSource drums;
        AudioSource wheels;

        protected override void ResetLayers()
        {
            lit = 0f;
            sprint = 0f;
            loop = 0f;
            foreach (var b in bands) SetColor(b, Color.white, 1f);      // exactly the paint
            SetColor(glow, Color.white, 0f);
            if (riderScale.Length != riders.Length)
            {
                riderScale = new Vector2[riders.Length];
                for (int i = 0; i < riders.Length; i++)
                    if (riders[i] != null) riderScale[i] = riders[i].transform.localScale;
            }
            foreach (var r in riders) SetColor(r, Color.white, 0f);
            if (domeCollider != null) domeCollider.enabled = false;
            if (drums == null) drums = LoopSound("M3_Show");
            if (wheels == null) wheels = LoopSound("M3_Wheels");
        }

        protected override IEnumerator Reveal()
        {
            // The dome goes dark with the wall...
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                float g = Mathf.Lerp(1f, darkLevel, Ease(t / 0.6f));
                foreach (var b in bands) SetColor(b, new Color(g, g, g), 1f);
                yield return null;
            }

            // ...then lights up band by band from the ground, the tower last.
            float perBand = 0.28f;
            for (float t = 0f; t < perBand * (bands.Length + 2); t += Time.deltaTime)
            {
                for (int i = 0; i < bands.Length; i++)
                {
                    int order = bands.Length - 1 - i;                   // bottom band first
                    float k = Ease((t - order * perBand) / (perBand * 2f));
                    float g = Mathf.Lerp(darkLevel, 1f, k);
                    SetColor(bands[i], new Color(g, g, g), 1f);
                }
                lit = Ease(t / (perBand * (bands.Length + 2)));
                SetColor(glow, lightColours[1], 0.55f * lit);
                foreach (var r in riders) SetColor(r, Color.white, lit);
                PlaceRiders();
                yield return null;
            }
            lit = 1f;
            if (domeCollider != null) domeCollider.enabled = true;
        }

        protected override void Live()
        {
            // A slow chase of light up the dome; much faster during the light show.
            bool show = Time.time < showUntil;
            float speed = show ? 9f : 2f;
            for (int i = 0; i < bands.Length; i++)
            {
                int order = bands.Length - 1 - i;
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * speed - order * 0.9f);
                float g = show ? Mathf.Lerp(0.55f, 1.15f, wave) : Mathf.Lerp(0.8f, 1f, wave);
                var tint = lightColours[(order + (int)(Time.time * (show ? 4f : 0.5f))) % lightColours.Length];
                SetColor(bands[i], Color.Lerp(Color.white, tint, show ? 0.8f : 0.35f * wave) * g, 1f);
            }
            float glowPulse = 0.45f + 0.15f * Mathf.Sin(Time.time * (show ? 8f : 1.5f));
            SetColor(glow, lightColours[1], show ? 0.85f : glowPulse);

            float beat = Mathf.Clamp01((showUntil - Time.time) / 0.6f);
            SetLoop(drums, drums != null ? Mathf.MoveTowards(drums.volume, beat * 0.7f, Time.deltaTime * 2f) : 0f);

            sprint = Mathf.MoveTowards(sprint, 0f, Time.deltaTime * 0.8f);
            SetLoop(wheels, 0.08f + 0.5f * sprint / maxSprint, 0.8f + 0.4f * sprint / maxSprint);
            loop += riderSpeed * (1f + sprint) * Time.deltaTime;
            PlaceRiders();
        }

        protected override bool Tap(Ray ray)
        {
            // INTERACTION 1: light show + story.
            if (!Hit(domeCollider, ray)) return false;
            showUntil = Time.time + 3f;
            ToggleInfo();
            return true;
        }

        // INTERACTION 2: the riders sprint.
        protected override void Drag(float dx) => sprint = Mathf.Clamp(sprint + Mathf.Abs(dx) * 10f, 0f, maxSprint);

        /// <summary>Riders spaced round a flat loop in front of the dome, always facing the viewer.</summary>
        void PlaceRiders()
        {
            var cam = Camera.main;
            for (int i = 0; i < riders.Length; i++)
            {
                var r = riders[i];
                if (r == null) continue;
                float a = loop + i * Mathf.PI * 2f / riders.Length;
                // Front half of the loop is nearest the viewer; the back half tucks against the wall.
                var pos = loopCentre + new Vector3(Mathf.Cos(a) * loopRadius.x, 0f,
                                                   -0.08f - loopRadius.y * (0.5f + 0.5f * Mathf.Sin(a)));
                var t = r.transform;
                t.localPosition = pos;

                // Billboard toward the camera, flipped so each rider faces the way they ride.
                if (cam != null) t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, wall.up);
                float dir = -Mathf.Sin(a) >= 0f ? 1f : -1f;          // moving right on the front half
                var s = riderScale.Length > i ? riderScale[i] : Vector2.one;
                t.localScale = new Vector3(s.x * dir, s.y, 1f);
            }
        }
    }
}
