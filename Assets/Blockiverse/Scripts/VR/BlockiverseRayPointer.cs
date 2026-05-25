using UnityEngine;
using UnityEngine.Rendering;

namespace Blockiverse.VR
{
    public sealed class BlockiverseRayPointer : MonoBehaviour
    {
        [SerializeField] Transform rayOrigin;
        [SerializeField] LineRenderer pointerLine;
        [SerializeField] BlockiverseControllerAnchor trackingSource;
        [SerializeField] LayerMask interactionLayers = Physics.DefaultRaycastLayers;
        [SerializeField] float maxDistance = 5.0f;

        BlockiverseHighlightTarget highlightedTarget;
        RaycastHit currentHit;
        bool hasHit;

        public BlockiverseHighlightTarget HighlightedTarget => highlightedTarget;
        public float MaxDistance => maxDistance;
        public bool HasHit => hasHit;

        public void Configure(
            Transform origin,
            LineRenderer lineRenderer,
            LayerMask layerMask,
            float distance,
            BlockiverseControllerAnchor controllerAnchor = null)
        {
            rayOrigin = origin;
            pointerLine = lineRenderer;
            trackingSource = controllerAnchor != null ? controllerAnchor : trackingSource;
            interactionLayers = layerMask;
            maxDistance = Mathf.Max(0.01f, distance);
            ConfigureLineRenderer();
            RefreshTrackingSource();
        }

        public void Refresh()
        {
            RefreshTrackingSource();

            if (trackingSource == null || !trackingSource.IsTracked)
            {
                hasHit = false;
                SetHighlightedTarget(null);
                SetPointerLineVisible(false);
                return;
            }

            Transform origin = rayOrigin != null ? rayOrigin : transform;
            Vector3 start = origin.position;
            Vector3 direction = origin.forward.sqrMagnitude > Mathf.Epsilon ? origin.forward.normalized : Vector3.forward;
            Vector3 end = start + direction * maxDistance;
            BlockiverseHighlightTarget hitTarget = null;
            hasHit = false;

            if (Physics.Raycast(
                    start,
                    direction,
                    out RaycastHit hit,
                    maxDistance,
                    interactionLayers,
                    QueryTriggerInteraction.Collide))
            {
                end = hit.point;
                currentHit = hit;
                hasHit = true;
                hitTarget = hit.collider.GetComponentInParent<BlockiverseHighlightTarget>();
            }

            SetHighlightedTarget(hitTarget);
            SetPointerLineVisible(true);
            UpdateLine(start, end);
        }

        public bool TryGetHit(out RaycastHit hit)
        {
            hit = currentHit;
            return hasHit;
        }

        void Awake()
        {
            if (rayOrigin == null)
                rayOrigin = transform;

            if (pointerLine == null)
                pointerLine = GetComponentInChildren<LineRenderer>(true);

            RefreshTrackingSource();
            ConfigureLineRenderer();
        }

        void OnEnable()
        {
            ConfigureLineRenderer();
            Refresh();
        }

        void OnDisable()
        {
            hasHit = false;
            SetHighlightedTarget(null);
            SetPointerLineVisible(false);
        }

        void Update()
        {
            Refresh();
        }

        void SetHighlightedTarget(BlockiverseHighlightTarget target)
        {
            if (highlightedTarget == target)
                return;

            if (highlightedTarget != null)
                highlightedTarget.SetHighlighted(false);

            highlightedTarget = target;

            if (highlightedTarget != null)
                highlightedTarget.SetHighlighted(true);
        }

        void ConfigureLineRenderer()
        {
            if (pointerLine == null)
                return;

            pointerLine.useWorldSpace = true;
            pointerLine.positionCount = 2;
            pointerLine.shadowCastingMode = ShadowCastingMode.Off;
            pointerLine.receiveShadows = false;
        }

        void RefreshTrackingSource()
        {
            if (trackingSource == null)
                trackingSource = GetComponentInParent<BlockiverseControllerAnchor>();
        }

        void UpdateLine(Vector3 start, Vector3 end)
        {
            if (pointerLine == null)
                return;

            pointerLine.SetPosition(0, start);
            pointerLine.SetPosition(1, end);
        }

        void SetPointerLineVisible(bool visible)
        {
            if (pointerLine != null)
                pointerLine.enabled = visible;
        }
    }
}
