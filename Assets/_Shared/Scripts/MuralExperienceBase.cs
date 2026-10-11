using System.Collections.Generic;
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

        /// <summary>Set by the HIDE AR toggle: an active mural keeps running but shows nothing,
        /// so the viewer sees the plain painting. Turning it back on resumes where it was.</summary>
        public static bool EffectsOff;

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> loops = new List<AudioSource>();
        AudioSource sounds;

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
            if (IsActive && contentRoot != null && contentRoot.gameObject.activeSelf == EffectsOff)
                contentRoot.gameObject.SetActive(!EffectsOff);
            if (ambientBed != null) ambientBed.mute = EffectsOff;
            foreach (var l in loops) l.mute = EffectsOff;

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
            Silence();
            HandleTrackingLost();
        }

        public void OnReset()
        {
            IsActive = false;
            if (ambientBed != null) ambientBed.Stop();
            Silence();
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

        protected void PlaySound(string clipName, float volume = 1f, float pitch = 1f)
        {
            var clip = Clip(clipName);
            if (clip == null || EffectsOff) return;
            if (sounds == null) sounds = NewSource("Sounds");
            sounds.pitch = pitch;
            sounds.PlayOneShot(clip, volume);
        }

        protected AudioSource LoopSound(string clipName)
        {
            var src = NewSource(clipName);
            src.clip = Clip(clipName);
            src.loop = true;
            src.volume = 0f;
            loops.Add(src);
            return src;
        }

        protected static void SetLoop(AudioSource src, float volume, float pitch = 1f)
        {
            if (src == null || src.clip == null) return;
            src.volume = volume;
            src.pitch = pitch;
            if (volume > 0.01f && !src.isPlaying) src.Play();
            else if (volume <= 0.01f && src.isPlaying) src.Pause();
        }

        protected static void Buzz()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

        protected void Silence()
        {
            foreach (var l in loops) SetLoop(l, 0f);
        }

        AudioClip Clip(string clipName)
        {
            if (!clips.TryGetValue(clipName, out var clip))
            {
                clip = Resources.Load<AudioClip>("Audio/" + clipName);
                clips[clipName] = clip;
            }
            return clip;
        }

        AudioSource NewSource(string sourceName)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            return src;
        }

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

            // Any slot without a clip falls back to Resources/Audio: the mural's own ambient
            // bed (Mx_Ambient) and the shared Detect / Lost / Confirm sounds.
            sfxDetect = WithClip(sfxDetect, "SFX_Detect", "Audio/Detect", false);
            sfxLost = WithClip(sfxLost, "SFX_Lost", "Audio/Lost", false);
            sfxConfirm = WithClip(sfxConfirm, "SFX_Confirm", "Audio/Confirm", false);
            ambientBed = WithClip(ambientBed, "Ambient_Bed", "Audio/" + muralSlot + "_Ambient", true);
        }

        AudioSource WithClip(AudioSource src, string slotName, string resource, bool loop)
        {
            if (src != null && src.clip != null) return src;
            var clip = Resources.Load<AudioClip>(resource);
            if (clip == null) return src;
            if (src == null)
            {
                var go = new GameObject(slotName);
                go.transform.SetParent(transform, false);
                src = go.AddComponent<AudioSource>();
            }
            src.clip = clip;
            src.loop = loop;
            src.playOnAwake = false;
            src.spatialBlend = 0f;              // 2D: always audible, whatever the mural's scale
            src.volume = loop ? 0.4f : 0.85f;
            return src;
        }

        AudioSource FindSlot(string slotName)
        {
            var t = transform.Find(slotName);
            return t != null ? t.GetComponent<AudioSource>() : null;
        }
    }
}
