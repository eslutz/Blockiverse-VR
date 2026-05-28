using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.VR
{
    public sealed class BlockiverseWorldSpacePanelPresenter : MonoBehaviour
    {
        [SerializeField] Canvas targetCanvas;
        [SerializeField] Transform headset;
        [SerializeField] float distanceMeters = 1.2f;
        [SerializeField] float horizontalOffsetMeters;
        [SerializeField] float verticalOffsetMeters = -0.1f;
        [SerializeField] float pitchDegrees;
        [SerializeField] float panelScale = 0.002f;
        [SerializeField] bool recenterOnShow = true;
        [SerializeField] bool showOnStart;

        public Canvas TargetCanvas => targetCanvas;
        public bool IsVisible => targetCanvas != null && targetCanvas.enabled;
        public bool ShowOnStart => showOnStart;

        public void Configure(
            Canvas canvas,
            Transform targetHeadset,
            float distance,
            float horizontalOffset,
            float verticalOffset,
            float pitch,
            float scale = 0.002f,
            bool recenterWhenShown = true,
            bool showWhenStarted = false)
        {
            targetCanvas = canvas;
            headset = targetHeadset;
            distanceMeters = distance;
            horizontalOffsetMeters = horizontalOffset;
            verticalOffsetMeters = verticalOffset;
            pitchDegrees = pitch;
            panelScale = scale;
            recenterOnShow = recenterWhenShown;
            showOnStart = showWhenStarted;
        }

        public void Show()
        {
            EnsureCanvas();

            if (recenterOnShow)
                Recenter();

            if (targetCanvas != null)
                targetCanvas.enabled = true;
        }

        public void Hide()
        {
            EnsureCanvas();

            if (targetCanvas != null)
                targetCanvas.enabled = false;
        }

        public void ToggleVisible()
        {
            EnsureCanvas();

            if (targetCanvas != null && targetCanvas.enabled)
                Hide();
            else
                Show();
        }

        public void Recenter()
        {
            Transform target = headset != null ? headset : Camera.main != null ? Camera.main.transform : null;

            if (target == null)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);

            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.ProjectOnPlane(target.up, Vector3.up);

            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.forward;

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 position = target.position
                + forward * Mathf.Max(0.1f, distanceMeters)
                + right * horizontalOffsetMeters
                + Vector3.up * verticalOffsetMeters;

            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(pitchDegrees, 0.0f, 0.0f));
            transform.localScale = Vector3.one * panelScale;
        }

        void Awake()
        {
            EnsureCanvas();
        }

        void Start()
        {
            if (showOnStart)
                Show();
        }

        void EnsureCanvas()
        {
            if (targetCanvas == null)
                targetCanvas = GetComponent<Canvas>();
        }
    }
}
