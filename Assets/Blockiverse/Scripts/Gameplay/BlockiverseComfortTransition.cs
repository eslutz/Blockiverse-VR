using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseComfortTransition : MonoBehaviour
    {
        public const float DefaultFadeSeconds = 0.12f;
        public const float DefaultHoldSeconds = 0.04f;

        [SerializeField] BlockiverseHudToolkitSurface hudSurface;
        [SerializeField] float fadeSeconds = DefaultFadeSeconds;
        [SerializeField] float holdSeconds = DefaultHoldSeconds;

        Coroutine transitionRoutine;

        public bool IsTransitioning => transitionRoutine != null;
        public BlockiverseHudToolkitSurface HudSurface => hudSurface;

        public void Configure(BlockiverseHudToolkitSurface targetHudSurface, float targetFadeSeconds = DefaultFadeSeconds, float targetHoldSeconds = DefaultHoldSeconds)
        {
            hudSurface = targetHudSurface;
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
            BlockiverseHudToolkitSurface surface = EnsureFadeSurface();
            if (surface == null)
            {
                ApplyRigMove(rig, position, yawDegrees);
                transitionRoutine = null;
                yield break;
            }

            surface.SetVisible(true);
            yield return FadeTo(surface, 1.0f);
            ApplyRigMove(rig, position, yawDegrees);

            if (holdSeconds > 0.0f)
                yield return new WaitForSecondsRealtime(holdSeconds);

            yield return FadeTo(surface, 0.0f);
            transitionRoutine = null;
        }

        IEnumerator FadeTo(BlockiverseHudToolkitSurface surface, float targetAlpha)
        {
            float startAlpha = targetAlpha > 0.5f ? 0.0f : 1.0f;
            if (fadeSeconds <= 0.0f)
            {
                surface.SetComfortFade(targetAlpha);
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                surface.SetComfortFade(Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / fadeSeconds)));
                yield return null;
            }

            surface.SetComfortFade(targetAlpha);
        }

        BlockiverseHudToolkitSurface EnsureFadeSurface()
        {
            if (hudSurface != null)
                return hudSurface;

            hudSurface = FindAnyObjectByType<BlockiverseHudToolkitSurface>(FindObjectsInactive.Include);
            if (hudSurface != null)
                return hudSurface;

            GameObject surfaceObject = new("Comfort Fade UI Toolkit Surface");
            UIDocument document = surfaceObject.AddComponent<UIDocument>();
            hudSurface = surfaceObject.AddComponent<BlockiverseHudToolkitSurface>();
            hudSurface.Configure(document);
            hudSurface.SetVisible(true);
            hudSurface.SetComfortFade(0.0f);
            return hudSurface;
        }

        static void ApplyRigMove(Transform rig, Vector3 position, float yawDegrees)
        {
            rig.SetPositionAndRotation(position, Quaternion.Euler(0.0f, yawDegrees, 0.0f));
        }
    }
}
