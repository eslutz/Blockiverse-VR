using System.Collections;
using UnityEngine;

namespace Blockiverse.UI
{
    public sealed class BlockiverseStartupOverlay : MonoBehaviour
    {
        [SerializeField] Canvas targetCanvas;
        [SerializeField] float hideAfterSeconds = 2.25f;
        [SerializeField] bool hideAutomatically = true;

        Coroutine hideRoutine;

        public bool IsVisible => targetCanvas != null && targetCanvas.enabled;

        public void Configure(Canvas canvas, float delaySeconds = 2.25f, bool automaticHide = true)
        {
            targetCanvas = canvas;
            hideAfterSeconds = delaySeconds;
            hideAutomatically = automaticHide;
        }

        public void Hide()
        {
            EnsureCanvas();

            if (targetCanvas != null)
                targetCanvas.enabled = false;
        }

        void Awake()
        {
            EnsureCanvas();
        }

        void OnEnable()
        {
            EnsureCanvas();

            if (!Application.isPlaying || !hideAutomatically || targetCanvas == null || !targetCanvas.enabled)
                return;

            hideRoutine = StartCoroutine(HideAfterDelay());
        }

        void OnDisable()
        {
            if (hideRoutine == null)
                return;

            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        IEnumerator HideAfterDelay()
        {
            yield return new WaitForSeconds(hideAfterSeconds);
            Hide();
            hideRoutine = null;
        }

        void EnsureCanvas()
        {
            if (targetCanvas == null)
                targetCanvas = GetComponent<Canvas>();
        }
    }
}
