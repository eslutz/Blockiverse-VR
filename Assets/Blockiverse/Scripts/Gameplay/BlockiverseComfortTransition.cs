using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseComfortTransition : MonoBehaviour
    {
        public const float DefaultFadeSeconds = 0.12f;
        public const float DefaultHoldSeconds = 0.04f;

        [SerializeField] CanvasGroup fadeGroup;
        [SerializeField] float fadeSeconds = DefaultFadeSeconds;
        [SerializeField] float holdSeconds = DefaultHoldSeconds;

        Coroutine transitionRoutine;

        public bool IsTransitioning => transitionRoutine != null;
        public CanvasGroup FadeGroup => fadeGroup;

        public void Configure(CanvasGroup targetFadeGroup, float targetFadeSeconds = DefaultFadeSeconds, float targetHoldSeconds = DefaultHoldSeconds)
        {
            fadeGroup = targetFadeGroup;
            fadeSeconds = Mathf.Max(0.0f, targetFadeSeconds);
            holdSeconds = Mathf.Max(0.0f, targetHoldSeconds);
        }

        public static bool TryMoveRigWithComfort(Transform rig, Vector3 position, float yawDegrees)
        {
            if (rig == null || !Application.isPlaying)
                return false;

            BlockiverseComfortTransition transition = rig.GetComponent<BlockiverseComfortTransition>() ??
                FindAnyObjectByType<BlockiverseComfortTransition>(FindObjectsInactive.Include);
            if (transition == null || !transition.isActiveAndEnabled)
                return false;

            transition.MoveRig(rig, position, yawDegrees);
            return true;
        }

        public void MoveRig(Transform rig, Vector3 position, float yawDegrees)
        {
            if (rig == null)
                return;

            if (!Application.isPlaying)
            {
                ApplyRigMove(rig, position, yawDegrees);
                return;
            }

            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);

            transitionRoutine = StartCoroutine(MoveRigRoutine(rig, position, yawDegrees));
        }

        IEnumerator MoveRigRoutine(Transform rig, Vector3 position, float yawDegrees)
        {
            CanvasGroup group = EnsureFadeGroup();
            if (group == null)
            {
                ApplyRigMove(rig, position, yawDegrees);
                transitionRoutine = null;
                yield break;
            }

            group.gameObject.SetActive(true);
            yield return FadeTo(group, 1.0f);
            ApplyRigMove(rig, position, yawDegrees);

            if (holdSeconds > 0.0f)
                yield return new WaitForSecondsRealtime(holdSeconds);

            yield return FadeTo(group, 0.0f);
            group.gameObject.SetActive(false);
            transitionRoutine = null;
        }

        IEnumerator FadeTo(CanvasGroup group, float targetAlpha)
        {
            float startAlpha = group.alpha;
            if (fadeSeconds <= 0.0f)
            {
                group.alpha = targetAlpha;
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / fadeSeconds));
                yield return null;
            }

            group.alpha = targetAlpha;
        }

        CanvasGroup EnsureFadeGroup()
        {
            if (fadeGroup != null)
                return fadeGroup;

            Camera targetCamera = Camera.main;
            GameObject canvasObject = new("Comfort Fade Overlay");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            if (targetCamera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = targetCamera;
                canvas.planeDistance = 0.05f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            canvas.sortingOrder = short.MaxValue;

            CanvasGroup group = canvasObject.AddComponent<CanvasGroup>();
            group.alpha = 0.0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            GameObject imageObject = new("Fade");
            imageObject.transform.SetParent(canvasObject.transform, false);
            RectTransform imageRect = imageObject.AddComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            Image image = imageObject.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;

            canvasObject.SetActive(false);
            fadeGroup = group;
            return fadeGroup;
        }

        static void ApplyRigMove(Transform rig, Vector3 position, float yawDegrees)
        {
            rig.SetPositionAndRotation(position, Quaternion.Euler(0.0f, yawDegrees, 0.0f));
        }
    }
}
