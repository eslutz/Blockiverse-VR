using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.VR
{
    [DefaultExecutionOrder(10000)]
    public sealed class BlockiverseCompositionLayerRenderScale : MonoBehaviour
    {
        const float MinimumRenderScale = 1.0f;

        [SerializeField] float renderScale = 2.0f;
        [SerializeField] Canvas sourceCanvas;
        [SerializeField] CompositionLayer compositionLayer;
        [SerializeField] TexturesExtension texturesExtension;
        [SerializeField] Camera mirrorCamera;

        RenderTexture renderTexture;
        int lastWidth;
        int lastHeight;

        public float RenderScale => renderScale;

        public void Configure(
            Canvas canvas,
            CompositionLayer layer,
            TexturesExtension extension,
            Camera canvasCamera,
            float scale)
        {
            sourceCanvas = canvas;
            compositionLayer = layer;
            texturesExtension = extension;
            mirrorCamera = canvasCamera;
            renderScale = Mathf.Max(MinimumRenderScale, scale);
        }

        void Start()
        {
            ApplyRenderScale();
        }

        void LateUpdate()
        {
            ApplyRenderScale();
        }

        void OnDisable()
        {
            ReleaseOwnedRenderTexture();
        }

        void OnDestroy()
        {
            ReleaseOwnedRenderTexture();
        }

        public void ApplyRenderScale()
        {
            ResolveReferences();
            ExcludeSourceCanvasLayerFromMainCamera();

            if (sourceCanvas == null || texturesExtension == null || mirrorCamera == null)
                return;

            bool visible = sourceCanvas.enabled && (compositionLayer == null || compositionLayer.enabled);
            mirrorCamera.enabled = visible;
            if (!visible)
                return;

            RectTransform rectTransform = sourceCanvas.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;

            ConfigureMirrorCamera(rectTransform);

            int width = Mathf.Max(1, Mathf.CeilToInt(rectTransform.rect.width * renderScale));
            int height = Mathf.Max(1, Mathf.CeilToInt(rectTransform.rect.height * renderScale));

            if (renderTexture == null || lastWidth != width || lastHeight != height || mirrorCamera.targetTexture != renderTexture)
            {
                ReleaseOwnedRenderTexture();
                renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"{gameObject.name} Composition UI",
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                renderTexture.Create();
                lastWidth = width;
                lastHeight = height;
            }

            mirrorCamera.targetTexture = renderTexture;
            texturesExtension.LeftTexture = renderTexture;
            texturesExtension.RightTexture = renderTexture;
        }

        void ConfigureMirrorCamera(RectTransform rectTransform)
        {
            if (mirrorCamera.transform.parent != sourceCanvas.transform)
                mirrorCamera.transform.SetParent(sourceCanvas.transform, false);

            mirrorCamera.transform.localScale = Vector3.one;
            mirrorCamera.transform.localPosition = new Vector3(0.0f, 0.0f, -100.0f);
            mirrorCamera.transform.localRotation = Quaternion.identity;
            mirrorCamera.gameObject.layer = sourceCanvas.gameObject.layer;
            mirrorCamera.orthographic = true;
            mirrorCamera.nearClipPlane = 0.0f;
            mirrorCamera.clearFlags = CameraClearFlags.SolidColor;
            mirrorCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.001f);
            mirrorCamera.cullingMask = 1 << sourceCanvas.gameObject.layer;

            Rect rect = rectTransform.rect;
            float canvasWidth = Mathf.Max(0.001f, Mathf.Abs(rect.width * rectTransform.lossyScale.x));
            float canvasHeight = Mathf.Max(0.001f, Mathf.Abs(rect.height * rectTransform.lossyScale.y));
            mirrorCamera.orthographicSize = canvasHeight * 0.5f;
            mirrorCamera.aspect = canvasWidth / canvasHeight;
        }

        void ResolveReferences()
        {
            if (sourceCanvas == null)
                sourceCanvas = GetComponentInChildren<Canvas>(true);

            if (compositionLayer == null)
                compositionLayer = GetComponent<CompositionLayer>();

            if (texturesExtension == null)
                texturesExtension = GetComponent<TexturesExtension>();

            if (mirrorCamera == null)
            {
                Camera[] cameras = GetComponentsInChildren<Camera>(true);
                foreach (Camera camera in cameras)
                {
                    if (camera != null && camera.gameObject.name == "CanvasCamera")
                    {
                        mirrorCamera = camera;
                        break;
                    }
                }

                if (mirrorCamera == null)
                {
                    foreach (Camera camera in cameras)
                    {
                        if (camera != null && camera != Camera.main)
                        {
                            mirrorCamera = camera;
                            break;
                        }
                    }
                }
            }
        }

        void ExcludeSourceCanvasLayerFromMainCamera()
        {
            if (sourceCanvas == null)
                return;

            Camera mainCamera = Camera.main;
            if (mainCamera == null || mainCamera == mirrorCamera)
                return;

            mainCamera.cullingMask &= ~(1 << sourceCanvas.gameObject.layer);
        }

        void ReleaseOwnedRenderTexture()
        {
            if (mirrorCamera != null && mirrorCamera.targetTexture == renderTexture)
                mirrorCamera.targetTexture = null;

            if (texturesExtension != null)
            {
                if (texturesExtension.LeftTexture == renderTexture)
                    texturesExtension.LeftTexture = null;
                if (texturesExtension.RightTexture == renderTexture)
                    texturesExtension.RightTexture = null;
            }

            if (renderTexture == null)
                return;

            renderTexture.Release();
            if (Application.isPlaying)
                Destroy(renderTexture);
            else
                DestroyImmediate(renderTexture);
            renderTexture = null;
            lastWidth = 0;
            lastHeight = 0;
        }
    }
}
