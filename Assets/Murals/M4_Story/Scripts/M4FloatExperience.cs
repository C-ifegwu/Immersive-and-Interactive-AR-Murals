using System.Collections;
using UnityEngine;

namespace ARMurals
{
    /// <summary>
    /// M4 - THE FIGURE'S ORNAMENTS COME ALIVE.
    ///
    /// The giant figure in traditional attire is painted surrounded by floating rings, gold
    /// bangles and cubes. The wall dims, and those pieces lift out of the paint and drift
    /// in the air in front of the figure, bobbing and turning slowly, joined by the painted
    /// coloured dots.
    ///
    /// INTERACTION 1 - tap a floating piece: it spins and the information panel opens.
    /// INTERACTION 2 - drag left/right: the pieces swing round the figure, toward you.
    /// </summary>
    public class M4FloatExperience : LayeredMuralExperience
    {
        [System.Serializable]
        public class Floater
        {
            public Renderer renderer;
            public Collider collider;
            [Tooltip("Where it sits on the paint (dots: where they appear from), in wall space.")]
            public Vector3 restPos;
            [Tooltip("How far out of the wall it floats, in metres.")]
            public float depth = 0.4f;
            public float bob = 0.04f;
            public float spin = 15f;
            public float delay;
            public bool fromPaint = true;   // false: appears from nothing (the coloured dots)
        }

        [SerializeField] Floater[] floaters = new Floater[0];
        [Tooltip("The figure's centre in wall space; the pieces orbit round a vertical axis through it.")]
        [SerializeField] Vector3 figureCentre;
        [SerializeField] float maxSwing = 70f;

        float[] spinBoost = new float[0];
        float[] angle = new float[0];
        float out_;               // 0 = on the wall, 1 = floating
        float swing;              // degrees round the figure
        float swingTarget;
        float lastSwing;

        protected override void ResetLayers()
        {
            spinBoost = new float[floaters.Length];
            angle = new float[floaters.Length];
            out_ = 0f;
            swing = swingTarget = 0f;
            for (int i = 0; i < floaters.Length; i++)
            {
                var f = floaters[i];
                if (f.renderer == null) continue;
                f.renderer.transform.localPosition = f.restPos;
                f.renderer.transform.localRotation = Quaternion.identity;
                SetColor(f.renderer, Color.white, f.fromPaint ? 1f : 0f);
                if (f.collider != null) f.collider.enabled = false;
            }
        }

        protected override IEnumerator Reveal()
        {
            float longest = 1.6f;
            foreach (var f in floaters) longest = Mathf.Max(longest, f.delay + 1.6f);
            for (float t = 0f; t < longest; t += Time.deltaTime)
            {
                out_ = 1f;
                Place(t);
                yield return null;
            }
            foreach (var f in floaters) if (f.collider != null) f.collider.enabled = true;
        }

        protected override void Live()
        {
            swing = Mathf.Lerp(swing, swingTarget, Time.deltaTime * 3f);
            swingTarget = Mathf.MoveTowards(swingTarget, 0f, Time.deltaTime * 6f);   // drifts home
            Place(float.MaxValue);
        }

        protected override bool Tap(Ray ray)
        {
            // INTERACTION 1: whichever piece was tapped spins, and the story opens.
            for (int i = 0; i < floaters.Length; i++)
            {
                if (!Hit(floaters[i].collider, ray)) continue;
                spinBoost[i] = 720f;
                PlaySound("M4_Chime", 0.8f, Random.Range(0.9f, 1.15f));
                ToggleInfo();
                return true;
            }
            return false;
        }

        // INTERACTION 2: swing the pieces round the figure.
        protected override void Drag(float dx)
        {
            swingTarget = Mathf.Clamp(swingTarget + dx * 160f, -maxSwing, maxSwing);
            if (Time.time - lastSwing < 0.6f) return;
            lastSwing = Time.time;
            PlaySound("M4_Swing", 0.5f);
        }

        /// <summary>sinceReveal: seconds since the reveal began (huge once live).</summary>
        void Place(float sinceReveal)
        {
            var orbit = Quaternion.Euler(0f, swing, 0f);
            for (int i = 0; i < floaters.Length; i++)
            {
                var f = floaters[i];
                if (f.renderer == null) continue;
                float k = Ease((sinceReveal - f.delay) / 1.6f) * out_;

                float bob = Mathf.Sin(Time.time * 1.1f + i * 1.7f) * f.bob * k;
                var floating = f.restPos + new Vector3(0f, bob, -f.depth * k);
                // Swing round a vertical axis through the figure.
                var pos = figureCentre + orbit * (floating - figureCentre);

                spinBoost[i] = Mathf.MoveTowards(spinBoost[i], 0f, Time.deltaTime * 400f);
                angle[i] += (f.spin * k + spinBoost[i]) * Time.deltaTime;

                var t = f.renderer.transform;
                t.localPosition = pos;
                t.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.7f + i) * 20f * k, angle[i]);
                SetColor(f.renderer, Color.white, f.fromPaint ? 1f : k);
            }
        }
    }
}
