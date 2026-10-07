using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARMurals
{
    /// <summary>
    /// The single piece of shared runtime code. Watches the tracked image manager,
    /// matches each detected mural to its owner's experience by slot name, parents
    /// that experience to the mural so it sits on the wall, and calls the three
    /// interface methods. It knows nothing about what any mural actually does.
    ///
    /// Lives on the same GameObject as ARTrackedImageManager (the XR Origin).
    /// </summary>
    [RequireComponent(typeof(ARTrackedImageManager))]
    public class ARMuralManager : MonoBehaviour
    {
        [Header("Mural experiences (one per slot, drag each owner's root in here)")]
        [SerializeField] List<MuralExperienceBase> experiences = new List<MuralExperienceBase>();

        [Header("UI")]
        [SerializeField] UIShellController ui;

        [Header("Tracking")]
        [Tooltip("Seconds of limited/none tracking before the experience is told it has lost the mural.")]
        [SerializeField] float trackingLossGrace = 0.5f;

        ARTrackedImageManager imageManager;

        readonly Dictionary<string, MuralExperienceBase> bySlot =
            new Dictionary<string, MuralExperienceBase>(System.StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<TrackableId, MuralExperienceBase> bound =
            new Dictionary<TrackableId, MuralExperienceBase>();
        readonly Dictionary<TrackableId, float> limitedSince =
            new Dictionary<TrackableId, float>();

        void Awake()
        {
            imageManager = GetComponent<ARTrackedImageManager>();

            foreach (var e in experiences)
            {
                if (e == null) continue;
                bySlot[e.MuralSlot] = e;
                e.OnReset();
                e.SetHidden(true);
            }
        }

        void OnEnable() => imageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        void OnDisable() => imageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);

        void Start()
        {
            if (ui == null) return;
            ui.SetState(UIShellController.ShellState.Start);
            ui.ResetRequested += ReplayActive;
            ui.RestartRequested += ResetAll;
        }

        void OnDestroy()
        {
            if (ui == null) return;
            ui.ResetRequested -= ReplayActive;
            ui.RestartRequested -= ResetAll;
        }

        /// <summary>REPLAY: put the mural back to untouched, then run it again straight away.</summary>
        void ReplayActive()
        {
            foreach (var e in experiences)
            {
                if (e == null || !e.IsActive) continue;
                e.OnReset();
                e.OnTrackingFound();
                return;
            }
        }

        /// <summary>BACK TO START: everything untouched and hidden.</summary>
        void ResetAll()
        {
            foreach (var e in experiences)
            {
                if (e == null) continue;
                e.OnReset();
                e.SetHidden(true);
            }
        }

        void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
            foreach (var image in args.added) Bind(image);
            foreach (var image in args.updated) Evaluate(image);

            foreach (var pair in args.removed)
            {
                if (!bound.TryGetValue(pair.Key, out var exp)) continue;
                exp.OnTrackingLost();
                exp.SetHidden(true);
                bound.Remove(pair.Key);
                limitedSince.Remove(pair.Key);
                ReportScanning();
            }
        }

        void Bind(ARTrackedImage image)
        {
            var slot = SlotFor(image);
            if (slot == null)
            {
                Debug.LogWarning($"[ARMuralManager] No experience registered for reference image '{image.referenceImage.name}'.");
                return;
            }

            // Parent to the tracked image so the content sits on the physical wall,
            // in metres, at the mural's real position and orientation.
            slot.transform.SetParent(image.transform, false);
            slot.transform.localPosition = Vector3.zero;
            slot.transform.localRotation = Quaternion.identity;
            slot.transform.localScale = Vector3.one;

            bound[image.trackableId] = slot;
            Evaluate(image);
        }

        void Evaluate(ARTrackedImage image)
        {
            if (!bound.TryGetValue(image.trackableId, out var exp))
            {
                Bind(image);
                return;
            }

            if (image.trackingState == TrackingState.Tracking)
            {
                limitedSince.Remove(image.trackableId);
                if (!exp.IsActive)
                {
                    exp.OnTrackingFound();
                    if (ui != null) ui.ShowExperience();
                }
                return;
            }

            // Limited or None: give it a grace window before declaring the mural lost,
            // so a momentary wobble does not restart the experience.
            if (!limitedSince.ContainsKey(image.trackableId))
                limitedSince[image.trackableId] = Time.time;

            if (Time.time - limitedSince[image.trackableId] >= trackingLossGrace && exp.IsActive)
            {
                exp.OnTrackingLost();
                ReportScanning();
            }
        }

        void ReportScanning()
        {
            if (ui == null) return;
            foreach (var kv in bound)
                if (kv.Value.IsActive) return;
            ui.ShowScanning();
        }

        MuralExperienceBase SlotFor(ARTrackedImage image)
        {
            var name = image.referenceImage.name;
            if (string.IsNullOrEmpty(name)) return null;

            if (bySlot.TryGetValue(name, out var exact)) return exact;

            // Tolerate "M1_target", "M1 - Flying Cars" etc.
            foreach (var kv in bySlot)
                if (name.StartsWith(kv.Key, System.StringComparison.OrdinalIgnoreCase))
                    return kv.Value;

            return null;
        }
    }
}
