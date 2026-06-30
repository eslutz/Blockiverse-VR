using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Blockiverse.Core;

namespace Blockiverse.VR
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9990)]
    public sealed class BlockiverseCompositionMenuCursor : MonoBehaviour
    {
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] RectTransform menuCanvas;
        [SerializeField] RectTransform cursor;
        [SerializeField] Graphic cursorGraphic;
        [SerializeField] float edgeInset = 8.0f;

        public RectTransform MenuCanvas => menuCanvas;
        public RectTransform Cursor => cursor;
        public Graphic CursorGraphic => cursorGraphic;

        public void Configure(
            BlockiverseInputRig rig,
            RectTransform canvas,
            RectTransform cursorRect,
            Graphic graphic)
        {
            inputRig = rig;
            menuCanvas = canvas;
            cursor = cursorRect;
            cursorGraphic = graphic;
            ApplyVisibility(false);
        }

        void Awake()
        {
            ResolveReferences();
            ApplyVisibility(false);
        }

        void OnEnable()
        {
            ResolveReferences();
            ApplyVisibility(false);
        }

        void LateUpdate()
        {
            TryUpdateCursor();
        }

        public bool TryUpdateCursor()
        {
            ResolveReferences();
            XRRayInteractor activeRay = ResolveActiveRay();
            if (activeRay == null ||
                !activeRay.isActiveAndEnabled ||
                !activeRay.TryGetCurrentUIRaycastResult(out RaycastResult raycastResult))
            {
                ApplyVisibility(false);
                return false;
            }

            return TryApplyRaycastResult(raycastResult);
        }

        public bool TryApplyRaycastResult(RaycastResult raycastResult)
        {
            ResolveReferences();
            if (menuCanvas == null ||
                cursor == null ||
                raycastResult.gameObject == null ||
                !IsDescendantOf(raycastResult.gameObject.transform, menuCanvas))
            {
                ApplyVisibility(false);
                return false;
            }

            Vector3 localHit = menuCanvas.InverseTransformPoint(raycastResult.worldPosition);
            Rect rect = menuCanvas.rect;
            float minX = rect.xMin + edgeInset;
            float maxX = rect.xMax - edgeInset;
            float minY = rect.yMin + edgeInset;
            float maxY = rect.yMax - edgeInset;
            cursor.anchoredPosition = new Vector2(
                Mathf.Clamp(localHit.x, minX, maxX),
                Mathf.Clamp(localHit.y, minY, maxY));
            ApplyVisibility(true);
            return true;
        }

        void ResolveReferences()
        {
            if (inputRig == null)
                inputRig = GetComponentInParent<BlockiverseInputRig>();

            if (menuCanvas == null)
            {
                Canvas canvas = GetComponentInChildren<Canvas>(includeInactive: true);
                menuCanvas = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            }

            if (cursorGraphic == null && cursor != null)
                cursorGraphic = cursor.GetComponent<Graphic>();
        }

        XRRayInteractor ResolveActiveRay()
        {
            if (inputRig == null)
                return null;

            return inputRig.ActiveToolHand == BlockiverseControllerRole.Left
                ? inputRig.LeftInteractionRay
                : inputRig.RightInteractionRay;
        }

        void ApplyVisibility(bool visible)
        {
            if (cursorGraphic != null)
                cursorGraphic.enabled = visible;

            if (cursor != null && cursor.gameObject.activeSelf != visible)
                cursor.gameObject.SetActive(visible);
        }

        static bool IsDescendantOf(Transform candidate, Transform parent)
        {
            while (candidate != null)
            {
                if (candidate == parent)
                    return true;

                candidate = candidate.parent;
            }

            return false;
        }
    }
}
