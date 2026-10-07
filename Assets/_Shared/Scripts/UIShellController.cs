using System;
using UnityEngine;
using UnityEngine.UI;

namespace ARMurals
{
    /// <summary>
    /// The five UI states the brief asks for. Package C replaces the visuals of this
    /// prefab; the panel names and this script's API are fixed by the conventions
    /// contract so the swap never breaks anyone's scene.
    /// </summary>
    public class UIShellController : MonoBehaviour
    {
        public enum ShellState { Start, Scanning, Experience, Info, Complete }

        [Header("Panels (names fixed: StartPanel / ScanPanel / ARControls / InfoPanel / ExitPanel)")]
        [SerializeField] GameObject startPanel;
        [SerializeField] GameObject scanPanel;
        [SerializeField] GameObject arControls;
        [SerializeField] GameObject infoPanel;
        [SerializeField] GameObject exitPanel;

        [Header("Optional wiring")]
        [SerializeField] Button beginButton;
        [SerializeField] Button infoButton;
        [SerializeField] Button infoCloseButton;
        [SerializeField] Button resetButton;
        [SerializeField] Button finishButton;
        [SerializeField] Button restartButton;
        [SerializeField] Text infoBody;

        public ShellState State { get; private set; } = ShellState.Start;

        public event Action BeginRequested;
        public event Action ResetRequested;
        public event Action RestartRequested;

        void Awake()
        {
            ResolvePanels();
            WireButtons();
            SetState(ShellState.Start);
        }

        public void SetState(ShellState next)
        {
            State = next;
            Show(startPanel, next == ShellState.Start);
            Show(scanPanel, next == ShellState.Scanning);
            Show(arControls, next == ShellState.Experience || next == ShellState.Info);
            Show(infoPanel, next == ShellState.Info);
            Show(exitPanel, next == ShellState.Complete);
        }

        // Called by ARMuralManager
        public void ShowScanning() => SetState(ShellState.Scanning);
        public void ShowExperience() => SetState(ShellState.Experience);
        public void ShowComplete() => SetState(ShellState.Complete);

        public void ToggleInfo()
        {
            SetState(State == ShellState.Info ? ShellState.Experience : ShellState.Info);
        }

        public void SetInfoText(string body)
        {
            if (infoBody != null) infoBody.text = body;
        }

        static void Show(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        void ResolvePanels()
        {
            if (startPanel == null) startPanel = Find("StartPanel");
            if (scanPanel == null) scanPanel = Find("ScanPanel");
            if (arControls == null) arControls = Find("ARControls");
            if (infoPanel == null) infoPanel = Find("InfoPanel");
            if (exitPanel == null) exitPanel = Find("ExitPanel");
        }

        GameObject Find(string n)
        {
            var t = transform.Find(n);
            return t != null ? t.gameObject : null;
        }

        void WireButtons()
        {
            if (beginButton != null) beginButton.onClick.AddListener(() => { BeginRequested?.Invoke(); ShowScanning(); });
            if (infoButton != null) infoButton.onClick.AddListener(ToggleInfo);
            if (infoCloseButton != null) infoCloseButton.onClick.AddListener(ToggleInfo);
            if (resetButton != null) resetButton.onClick.AddListener(() => ResetRequested?.Invoke());
            if (finishButton != null) finishButton.onClick.AddListener(ShowComplete);
            if (restartButton != null) restartButton.onClick.AddListener(() => { RestartRequested?.Invoke(); SetState(ShellState.Start); });
        }
    }
}
