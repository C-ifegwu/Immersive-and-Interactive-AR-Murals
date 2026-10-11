using System.Collections;
using UnityEngine;

namespace ARMurals
{
    /// <summary>
    /// M5 - LIVING PAINT.
    ///
    /// The painting itself comes alive while staying painted: the wall dims, then the lady's
    /// gold hoop earrings start to swing from their hooks, a glint of light runs down her
    /// gold neck rings, and the acacia trees behind her sway in a breeze.
    ///
    /// INTERACTION 1 - tap an earring: it swings hard, settles slowly, and the information
    ///                 panel opens.
    /// INTERACTION 2 - drag left/right: a gust of wind; the trees bend and both earrings
    ///                 swing the way the wind blows.
    /// </summary>
    public class M5LivingPaintExperience : LayeredMuralExperience
    {
        [Header("Earrings (pivots at the hooks)")]
        [SerializeField] Transform[] earrings = new Transform[0];
        [SerializeField] Collider[] earringColliders = new Collider[0];
        [SerializeField] float stiffness = 9f;
        [SerializeField] float damping = 1.2f;
        [SerializeField] float tapKick = 140f;

        [Header("Trees (pivots at the foot of each canopy)")]
        [SerializeField] Transform[] canopies = new Transform[0];
        [SerializeField] float swayDegrees = 2.5f;

        [Header("Neck rings glint")]
        [SerializeField] Renderer glint;
        [SerializeField] Vector2 glintTopBottom = new Vector2(0.3f, -0.3f);
        [SerializeField] float glintEvery = 3.5f;

        float[] angle = new float[0];
        float[] velocity = new float[0];
        float alive;              // 0 = still paint, 1 = fully alive
        float gust;               // signed: + blows right
        int lastGlint = -1;
        AudioSource wind;

        protected override void ResetLayers()
        {
            angle = new float[earrings.Length];
            velocity = new float[earrings.Length];
            alive = 0f;
            gust = 0f;
            foreach (var e in earrings) if (e != null) e.localRotation = Quaternion.identity;
            foreach (var c in canopies) if (c != null) c.localRotation = Quaternion.identity;
            foreach (var c in earringColliders) if (c != null) c.enabled = false;
            SetColor(glint, Color.white, 0f);
            if (wind == null) wind = LoopSound("M5_Gust");
        }

        protected override IEnumerator Reveal()
        {
            // The paint wakes up gradually: a first small nudge sets the earrings going.
            for (int i = 0; i < velocity.Length; i++) velocity[i] = (i % 2 == 0 ? 1f : -1f) * 40f;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                alive = Ease(t / 1.2f);
                Animate();
                yield return null;
            }
            alive = 1f;
            foreach (var c in earringColliders) if (c != null) c.enabled = true;
        }

        protected override void Live() => Animate();

        void Animate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            gust = Mathf.MoveTowards(gust, 0f, dt * 1.5f);
            SetLoop(wind, Mathf.Clamp01(Mathf.Abs(gust) / 1.5f) * 0.7f * alive);
            float breeze = Mathf.Sin(Time.time * 0.9f) * 0.3f + Mathf.Sin(Time.time * 2.3f) * 0.1f;

            // Earrings: a damped pendulum each, pushed by the breeze and the gust.
            for (int i = 0; i < earrings.Length; i++)
            {
                if (earrings[i] == null) continue;
                float force = (breeze * 6f + gust * 25f) * alive;
                velocity[i] += (-stiffness * angle[i] - damping * velocity[i] + force) * dt;
                angle[i] += velocity[i] * dt;
                angle[i] = Mathf.Clamp(angle[i], -35f, 35f);
                earrings[i].localRotation = Quaternion.Euler(0f, 0f, angle[i]);
            }

            // Trees sway with the breeze and bend with the gust.
            for (int i = 0; i < canopies.Length; i++)
            {
                if (canopies[i] == null) continue;
                float sway = (Mathf.Sin(Time.time * 1.2f + i * 1.3f) * swayDegrees - gust * swayDegrees * 2.5f) * alive;
                canopies[i].localRotation = Quaternion.Euler(0f, 0f, sway);
            }

            // A glint runs down the gold neck rings every few seconds.
            if (glint != null)
            {
                float p = Mathf.Repeat(Time.time, glintEvery) / 1.2f;      // the sweep takes 1.2 s
                int sweep = Mathf.FloorToInt(Time.time / glintEvery);
                if (alive > 0.9f && sweep != lastGlint)
                {
                    lastGlint = sweep;
                    PlaySound("M5_Glint", 0.25f, 1.3f);
                }
                var pos = glint.transform.localPosition;
                pos.y = Mathf.Lerp(glintTopBottom.x, glintTopBottom.y, Mathf.Clamp01(p));
                glint.transform.localPosition = pos;
                SetColor(glint, Color.white, p <= 1f ? Mathf.Sin(p * Mathf.PI) * 0.9f * alive : 0f);
            }
        }

        protected override bool Tap(Ray ray)
        {
            // INTERACTION 1: set the tapped earring swinging.
            for (int i = 0; i < earringColliders.Length && i < velocity.Length; i++)
            {
                if (!Hit(earringColliders[i], ray)) continue;
                velocity[i] += (velocity[i] >= 0f ? 1f : -1f) * tapKick;
                PlaySound("M5_Jingle", 0.7f, Random.Range(0.9f, 1.1f));
                ToggleInfo();
                return true;
            }
            return false;
        }

        // INTERACTION 2: a gust of wind in the drag direction.
        protected override void Drag(float dx) => gust = Mathf.Clamp(gust + dx * 6f, -1.5f, 1.5f);
    }
}
