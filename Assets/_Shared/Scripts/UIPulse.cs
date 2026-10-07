using UnityEngine;

namespace ARMurals
{
    /// <summary>Slow breathing pulse for the scanning reticle. Placeholder polish; C may replace it.</summary>
    public class UIPulse : MonoBehaviour
    {
        [SerializeField] float period = 1.8f;
        [SerializeField] float scaleAmount = 0.04f;
        [SerializeField] CanvasGroup fade;
        [SerializeField] float minAlpha = 0.45f;

        Vector3 baseScale;

        void Awake() => baseScale = transform.localScale;

        void OnEnable()
        {
            transform.localScale = baseScale;
            if (fade != null) fade.alpha = 1f;
        }

        void Update()
        {
            float k = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f / Mathf.Max(period, 0.1f));
            transform.localScale = baseScale * (1f + scaleAmount * k);
            if (fade != null) fade.alpha = Mathf.Lerp(minAlpha, 1f, k);
        }
    }
}
