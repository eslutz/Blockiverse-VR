using Oculus.Avatar2;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// Configures and owns an <see cref="OvrAvatarEntity"/> for Blockiverse VR.
    ///
    /// Local first-person mode: FirstPerson view with Full manifestation — the player sees
    /// their own hands, arms, and torso (the SDK's first-person geometry has no head and no
    /// legs by design; there is no supported way to show the local player their own legs).
    /// Remote third-person mode: ThirdPerson view with Full manifestation — the complete
    /// body including legs.
    ///
    /// The entity transform is the SDK's tracking-space root. It must stay at an identity
    /// local pose under the rig root (local player) or the network player object (remote),
    /// both of which represent the floor-level tracking origin. It must never be moved to
    /// the head: the SDK positions every joint relative to this root.
    ///
    /// Lifecycle:
    ///   1. MetaHorizonAvatarProvider creates the entity object inactive.
    ///   2. ConfigurePresentation() stages creation flags (view/manifestation/features).
    ///   3. EnsureInputManager() wires the rig's avatar input manager.
    ///   4. The provider activates the object so Awake() runs SDK setup.
    ///   5. CreateConfiguredEntity() creates the native entity with staged flags and input.
    ///   6. TryLoadUserAvatar(userId) starts the CDN load once platform auth resolves.
    /// </summary>
    public sealed class BlockiverseMetaAvatarEntity : OvrAvatarEntity
    {
        bool loadEventsSubscribed;

        // Disable the default CreateEntity-on-Awake behavior so MetaHorizonAvatarProvider can
        // set _creationInfo flags and wire the InputManager before the entity is created.
        protected override bool CreateEntityOnAwake => false;

        /// <summary>A model (default or user) is loaded and drawable. Policy about *which*
        /// model is acceptable to present lives in the provider, not here.</summary>
        public bool IsRenderableReady =>
            IsCreated &&
            CurrentState >= AvatarState.DefaultAvatar &&
            !IsApplyingModels;

        /// <summary>True from TryLoadUserAvatar until the SDK reports success or failure.</summary>
        public bool UserAvatarLoadInFlight { get; private set; }

        /// <summary>True when the most recent user-avatar load attempt failed.</summary>
        public bool UserAvatarLoadFailed { get; private set; }

        /// <summary>The user id handed to the most recent TryLoadUserAvatar call.</summary>
        public ulong RequestedUserId { get; private set; }

        /// <summary>
        /// Set presentation flags. View/manifestation/features only apply to the native
        /// entity when staged before creation; view and manifestation can also be switched
        /// on an already-created entity.
        /// </summary>
        public void ConfigurePresentation(MetaAvatarPresentationMode mode, bool hideHeadForFirstPerson)
        {
            // hideHeadForFirstPerson is retained for interface stability but unused: the
            // SDK's FirstPerson view geometry has no head at all.
            _ = hideHeadForFirstPerson;

            bool isLocal = mode == MetaAvatarPresentationMode.LocalFirstPerson;

            _creationInfo.renderFilters.viewFlags = isLocal
                ? CAPI.ovrAvatar2EntityViewFlags.FirstPerson
                : CAPI.ovrAvatar2EntityViewFlags.ThirdPerson;
            _creationInfo.renderFilters.manifestationFlags = CAPI.ovrAvatar2EntityManifestationFlags.Full;
            _creationInfo.renderFilters.quality = isLocal
                ? CAPI.ovrAvatar2EntityQuality.Standard
                : CAPI.ovrAvatar2EntityQuality.Light;

            if (!IsCreated)
            {
                // Local avatars animate from live tracking (Preset_Default includes the
                // Animation feature); remote avatars are posed purely by ApplyStreamData.
                _creationInfo.features = isLocal
                    ? CAPI.ovrAvatar2EntityFeatures.Preset_Default
                    : CAPI.ovrAvatar2EntityFeatures.Preset_Remote;
            }

            SetIsLocal(isLocal);
            if (IsCreated)
            {
                SetActiveView(_creationInfo.renderFilters.viewFlags);
                SetActiveManifestation(_creationInfo.renderFilters.manifestationFlags);
            }
        }

        public bool CreateConfiguredEntity(OvrAvatarInputManagerBehavior inputManager = null)
        {
            EnsureInputManager(inputManager);

            if (IsCreated)
                return true;

            if (!OvrAvatarManager.hasInstance || !OvrAvatarManager.initialized)
                return false;

            SubscribeLoadEvents();
            CreateEntity();
            return IsCreated;
        }

        public void EnsureInputManager(OvrAvatarInputManagerBehavior inputManager = null)
        {
            OvrAvatarInputManagerBehavior resolvedInputManager = inputManager ?? ResolveInputManager();
            if (resolvedInputManager != null)
                SetInputManager(resolvedInputManager);
        }

        public bool TryLoadUserAvatar(ulong userId)
        {
            if (!IsCreated || userId == 0)
                return false;

            SubscribeLoadEvents();
            RequestedUserId = userId;
            UserAvatarLoadInFlight = true;
            UserAvatarLoadFailed = false;
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

        void SubscribeLoadEvents()
        {
            if (loadEventsSubscribed)
                return;

            loadEventsSubscribed = true;
            OnUserAvatarLoadedEvent.AddListener(HandleUserAvatarLoaded);
            OnLoadFailedEvent.AddListener(HandleLoadFailed);
        }

        void HandleUserAvatarLoaded(OvrAvatarEntity entity)
        {
            UserAvatarLoadInFlight = false;
            UserAvatarLoadFailed = false;
        }

        void HandleLoadFailed(OvrAvatarEntity entity, CAPI.ovrAvatar2LoadRequestInfo requestInfo)
        {
            // Any failed load request while a user-avatar load is pending ends that attempt;
            // the provider owns retry policy and backoff.
            if (!UserAvatarLoadInFlight)
                return;

            UserAvatarLoadInFlight = false;
            UserAvatarLoadFailed = true;
        }

        OvrAvatarInputManagerBehavior ResolveInputManager()
        {
            return GetComponent<OvrAvatarInputManagerBehavior>()
                ?? GetComponentInParent<OvrAvatarInputManagerBehavior>(true)
                ?? GetComponentInChildren<OvrAvatarInputManagerBehavior>(true);
        }
    }
}
