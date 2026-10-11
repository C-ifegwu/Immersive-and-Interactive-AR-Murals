using System.Collections;
using UnityEngine;

namespace ARMurals
{
    /// <summary>
    /// M1 - THE STONE HEAD WAKES UP.
    ///
    /// The wall dims, the carved head lifts a little out of the paint, eyelids settle over
    /// its blank eyes, then open on pupils that were never painted. From then on it blinks
    /// by itself and its eyes follow the viewer.
    ///
    /// INTERACTION 1 - tap the face: it winks and the information panel opens.
    /// INTERACTION 2 - move the phone (or drag): the eyes follow you.
    /// </summary>
    public class M1BlinkExperience : LayeredMuralExperience
    {
        [Header("Face")]
        [SerializeField] Transform face;
        [SerializeField] Renderer head;
        [SerializeField] Collider faceCollider;
        [SerializeField] float lift = 0.06f;

        [Header("Eyes (index 0 = left, 1 = right)")]
        [SerializeField] Transform[] lids = new Transform[2];       // pivots at the top of each eye
        [SerializeField] Renderer[] lidRenderers = new Renderer[2];
        [SerializeField] Renderer[] pupils = new Renderer[2];
        [SerializeField] Vector3[] eyeCentres = new Vector3[2];
        [Tooltip("How far the pupil can travel from the centre of the eye, in metres (x, y).")]
        [SerializeField] Vector2 pupilTravel = new Vector2(0.07f, 0.025f);

        [Header("Blinking")]
        [SerializeField] Vector2 blinkInterval = new Vector2(2f, 4.5f);
        [SerializeField] float blinkDuration = 0.22f;

        float nextBlink;
        float blinkStart = -10f;
        float winkStart = -10f;
        float dragLook;            // -1..1, decays back to following the viewer

        protected override void ResetLayers()
        {
            if (face != null) face.localPosition = Vector3.zero;
            for (int i = 0; i < 2; i++)
            {
                SetLid(i, 1f);
                SetColor(lidRenderers[i], Color.white, 0f);
                SetColor(pupils[i], Color.white, 0f);
                if (pupils[i] != null) pupils[i].transform.localPosition = eyeCentres[i] + new Vector3(0f, 0f, -0.004f);
            }
            if (faceCollider != null) faceCollider.enabled = false;
            dragLook = 0f;
        }

        protected override IEnumerator Reveal()
        {
            // The head lifts out of the wall and the lids settle over the blank eyes.
            for (float t = 0f; t < 0.9f; t += Time.deltaTime)
            {
                float k = Ease(t / 0.9f);
                if (face != null) face.localPosition = new Vector3(0f, 0f, -lift * k);
                for (int i = 0; i < 2; i++) SetColor(lidRenderers[i], Color.white, k);
                yield return null;
            }
            for (int i = 0; i < 2; i++) SetColor(pupils[i], Color.white, 1f);
            yield return new WaitForSeconds(0.4f);

            // It wakes: the eyes open slowly, once.
            for (float t = 0f; t < 1.0f; t += Time.deltaTime)
            {
                float open = Ease(t / 1.0f);
                for (int i = 0; i < 2; i++) SetLid(i, 1f - open);
                yield return null;
            }
            for (int i = 0; i < 2; i++) SetLid(i, 0f);

            if (faceCollider != null) faceCollider.enabled = true;
            nextBlink = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);
        }

        protected override void Live()
        {
            // Blinking on its own.
            if (Time.time >= nextBlink)
            {
                blinkStart = Time.time;
                nextBlink = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);
            }
            float blink = Pulse(blinkStart, blinkDuration);
            float wink = Pulse(winkStart, 0.6f);
            SetLid(0, Mathf.Max(blink, wink));       // the left eye winks
            SetLid(1, blink);

            // INTERACTION 2: the pupils follow the viewer (or the drag, in the editor).
            dragLook = Mathf.MoveTowards(dragLook, 0f, Time.deltaTime * 0.6f);
            var viewer = ViewerInWall();
            for (int i = 0; i < 2; i++)
            {
                if (pupils[i] == null) continue;
                Vector2 look = Vector2.zero;
                if (viewer.HasValue)
                {
                    var d = viewer.Value - (eyeCentres[i] + Vector3.forward * -lift);
                    float depth = Mathf.Max(0.3f, -d.z);
                    look = new Vector2(Mathf.Clamp(d.x / depth, -1f, 1f), Mathf.Clamp(d.y / depth, -1f, 1f));
                }
                if (Mathf.Abs(dragLook) > 0.01f) look.x = dragLook;
                var target = eyeCentres[i] + new Vector3(look.x * pupilTravel.x, look.y * pupilTravel.y, -0.004f);
                var p = pupils[i].transform;
                p.localPosition = Vector3.Lerp(p.localPosition, target, Time.deltaTime * 8f);
            }
        }

        protected override bool Tap(Ray ray)
        {
            // INTERACTION 1: wink and tell its story.
            if (!Hit(faceCollider, ray)) return false;
            winkStart = Time.time;
            ToggleInfo();
            return true;
        }

        protected override void Drag(float dx) => dragLook = Mathf.Clamp(dragLook + dx * 4f, -1f, 1f);

        /// <summary>0 = open, 1 = closed. Lids hang from a pivot at the top of the eye.</summary>
        void SetLid(int i, float closed)
        {
            if (lids[i] == null) return;
            lids[i].localScale = new Vector3(1f, Mathf.Max(0.001f, closed), 1f);
        }

        /// <summary>0 -> 1 -> 0 over duration, starting at start.</summary>
        static float Pulse(float start, float duration)
        {
            float t = (Time.time - start) / duration;
            return t < 0f || t > 1f ? 0f : Mathf.Sin(t * Mathf.PI);
        }
    }
}
