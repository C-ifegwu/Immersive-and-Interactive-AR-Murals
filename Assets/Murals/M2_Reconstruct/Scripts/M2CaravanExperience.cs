using System.Collections;
using UnityEngine;

namespace ARMurals
{
    /// <summary>
    /// M2 - THE CARAVAN MOVES ON.
    ///
    /// The wall dims, the painted camel caravans lift off the dunes and start to walk,
    /// slowly, with a soft step, while a little sand drifts across the desert. Each caravan
    /// walks a short stretch, fades, and starts again from where it was painted.
    ///
    /// INTERACTION 1 - tap the caravan: it stops to rest and the information panel opens.
    ///                 Tap again and it walks on.
    /// INTERACTION 2 - drag left/right: the wind picks up, sand blows harder and the
    ///                 caravans hurry along.
    /// </summary>
    public class M2CaravanExperience : LayeredMuralExperience
    {
        [System.Serializable]
        public class Walker
        {
            public Renderer renderer;
            public Vector3 restPos;
            [Tooltip("How far it walks before starting over, in metres (+ = right).")]
            public float distance = 0.5f;
            public float speed = 0.05f;
            public float stepHeight = 0.006f;
        }

        [SerializeField] Walker[] walkers = new Walker[0];
        [SerializeField] Collider caravanCollider;
        [SerializeField] ParticleSystem sand;
        [SerializeField] float lift = 0.04f;
        [SerializeField] float maxWind = 4f;
        [SerializeField] int leadWalker;
        [SerializeField] float kneelDepth = 0.18f;
        [SerializeField] float stormLean = 4f;

        float[] travelled = new float[0];
        bool resting;
        float wind;
        float nextBell;
        float kneel;
        Vector3 leadScale;
        AudioSource storm;

        protected override void ResetLayers()
        {
            travelled = new float[walkers.Length];
            if (leadScale == Vector3.zero && Lead() != null) leadScale = Lead().transform.localScale;
            kneel = 0f;
            for (int i = 0; i < walkers.Length; i++) Place(i, 0f, 1f, 0f);
            if (sand != null) sand.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (caravanCollider != null) caravanCollider.enabled = false;
            resting = false;
            wind = 0f;
            if (storm == null) storm = LoopSound("M2_Storm");
        }

        protected override IEnumerator Reveal()
        {
            // The caravans lift off the dunes and the sand starts to move.
            if (sand != null) sand.Play();
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                for (int i = 0; i < walkers.Length; i++) Place(i, 0f, 1f, Ease(t / 0.8f));
                yield return null;
            }
            if (caravanCollider != null) caravanCollider.enabled = true;
        }

        protected override void Live()
        {
            wind = Mathf.MoveTowards(wind, 0f, Time.deltaTime * 1.2f);
            kneel = Mathf.MoveTowards(kneel, resting ? 1f : 0f, Time.deltaTime * 1.5f);
            float pace = resting ? 0f : 1f + wind;

            for (int i = 0; i < walkers.Length; i++)
            {
                var w = walkers[i];
                travelled[i] += w.speed * pace * Time.deltaTime;
                if (travelled[i] > Mathf.Abs(w.distance)) travelled[i] = 0f;

                // Fade out at the end of the walk and back in at the start, so the loop is invisible.
                float f = travelled[i] / Mathf.Max(0.01f, Mathf.Abs(w.distance));
                float alpha = Mathf.Clamp01(Mathf.Min(f / 0.12f, (1f - f) / 0.12f));
                if (travelled[i] < 0.0001f) alpha = 1f;
                Place(i, travelled[i] * Mathf.Sign(w.distance), alpha, 1f);
            }

            SetLoop(storm, Mathf.Clamp01(wind / maxWind) * 0.8f, 0.9f + wind * 0.05f);
            if (!resting && Time.time >= nextBell)
            {
                PlaySound("M2_Bells", 0.35f, Random.Range(0.8f, 1.1f));
                nextBell = Time.time + Random.Range(2.5f, 4.5f) / (1f + wind * 0.5f);
            }

            if (sand != null)
            {
                var main = sand.main;
                main.simulationSpeed = 1f + wind;
                var emission = sand.emission;
                emission.rateOverTimeMultiplier = 14f * (1f + wind * 2f);
            }
        }

        protected override bool Tap(Ray ray)
        {
            // INTERACTION 1: stop to rest, and tell the story.
            if (!Hit(caravanCollider, ray)) return false;
            resting = !resting;
            if (resting) PlaySound("M2_Exhale", 0.9f, 0.7f);
            ToggleInfo();
            return true;
        }

        // INTERACTION 2: wind.
        protected override void Drag(float dx) => wind = Mathf.Clamp(wind + Mathf.Abs(dx) * 12f, 0f, maxWind);

        void Place(int i, float x, float alpha, float lifted)
        {
            var w = walkers[i];
            if (w.renderer == null) return;
            // A gentle bob, one per step.
            float step = Mathf.Abs(Mathf.Sin(travelled.Length > i ? travelled[i] * 60f : 0f)) * w.stepHeight * lifted;
            w.renderer.transform.localPosition = w.restPos + new Vector3(x, step, -lift * lifted);
            SetColor(w.renderer, Color.white, alpha);
            float lean = -Mathf.Sign(w.distance) * stormLean * Mathf.Clamp01(wind / maxWind) * lifted;
            w.renderer.transform.localRotation = Quaternion.Euler(0f, 0f, lean);
            if (i == leadWalker) Kneel(w.renderer.transform);
        }

        void Kneel(Transform t)
        {
            if (leadScale == Vector3.zero) return;
            float k = Ease(kneel);
            t.localScale = new Vector3(leadScale.x, leadScale.y * (1f - kneelDepth * k), leadScale.z);
            t.localPosition += Vector3.down * leadScale.y * kneelDepth * 0.5f * k;
        }

        Renderer Lead() => leadWalker < walkers.Length ? walkers[leadWalker].renderer : null;
    }
}
