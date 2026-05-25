using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseRayPointer : MonoBehaviour
    {
        [SerializeField] Transform rayOrigin;
        [SerializeField] LineRenderer pointerLine;
        [SerializeField] LayerMask interactionLayers = Physics.DefaultRaycastLayers;
        [SerializeField] float maxDistance = 5.0f;

        BlockiverseHighlightTarget highlightedTarget;

        public BlockiverseHighlightTarget HighlightedTarget => highlightedTarget;
        public float MaxDistance => maxDistance;

        public void Configure(Transform origin, LineRenderer lineRenderer, LayerMask layerMask, float distance)
        {
            rayOrigin = origin;
            pointerLine = lineRenderer;
            interactionLayers = layerMask;
            maxDistance = Mathf.Max(0.01f, distance);
            ConfigureLineRenderer();
        }

        public void Refresh()
        {
            Transform origin = rayOrigin != null ? rayOrigin : transform;
            Vector3 start = origin.position;
            Vector3 direction = origin.forward.sqrMagnitude > Mathf.Epsilon ? origin.forward.normalized : Vector3.forward;
            Vector3 end = start + direction * maxDistance;
            BlockiverseHighlightTarget hitTarget = null;

            if (Physics.Raycast(
                    start,
                    direction,
                    out RaycastHit hit,
                    maxDistance,
                    interactionLayers,
                    QueryTriggerInteraction.Collide))
            {
                end = hit.point;
                hitTarget = hit.collider.GetComponentInParent<BlockiverseHighlightTarget>();
            }

            SetHighlightedTarget(hitTarget);
            UpdateLine(start, end);
        }

        void Awake()
        {
            if (rayOrigin == null)
                rayOrigin = transform;

            if (pointerLine == null)
                pointerLine = GetComponentInChildren<LineRenderer>(true);

            ConfigureLineRenderer();
        }

        void OnEnable()
        {
            ConfigureLineRenderer();
            Refresh();
        }

        void OnDisable()
        {
            SetHighlightedTarget(null);
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
        }

        void UpdateLine(Vector3 start, Vector3 end)
        {
            if (pointerLine == null)
                return;

            pointerLine.SetPosition(0, start);
            pointerLine.SetPosition(1, end);
        }
    }
}
