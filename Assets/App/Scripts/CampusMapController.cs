using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ARMurals
{
    /// <summary>
    /// The start screen: a campus map with one pin per mural and a tab per building
    /// level. ALL shows every mural; L1/L2/L3 show only that level's murals (and that
    /// level's map, if one is set). Tapping a pin opens a card with the mural's photo,
    /// location and story; SCAN THIS MURAL hands over to the scanning screen with that
    /// mural as the hint. Pins turn green once a mural has been found.
    ///
    /// Sits on UI_Shell next to UIShellController and only drives the shell through its
    /// public API, so the shell itself stays untouched.
    /// </summary>
    public class CampusMapController : MonoBehaviour
    {
        [SerializeField] UIShellController shell;
        [SerializeField] MuralDirectory directory;
        [SerializeField] List<MuralExperienceBase> experiences = new List<MuralExperienceBase>();
        [SerializeField] ARMuralManager manager;

        [Header("Splash (first screen on launch)")]
        [SerializeField] GameObject splash;
        [SerializeField] Button splashStartButton;
        [SerializeField] Text splashFacts;

        [Header("Map")]
        [SerializeField] RawImage mapImage;
        [SerializeField] AspectRatioFitter mapFitter;
        [SerializeField] RectTransform pinLayer;
        [SerializeField] Button pinTemplate;
        [SerializeField] Text progressLabel;
        [SerializeField] Button scanAnyButton;

        [Header("Level tabs (element 0 = ALL, then level 1, 2, ...)")]
        [SerializeField] List<Button> levelTabs = new List<Button>();

        [Header("Mural card")]
        [SerializeField] GameObject card;
        [SerializeField] RawImage cardPhoto;
        [SerializeField] Text cardTitle;
        [SerializeField] Text cardLocation;
        [SerializeField] Text cardBlurb;
        [SerializeField] Button cardScanButton;
        [SerializeField] Button cardCloseButton;

        [Header("Scanning screen")]
        [SerializeField] RawImage scanThumb;
        [SerializeField] Text scanHint;
        [SerializeField] Text scanSub;
        [SerializeField] Button backToMapButton;

        [Header("Look")]
        [SerializeField] Color pinIdle = new Color(0.988f, 0.737f, 0.251f, 1f);
        [SerializeField] Color pinSelected = new Color(0.90f, 0.47f, 0.10f, 1f);   // deeper orange, visible on the white map
        [SerializeField] Color pinFound = new Color(0.40f, 0.82f, 0.55f, 1f);
        [SerializeField] Color tabOn = new Color(0.988f, 0.737f, 0.251f, 1f);
        [SerializeField] Color tabOff = new Color(1f, 1f, 1f, 0f);
        [SerializeField] Color tabTextOn = new Color(0.063f, 0.071f, 0.110f, 1f);
        [SerializeField] Color tabTextOff = new Color(0.949f, 0.945f, 0.925f, 1f);

        readonly List<Button> pins = new List<Button>();
        readonly HashSet<string> found = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        MuralDirectory.Entry selected;
        int level;                                  // 0 = all levels
        UIShellController.ShellState lastState;

        void Start()
        {
            if (directory == null) { Debug.LogWarning("[CampusMap] No MuralDirectory assigned."); return; }

            BuildPins();
            for (int i = 0; i < levelTabs.Count; i++)
            {
                int tab = i;
                if (levelTabs[i] != null) levelTabs[i].onClick.AddListener(() => ShowLevel(tab));
            }
            if (scanAnyButton != null) scanAnyButton.onClick.AddListener(() => BeginScan(null));
            if (cardScanButton != null) cardScanButton.onClick.AddListener(() => BeginScan(selected));
            if (cardCloseButton != null) cardCloseButton.onClick.AddListener(() => Select(null));
            if (backToMapButton != null) backToMapButton.onClick.AddListener(BackToMap);
            if (manager != null) manager.MuralDetected += MarkFound;

            if (splash != null) splash.SetActive(true);
            if (splashStartButton != null) splashStartButton.onClick.AddListener(() => splash.SetActive(false));
            if (splashFacts != null)
                splashFacts.text = directory.murals.Count + " murals  ·  " + directory.levelCount + " levels  ·  about 15 minutes";

            ShowLevel(0);
            if (shell != null) lastState = shell.State;
        }

        void Update()
        {
            // Mark murals as found the moment their experience goes live.
            foreach (var e in experiences)
                if (e != null && e.IsActive && found.Add(e.MuralSlot))
                    RefreshPins();

            // Whenever the shell comes back to the start screen (BACK TO START), start
            // from a clean map rather than a half-open card.
            if (shell != null && shell.State != lastState)
            {
                if (shell.State == UIShellController.ShellState.Start) Select(null);
                lastState = shell.State;
            }
        }

        void OnDestroy()
        {
            if (manager != null) manager.MuralDetected -= MarkFound;
        }

        void MarkFound(string slot)
        {
            // Reference image names may carry a suffix ("M2_target"); keep the slot part.
            var entry = directory.murals.Find(e => slot.StartsWith(e.slot, System.StringComparison.OrdinalIgnoreCase));
            if (entry != null && found.Add(entry.slot)) RefreshPins();
        }

        void BuildPins()
        {
            if (pinTemplate == null || pinLayer == null) return;
            pinTemplate.gameObject.SetActive(false);

            for (int i = 0; i < directory.murals.Count; i++)
            {
                var entry = directory.murals[i];
                var pin = Instantiate(pinTemplate, pinLayer);
                pin.name = "Pin_" + entry.slot;

                var rt = (RectTransform)pin.transform;
                rt.anchorMin = rt.anchorMax = entry.mapPosition;
                rt.anchoredPosition = Vector2.zero;

                SetPinText(pin, "Number", (i + 1).ToString());
                pin.onClick.AddListener(() => Select(entry));
                pins.Add(pin);
            }
        }

        void ShowLevel(int next)
        {
            level = next;

            var map = directory.MapFor(level);
            if (mapImage != null && map != null)
            {
                mapImage.texture = map;
                if (mapFitter != null) mapFitter.aspectRatio = (float)map.width / map.height;
            }

            for (int i = 0; i < levelTabs.Count; i++)
            {
                if (levelTabs[i] == null) continue;
                bool on = i == level;
                levelTabs[i].targetGraphic.color = on ? tabOn : tabOff;
                var label = levelTabs[i].GetComponentInChildren<Text>();
                if (label != null) label.color = on ? tabTextOn : tabTextOff;
            }

            // A card for a mural on another level makes no sense once that level is hidden.
            if (selected != null && !OnShownLevel(selected)) Select(null);
            else RefreshPins();
        }

        bool OnShownLevel(MuralDirectory.Entry entry) => level == 0 || entry.level == level;

        void Select(MuralDirectory.Entry entry)
        {
            selected = entry;
            if (card != null) card.SetActive(entry != null);
            if (scanAnyButton != null) scanAnyButton.gameObject.SetActive(entry == null);

            if (entry != null)
            {
                if (cardTitle != null) cardTitle.text = entry.title.ToUpperInvariant();
                if (cardLocation != null) cardLocation.text = "Level " + entry.level + "  ·  " + entry.location;
                if (cardBlurb != null) cardBlurb.text = entry.blurb;
                ShowPhoto(cardPhoto, entry.photo, 1f);
            }
            RefreshPins();
        }

        void BeginScan(MuralDirectory.Entry entry)
        {
            if (entry != null)
            {
                if (scanHint != null) scanHint.text = "FIND " + entry.title.ToUpperInvariant();
                if (scanSub != null) scanSub.text = "Level " + entry.level + "  ·  " + entry.location +
                                                    "\nHold steady and fill the frame with the artwork";
                ShowPhoto(scanThumb, entry.photo, 0.9f);
            }
            else
            {
                if (scanHint != null) scanHint.text = "FIND A MURAL";
                if (scanSub != null) scanSub.text = "Hold steady and fill the frame with the artwork";
                ShowPhoto(scanThumb, null, 0f);
            }

            if (shell != null) shell.ShowScanning();
        }

        void BackToMap()
        {
            if (shell != null) shell.SetState(UIShellController.ShellState.Start);
        }

        void RefreshPins()
        {
            for (int i = 0; i < pins.Count; i++)
            {
                var entry = directory.murals[i];
                pins[i].gameObject.SetActive(OnShownLevel(entry));

                // On ALL, say which level each mural is on; on a level tab that is obvious.
                SetPinText(pins[i], "Name", level == 0 ? entry.title + "  ·  L" + entry.level : entry.title);

                var img = pins[i].targetGraphic;
                if (img == null) continue;
                img.color = entry == selected ? pinSelected
                          : found.Contains(entry.slot) ? pinFound
                          : pinIdle;
                pins[i].transform.localScale = entry == selected ? Vector3.one * 1.2f : Vector3.one;
            }
            RefreshProgress();
        }

        void RefreshProgress()
        {
            if (progressLabel == null || directory == null) return;
            progressLabel.text = found.Count + " / " + directory.murals.Count + " found";
        }

        static void SetPinText(Button pin, string child, string text)
        {
            var t = pin.transform.Find(child);
            if (t != null && t.TryGetComponent<Text>(out var label)) label.text = text;
        }

        /// <summary>Show a photo at its own aspect ratio inside the image's box, or hide the image.</summary>
        static void ShowPhoto(RawImage target, Texture2D photo, float alpha)
        {
            if (target == null) return;
            target.gameObject.SetActive(photo != null);
            if (photo == null) return;

            target.texture = photo;
            var c = target.color; c.a = alpha; target.color = c;

            if (target.TryGetComponent<AspectRatioFitter>(out var fitter))
                fitter.aspectRatio = (float)photo.width / photo.height;
        }
    }
}
