using UnityEngine;
using UnityEngine.InputSystem;

namespace ARMurals
{
    /// <summary>
    /// Base class every mural experience derives from. Handles the interface plumbing,
    /// the named audio slots and the debug keys, so each owner only writes their own
    /// HandleTrackingFound / HandleTrackingLost / HandleReset.
    ///
    /// DEBUG KEYS (Editor and development builds, when Debug Keys is ticked):
    ///   F = simulate tracking found    L = simulate tracking lost    R = reset
    /// This is what lets you build and polish your whole mural with no camera,
    /// no manager and nobody else's files.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class MuralExperienceBase : MonoBehaviour, IMuralExperience
    {
        [Header("Identity")]
        [Tooltip("M1..M5. Must match the reference image name in the shared library.")]
        [SerializeField] string muralSlot = "M1";

        [Tooltip("Real-world width of the physical mural in metres, from the tape measure.")]
        [SerializeField] float muralWidthMeters = 2.0f;

        [Header("Audio slots (names are fixed by the conventions contract)")]
        [SerializeField] AudioSource sfxDetect;
        [SerializeField] AudioSource sfxLost;
        [SerializeField] AudioSource sfxConfirm;
        [SerializeField] AudioSource ambientBed;

        [Header("Content root")]
        [Tooltip("Everything digital lives under here. Hidden on tracking loss.")]
        [SerializeField] protected Transform contentRoot;

        [Header("Debug")]
        [SerializeField] bool debugKeys = true;

        public string MuralSlot => muralSlot;
        public float MuralWidthMeters => muralWidthMeters;
        public bool IsActive { get; private set; }

        protected virtual void Awake()
        {
            if (contentRoot == null) contentRoot = transform;
            ResolveAudioSlots();
            HandleReset();
            IsActive = false;
        }

        protected virtual void Update()
        {
            if (!debugKeys) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.fKey.wasPressedThisFrame) OnTrackingFound();
            if (kb.lKey.wasPressedThisFrame) OnTrackingLost();
            if (kb.rKey.wasPressedThisFrame) OnReset();
        }

        public void OnTrackingFound()
        {
            if (IsActive) return;
            IsActive = true;
            Play(sfxDetect);
            if (ambientBed != null && !ambientBed.isPlaying) ambientBed.Play();
            HandleTrackingFound();
        }

        public void OnTrackingLost()
        {
            if (!IsActive) return;
            IsActive = false;
            Play(sfxLost);
            if (ambientBed != null) ambientBed.Pause();
            HandleTrackingLost();
        }

        public void OnReset()
        {
            IsActive = false;
            if (ambientBed != null) ambientBed.Stop();
            HandleReset();
        }

        /// <summary>Begin or resume the experience. Tracking is live.</summary>
        protected abstract void HandleTrackingFound();

        /// <summary>Hide digital content within half a second. Keep state so resume works.</summary>
        protected abstract void HandleTrackingLost();

        /// <summary>Put the mural back to untouched: nothing digital visible, no state.</summary>
        protected abstract void HandleReset();

        /// <summary>
        /// Hide or show the digital content WITHOUT disabling this GameObject, so the
        /// experience keeps running (and the debug keys keep working) while nothing is drawn.
        /// </summary>
        public void SetHidden(bool hidden)
        {
            if (contentRoot != null) contentRoot.gameObject.SetActive(!hidden);
        }

        protected void PlayConfirm() => Play(sfxConfirm);

        static void Play(AudioSource src)
        {
            if (src != null && src.clip != null) src.PlayOneShot(src.clip);
        }

        void ResolveAudioSlots()
        {
            // Resolve by the fixed names if the inspector slots were left empty.
            if (sfxDetect == null) sfxDetect = FindSlot("SFX_Detect");
            if (sfxLost == null) sfxLost = FindSlot("SFX_Lost");
            if (sfxConfirm == null) sfxConfirm = FindSlot("SFX_Confirm");
            if (ambientBed == null) ambientBed = FindSlot("Ambient_Bed");
        }

        AudioSource FindSlot(string slotName)
        {
            var t = transform.Find(slotName);
            return t != null ? t.GetComponent<AudioSource>() : null;
        }
    }
}
