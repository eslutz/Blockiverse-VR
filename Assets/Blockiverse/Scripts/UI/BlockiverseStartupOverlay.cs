using System.Collections;
using Blockiverse.VR;
using UnityEngine;

namespace Blockiverse.UI
{
    public sealed class BlockiverseStartupOverlay : MonoBehaviour
    {
        [SerializeField] Canvas targetCanvas;
        [SerializeField] BlockiverseWorldSpacePanelPresenter presenter;
        [SerializeField] float hideAfterSeconds = 2.25f;
        [SerializeField] bool hideAutomatically = true;

        Coroutine hideRoutine;

        public bool IsVisible => targetCanvas != null && targetCanvas.enabled;

        public void Configure(Canvas canvas, float delaySeconds = 2.25f, bool automaticHide = true)
        {
            Configure(canvas, null, delaySeconds, automaticHide);
        }

        public void Configure(
            Canvas canvas,
            BlockiverseWorldSpacePanelPresenter panelPresenter,
            float delaySeconds = 2.25f,
            bool automaticHide = true)
        {
            targetCanvas = canvas;
            presenter = panelPresenter;
            hideAfterSeconds = delaySeconds;
            hideAutomatically = automaticHide;
        }

        public void Hide()
        {
            EnsureCanvas();

            if (presenter != null)
                presenter.Hide();
            else if (targetCanvas != null)
                targetCanvas.enabled = false;
        }

        void Awake()
        {
            EnsureCanvas();
        }

        void OnEnable()
        {
            EnsureCanvas();
            BeginHideTimer();
        }

        void Start()
        {
            EnsureCanvas();
            BeginHideTimer();
        }

        void BeginHideTimer()
        {
            if (!Application.isPlaying || !hideAutomatically || targetCanvas == null || hideRoutine != null)
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
