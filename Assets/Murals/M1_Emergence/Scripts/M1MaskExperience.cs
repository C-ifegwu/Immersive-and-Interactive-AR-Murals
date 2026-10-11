using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ARMurals
{
    public class M1MaskExperience : LayeredMuralExperience
    {
        enum Stage { Whole, Open, Closing, Empty }

        [System.Serializable]
        public class Wearer
        {
            public string name;
            [TextArea(3, 6)] public string story;
            public Renderer renderer;
            public Collider collider;
            public Vector3 paintedSpot;
        }

        [Header("Mask")]
        [SerializeField] Transform mask;
        [SerializeField] Renderer[] shards = new Renderer[0];
        [SerializeField] Collider maskCollider;
        [SerializeField] Renderer glow;
        [SerializeField] Renderer hollow;
        [SerializeField] Color amber = new Color(1f, 0.62f, 0.2f);
        [SerializeField] float maskRadius = 0.9f;
        [SerializeField] float bulge = 0.35f;
        [SerializeField] float forward = 0.25f;
        [SerializeField] float emergeTime = 3f;
        [SerializeField] float splitGap = 0.35f;

        [Header("Wearers")]
        [SerializeField] Wearer[] wearers = new Wearer[0];
        [SerializeField] Vector2 orbitRadius = new Vector2(1.1f, 0.45f);
        [SerializeField] float orbitSpeed = 0.25f;
        [SerializeField] Renderer projection;

        [Header("Ending")]
        [SerializeField] float faceDistance = 0.45f;
        [SerializeField, TextArea(4, 8)] string endingText =
            "The mask is empty. Five people wore it, gave it a body for a while, and set it down. You are looking straight through it now.\n\nThis wall is not a monument. It is a queue.";

        Stage stage;
        Vector3[] restPos = new Vector3[0];
        Quaternion[] restRot = new Quaternion[0];
        Vector3 maskHome;
        float shape;
        float split;
        float rise;
        float orbit;
        float spin;
        int focus = -1;
        readonly HashSet<int> visited = new HashSet<int>();
        Coroutine ending;
        AudioSource drone;

        protected override void ResetLayers()
        {
            if (restPos.Length != shards.Length)
            {
                restPos = new Vector3[shards.Length];
                restRot = new Quaternion[shards.Length];
                for (int i = 0; i < shards.Length; i++)
                {
                    if (shards[i] == null) continue;
                    restPos[i] = shards[i].transform.localPosition;
                    restRot[i] = shards[i].transform.localRotation;
                }
                if (mask != null) maskHome = mask.localPosition;
            }

            if (ending != null) { StopCoroutine(ending); ending = null; }
            stage = Stage.Whole;
            shape = split = rise = orbit = spin = 0f;
            focus = -1;
            visited.Clear();

            if (mask != null)
            {
                mask.localPosition = maskHome;
                mask.localRotation = Quaternion.identity;
            }
            PlaceShards();
            foreach (var s in shards) SetColor(s, Color.white, 0f);
            SetColor(glow, amber, 0f);
            SetColor(hollow, Color.white, 0f);
            SetColor(projection, amber, 0f);
            foreach (var w in wearers)
            {
                SetColor(w.renderer, Color.white, 0f);
                if (w.collider != null) w.collider.enabled = false;
            }
            if (maskCollider != null) maskCollider.enabled = false;
            if (drone == null) drone = LoopSound("M1_Drone");
        }

        protected override IEnumerator Reveal()
        {
            SetLoop(drone, 0.6f, 0.5f);
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                foreach (var s in shards) SetColor(s, Color.white, Ease(t));
                yield return null;
            }

            PlaySound("M1_Tear", 0.9f, 0.8f);
            for (float t = 0f; t < emergeTime; t += Time.deltaTime)
            {
                shape = Ease(t / emergeTime);
                PlaceShards();
                SetColor(glow, amber, 0.6f * shape);
                yield return null;
            }
            shape = 1f;
            if (maskCollider != null) maskCollider.enabled = true;
        }

        protected override void Live()
        {
            SetLoop(drone, stage == Stage.Empty ? 0.2f : 0.6f, 0.5f);
            if (stage == Stage.Closing || stage == Stage.Empty) return;

            bool reading = ui != null && ui.State == UIShellController.ShellState.Info;
            if (focus >= 0 && !reading) focus = -1;
            if (stage == Stage.Open && !reading && visited.Count >= wearers.Length && wearers.Length > 0)
            {
                ending = StartCoroutine(Ending());
                return;
            }

            split = Mathf.MoveTowards(split, stage == Stage.Open ? 1f : 0f, Time.deltaTime * 1.2f);
            rise = Mathf.MoveTowards(rise, stage == Stage.Open ? 1f : 0f, Time.deltaTime * 0.6f);
            spin = Mathf.MoveTowards(spin, 0f, Time.deltaTime * 2f);
            orbit += (orbitSpeed + spin) * Time.deltaTime;

            PlaceShards();
            PlaceWearers();
            SetColor(glow, amber, (0.55f + 0.1f * Mathf.Sin(Time.time * 6f) + 0.05f * Mathf.Sin(Time.time * 13f)) * shape);

            float beam = focus >= 0 ? 0.55f + 0.25f * Mathf.Sin(Time.time * 3f) : 0f;
            SetColor(projection, amber, beam);
            if (focus >= 0 && projection != null) projection.transform.localPosition = wearers[focus].paintedSpot;
        }

        protected override bool Tap(Ray ray)
        {
            if (stage == Stage.Whole && Hit(maskCollider, ray))
            {
                stage = Stage.Open;
                PlaySound("M1_Split", 1f, 0.7f);
                foreach (var w in wearers) if (w.collider != null) w.collider.enabled = true;
                return true;
            }

            if (stage != Stage.Open) return false;
            for (int i = 0; i < wearers.Length; i++)
            {
                if (!Hit(wearers[i].collider, ray)) continue;
                Visit(i);
                return true;
            }
            return false;
        }

        protected override void Drag(float dx)
        {
            if (stage == Stage.Open) spin = Mathf.Clamp(spin + dx * 8f, -2f, 2f);
        }

        void Visit(int i)
        {
            if (focus == i)
            {
                focus = -1;
                CloseStory();
                return;
            }
            focus = i;
            visited.Add(i);
            ShowStory(wearers[i].name.ToUpperInvariant() + "\n\n" + wearers[i].story);
        }

        IEnumerator Ending()
        {
            stage = Stage.Closing;
            foreach (var w in wearers) if (w.collider != null) w.collider.enabled = false;
            if (maskCollider != null) maskCollider.enabled = false;

            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                rise = 1f - Ease(t / 0.8f);
                orbit += orbitSpeed * 4f * Time.deltaTime;
                PlaceWearers();
                yield return null;
            }
            rise = 0f;
            PlaceWearers();

            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                split = 1f - Ease(t / 0.3f);
                PlaceShards();
                yield return null;
            }
            split = 0f;
            PlaceShards();
            PlaySound("M1_Snap", 1f);
            Buzz();

            var from = mask != null ? mask.localPosition : Vector3.zero;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                if (mask != null) mask.localPosition = Vector3.Lerp(from, FacePoint(), Ease(t / 1.2f));
                yield return null;
            }

            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                float k = Ease(t / 1.5f);
                foreach (var s in shards) SetColor(s, Color.white, 1f - k);
                SetColor(glow, amber, 0.6f * (1f - k));
                SetColor(hollow, Color.white, k);
                if (mask != null) mask.localPosition = FacePoint();
                yield return null;
            }

            stage = Stage.Empty;
            ShowStory(endingText);
            ending = null;
        }

        Vector3 FacePoint()
        {
            var viewer = ViewerInWall();
            if (!viewer.HasValue) return maskHome + new Vector3(0f, 0f, -1.5f);
            var toWall = (maskHome - viewer.Value).normalized;
            return viewer.Value + toWall * faceDistance;
        }

        void PlaceShards()
        {
            for (int i = 0; i < shards.Length; i++)
            {
                if (shards[i] == null) continue;
                var rest = restPos[i];
                var d = new Vector2(rest.x, rest.y) / Mathf.Max(maskRadius, 0.01f);
                float curve = Mathf.Clamp01(1f - d.sqrMagnitude);
                float side = rest.x < 0f ? -1f : 1f;

                var pos = rest + new Vector3(side * splitGap * split, 0f, -(forward + bulge * curve) * shape);
                var rot = restRot[i] * Quaternion.Euler(d.y * 35f * shape, -d.x * 35f * shape + side * 25f * split, 0f);

                var t = shards[i].transform;
                t.localPosition = pos;
                t.localRotation = rot;
            }
        }

        void PlaceWearers()
        {
            var cam = Camera.main;
            var centre = mask != null ? mask.localPosition : Vector3.zero;
            for (int i = 0; i < wearers.Length; i++)
            {
                var w = wearers[i];
                if (w.renderer == null) continue;
                float a = orbit + i * Mathf.PI * 2f / Mathf.Max(wearers.Length, 1);
                var ring = new Vector3(Mathf.Cos(a) * orbitRadius.x, Mathf.Sin(a * 2f) * 0.08f, -forward - orbitRadius.y * (0.5f + 0.5f * Mathf.Sin(a)));
                var pos = centre + ring * rise;
                if (i == focus) pos = Vector3.Lerp(pos, centre + new Vector3(0f, 0.1f, -forward - 0.8f), 0.85f);

                var t = w.renderer.transform;
                t.localPosition = Vector3.Lerp(t.localPosition, pos, Time.deltaTime * 6f);
                t.localScale = Vector3.one * Mathf.Lerp(0.05f, i == focus ? 1.4f : 1f, rise);
                if (cam != null && wall != null) t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, wall.up);
                SetColor(w.renderer, visited.Contains(i) ? new Color(1f, 0.9f, 0.7f) : Color.white, rise);
            }
        }

        void ShowStory(string text)
        {
            if (ui == null) return;
            ui.SetInfoText(text);
            if (ui.State != UIShellController.ShellState.Info) ui.ToggleInfo();
        }

        void CloseStory()
        {
            if (ui != null && ui.State == UIShellController.ShellState.Info) ui.ToggleInfo();
            if (ui != null) ui.SetInfoText(infoBody);
        }
    }
}
