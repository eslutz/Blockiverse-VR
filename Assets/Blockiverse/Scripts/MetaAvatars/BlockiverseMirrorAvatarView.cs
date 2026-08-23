using UnityEngine;
using UnityEngine.Rendering.Universal;
#if UNITY_ANDROID && !UNITY_EDITOR
using Oculus.Avatar2;
#endif

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// The mirror "studio" (issue #340): a pocket, on its own render layer, holding a
    /// loopback copy of the local player's Meta avatar and the small camera that renders
    /// it to the mirror pane's texture.
    ///
    /// The loopback entity is a remote-style avatar (ThirdPerson view, Full manifestation
    /// — the configuration that includes legs) posed by the same stream recording the
    /// local first-person avatar already supports. No second scene render happens: the
    /// studio camera sees only this layer, and the main camera culls it.
    ///
    /// The entity itself exists only on device, like every avatar entity in this project;
    /// in the editor the mirror renders the studio backdrop color.
    /// </summary>
    public sealed class BlockiverseMirrorAvatarView : MonoBehaviour
    {
        public const int TextureSize = 512;
        const float StreamRecordRateHz = 24.0f;
        const float StreamStaleSeconds = 3.0f;
        const float PresenterSearchIntervalSeconds = 1.0f;
        const float CameraFieldOfViewDegrees = 60.0f;

        Camera studioCamera;
        RenderTexture texture;
        Transform entityRoot;
        BlockiverseMetaAvatarPresenter localPresenter;
        float nextPresenterSearchTime;
        float nextRecordTime;
        float lastStreamAppliedTime = float.NegativeInfinity;
        bool mirrorActive;

#if UNITY_ANDROID && !UNITY_EDITOR
        BlockiverseMetaAvatarEntity entity;
        ulong requestedLikenessUserId;
        float nextLikenessAttemptTime;
#endif

        public RenderTexture Texture => texture;
        public Transform StudioRoot => transform;

        /// <summary>Create the studio. All of its objects live on <paramref name="studioLayer"/>,
        /// which the caller must cull from the main camera.</summary>
        public static BlockiverseMirrorAvatarView Create(int studioLayer)
        {
            var studioObject = new GameObject("Mirror Avatar Studio");
            studioObject.layer = studioLayer;
            var view = studioObject.AddComponent<BlockiverseMirrorAvatarView>();
            view.Build(studioLayer);
            return view;
        }

        void Build(int studioLayer)
        {
            texture = new RenderTexture(TextureSize, TextureSize, 16, RenderTextureFormat.Default)
            {
                name = "Blockiverse Mirror",
                useMipMap = false,
                autoGenerateMips = false,
            };

            // The camera sits at the pane's position in studio space and looks into the
            // reflected half-space, so it shows what the mirror would.
            var cameraObject = new GameObject("Mirror Camera");
            cameraObject.layer = studioLayer;
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.SetLocalPositionAndRotation(
                new Vector3(0.0f, 0.0f, 0.0f), Quaternion.LookRotation(Vector3.back, Vector3.up));

            studioCamera = cameraObject.AddComponent<Camera>();
            studioCamera.cullingMask = 1 << studioLayer;
            studioCamera.clearFlags = CameraClearFlags.SolidColor;
            studioCamera.backgroundColor = new Color(0.16f, 0.18f, 0.21f, 1.0f);
            studioCamera.fieldOfView = CameraFieldOfViewDegrees;
            studioCamera.nearClipPlane = 0.05f;
            studioCamera.farClipPlane = 30.0f;
            studioCamera.targetTexture = texture;
            studioCamera.enabled = false;
            studioCamera.stereoTargetEye = StereoTargetEyeMask.None;

            UniversalAdditionalCameraData cameraData = studioCamera.GetUniversalAdditionalCameraData();
            if (cameraData != null)
            {
                cameraData.renderShadows = false;
                cameraData.renderPostProcessing = false;
                cameraData.antialiasing = AntialiasingMode.None;
            }

            var entityRootObject = new GameObject("Mirror Avatar Root");
            entityRootObject.layer = studioLayer;
            entityRootObject.transform.SetParent(transform, false);
            entityRoot = entityRootObject.transform;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Same staged-creation pattern as MetaHorizonAvatarProvider: inactive while the
            // creation flags are set, then activated for SDK setup. Remote-style entity —
            // posed purely by ApplyStreamData.
            var entityObject = new GameObject("Mirror Avatar Entity");
            entityObject.layer = studioLayer;
            entityObject.SetActive(false);
            entityObject.transform.SetParent(entityRoot, false);
            entity = entityObject.AddComponent<BlockiverseMetaAvatarEntity>();
            entity.ConfigurePresentation(MetaAvatarPresentationMode.RemoteThirdPerson, hideHeadForFirstPerson: false);
            entityObject.SetActive(true);
#endif
        }

        public void SetMirrorActive(bool active)
        {
            if (mirrorActive == active)
                return;

            mirrorActive = active;
            RefreshVisibility();
        }

        /// <summary>Drive one frame of the active mirror. The pose is the loopback entity
        /// root in studio space (already reflected by the caller).</summary>
        public void TickMirror(Vector3 entityStudioPosition, Quaternion entityStudioRotation)
        {
            if (!mirrorActive)
                return;

            entityRoot.SetPositionAndRotation(entityStudioPosition, entityStudioRotation);

            float now = Time.unscaledTime;
            if (localPresenter == null && now >= nextPresenterSearchTime)
            {
                nextPresenterSearchTime = now + PresenterSearchIntervalSeconds;
                localPresenter = FindLocalFirstPersonPresenter();
            }

            if (localPresenter != null && now >= nextRecordTime)
            {
                nextRecordTime = now + 1.0f / StreamRecordRateHz;
                FeedLoopbackEntity(now);
            }

            RefreshVisibility();
        }

        void FeedLoopbackEntity(float now)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (entity == null)
                return;

            if (!entity.IsCreated)
            {
                if (OvrAvatarManager.hasInstance && OvrAvatarManager.initialized)
                    entity.CreateConfiguredEntity();
                return;
            }

            EnsureLikeness();

            if (localPresenter.TryRecordLocalStream(out byte[] streamData) &&
                streamData != null && streamData.Length > 0)
            {
                entity.SetIsLocal(false);
                if (entity.ApplyStreamData(streamData))
                    lastStreamAppliedTime = now;
            }
#else
            _ = now;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void EnsureLikeness()
        {
            // Show the player's real avatar in the mirror once the platform chain has
            // resolved it; until then (or for accounts that never resolve — child policy
            // keeps the token chain from running at all) the default avatar stands in.
            if (requestedLikenessUserId != 0 ||
                Time.unscaledTime < nextLikenessAttemptTime ||
                !OvrAvatarEntitlement.AccessTokenIsValid() ||
                localPresenter == null ||
                !localPresenter.TryGetLocalMetaUserId(out ulong userId))
            {
                return;
            }

            nextLikenessAttemptTime = Time.unscaledTime + 10.0f;
            if (entity.TryLoadUserAvatar(userId))
                requestedLikenessUserId = userId;
        }
#endif

        void RefreshVisibility()
        {
            bool streamFresh = Time.unscaledTime - lastStreamAppliedTime < StreamStaleSeconds;

            if (studioCamera != null)
                studioCamera.enabled = mirrorActive;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Hide via view flags, never by deactivating the entity's GameObject: the SDK
            // only advances an inactive entity's load pipeline once reactivated, so a
            // deactivated not-yet-loaded entity could never become showable.
            entity?.SetPresentationVisible(mirrorActive && streamFresh);
#else
            _ = streamFresh;
#endif
        }

        static BlockiverseMetaAvatarPresenter FindLocalFirstPersonPresenter()
        {
            foreach (BlockiverseMetaAvatarPresenter presenter in FindObjectsByType<BlockiverseMetaAvatarPresenter>())
            {
                if (presenter.PresentationMode == MetaAvatarPresentationMode.LocalFirstPerson)
                    return presenter;
            }

            return null;
        }

        void OnDestroy()
        {
            if (studioCamera != null)
                studioCamera.targetTexture = null;

            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
                texture = null;
            }
        }
    }
}
