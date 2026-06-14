using Oculus.Avatar2;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// Configures and owns an <see cref="OvrAvatarEntity"/> for Blockiverse VR.
    ///
    /// Local first-person mode: renders hands + clothing in first-person view only.
    /// The entity uses the native OVR Plugin for controller/HMD pose tracking so the
    /// avatar hands track the Quest controllers.
    ///
    /// Lifecycle:
    ///   1. MetaHorizonAvatarProvider creates the entity object inactive.
    ///   2. ConfigurePresentation() stages creation flags and first-person visibility.
    ///   3. EnsureInputManager() wires the parent rig's avatar input manager.
    ///   4. The provider activates the object so Awake() runs SDK setup.
    ///   5. CreateConfiguredEntity() creates the native entity with staged flags and input.
    ///   6. MetaHorizonAvatarProvider calls TryLoadUserAvatar(userId) after the Meta Platform
    ///      entitlement callback returns.
    /// </summary>
    public sealed class BlockiverseMetaAvatarEntity : OvrAvatarEntity
    {
        MetaAvatarTrackingSources trackingSources = MetaAvatarTrackingSources.Empty;
        bool hideFirstPersonHead;

        // Disable the default CreateEntity-on-Awake behavior so MetaHorizonAvatarProvider can
        // set _creationInfo flags and wire the InputManager before the entity is created.
        protected override bool CreateEntityOnAwake => false;

        public bool IsRenderableReady =>
            IsCreated &&
            CurrentState >= AvatarState.DefaultAvatar &&
            !IsApplyingModels &&
            !IsPendingAvatar;

        /// <summary>
        /// Set presentation flags before the SDK entity is created.
        /// </summary>
        public void ConfigurePresentation(MetaAvatarPresentationMode mode, bool hideHeadForFirstPerson)
        {
            bool isLocal = mode == MetaAvatarPresentationMode.LocalFirstPerson;
            hideFirstPersonHead = isLocal && hideHeadForFirstPerson;

            if (isLocal)
            {
                _creationInfo.renderFilters.viewFlags =
                    CAPI.ovrAvatar2EntityViewFlags.FirstPerson;
                _creationInfo.renderFilters.manifestationFlags =
                    CAPI.ovrAvatar2EntityManifestationFlags.Hands;
                _creationInfo.renderFilters.quality =
                    CAPI.ovrAvatar2EntityQuality.Standard;
            }
            else
            {
                _creationInfo.renderFilters.viewFlags =
                    CAPI.ovrAvatar2EntityViewFlags.ThirdPerson;
                _creationInfo.renderFilters.manifestationFlags =
                    CAPI.ovrAvatar2EntityManifestationFlags.Half;
                _creationInfo.renderFilters.quality =
                    CAPI.ovrAvatar2EntityQuality.Light;
            }

            SetIsLocal(isLocal);
            if (IsCreated)
            {
                SetActiveView(_creationInfo.renderFilters.viewFlags);
                SetActiveManifestation(_creationInfo.renderFilters.manifestationFlags);
            }

            ApplyFirstPersonVisibility();
        }

        public bool CreateConfiguredEntity(OvrAvatarInputManagerBehavior inputManager = null)
        {
            EnsureInputManager(inputManager);

            if (IsCreated)
            {
                ApplyFirstPersonVisibility();
                return true;
            }

            if (!OvrAvatarManager.hasInstance || !OvrAvatarManager.initialized)
                return false;

            CreateEntity();

            ApplyFirstPersonVisibility();
            return IsCreated;
        }

        public void EnsureInputManager(OvrAvatarInputManagerBehavior inputManager = null)
        {
            OvrAvatarInputManagerBehavior resolvedInputManager = inputManager ?? ResolveInputManager();
            if (resolvedInputManager != null)
                SetInputManager(resolvedInputManager);
        }

        public void SetTrackingSources(MetaAvatarTrackingSources sources)
        {
            trackingSources = sources;
            SetTrackingSourcesFromTransforms();
        }

        public bool TryLoadUserAvatar(ulong userId)
        {
            if (!IsCreated || userId == 0)
                return false;

            _userId = userId;
            LoadUser();
            return true;
        }

        public bool TryLoadPresetAvatar(string presetPath)
        {
            if (!IsCreated || string.IsNullOrWhiteSpace(presetPath))
                return false;

            return LoadAssets(new[] { presetPath }, AssetSource.Zip);
        }

        public void SetTrackingSourcesFromTransforms()
        {
            if (trackingSources.Head != null)
                transform.SetPositionAndRotation(trackingSources.Head.position, trackingSources.Head.rotation);
        }

        void ApplyFirstPersonVisibility()
        {
            // The avatar's "Head" child should be hidden in first-person so the player
            // doesn't see their own avatar face clipping into the camera.
            Transform head = transform.Find("Head");
            if (head != null)
                head.gameObject.SetActive(!hideFirstPersonHead);
        }

        OvrAvatarInputManagerBehavior ResolveInputManager()
        {
            return GetComponent<OvrAvatarInputManagerBehavior>()
                ?? GetComponentInParent<OvrAvatarInputManagerBehavior>(true)
                ?? GetComponentInChildren<OvrAvatarInputManagerBehavior>(true);
        }
    }
}
