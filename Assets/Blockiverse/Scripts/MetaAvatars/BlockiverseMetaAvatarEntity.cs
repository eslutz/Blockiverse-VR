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

        /// <summary>A CDN/preset avatar (not the generic default) is loaded and drawable.
        /// Poll this alongside the events: the SDK's OnUserAvatarLoadedEvent fires only on
        /// the FIRST transition into the UserAvatar state, so a second successful load on
        /// the same entity completes without any event.</summary>
        public bool HasUserAvatarModel => CurrentState == AvatarState.UserAvatar;

        bool presentationVisible = true;
        MetaAvatarPresentationMode configuredMode = MetaAvatarPresentationMode.RemoteThirdPerson;

        /// <summary>
        /// Set presentation flags. View/manifestation/features apply to the native entity
        /// only when staged before creation: this entity loads exactly one view's geometry
        /// (per its render filters), so switching mode on a live entity would select a view
        /// with nothing loaded and the avatar would silently vanish. A post-creation call
        /// with the SAME mode is a harmless no-op; a different mode is refused.
        /// </summary>
        public void ConfigurePresentation(MetaAvatarPresentationMode mode, bool hideHeadForFirstPerson)
        {
            // hideHeadForFirstPerson is retained for interface stability but unused: the
            // SDK's FirstPerson view geometry has no head at all.
            _ = hideHeadForFirstPerson;

            bool isLocal = mode == MetaAvatarPresentationMode.LocalFirstPerson;

            if (IsCreated)
            {
                if (mode != configuredMode)
                {
                    Debug.LogWarning(
                        $"[BlockiverseMetaAvatarEntity] Refusing to switch presentation mode from {configuredMode} " +
                        $"to {mode} on a created entity: only {configuredMode}'s view geometry is loaded.", this);
                }
                return;
            }

            configuredMode = mode;
            _creationInfo.renderFilters.viewFlags = isLocal
                ? CAPI.ovrAvatar2EntityViewFlags.FirstPerson
                : CAPI.ovrAvatar2EntityViewFlags.ThirdPerson;
            _creationInfo.renderFilters.manifestationFlags = CAPI.ovrAvatar2EntityManifestationFlags.Full;
            _creationInfo.renderFilters.quality = isLocal
                ? CAPI.ovrAvatar2EntityQuality.Standard
                : CAPI.ovrAvatar2EntityQuality.Light;
            // Local avatars animate from live tracking (Preset_Default includes the
            // Animation feature); remote avatars are posed purely by ApplyStreamData.
            _creationInfo.features = isLocal
                ? CAPI.ovrAvatar2EntityFeatures.Preset_Default
                : CAPI.ovrAvatar2EntityFeatures.Preset_Remote;

            SetIsLocal(isLocal);
        }

        /// <summary>
        /// Show or hide the avatar without touching the GameObject's active state. The SDK
        /// only advances an entity's load pipeline while its behaviour is active, so
        /// deactivating the object to hide a not-yet-ready avatar deadlocks the load; view
        /// flags hide every primitive while updates keep running.
        /// </summary>
        public void SetPresentationVisible(bool visible)
        {
            if (presentationVisible == visible)
                return;

            presentationVisible = visible;
            if (!IsCreated)
                return;

            SetActiveView(visible
                ? _creationInfo.renderFilters.viewFlags
                : CAPI.ovrAvatar2EntityViewFlags.None);

            // Becoming visible is the other moment new renderers can be in play — a view-flag
            // change brings geometry back that may have been built while hidden.
            if (visible)
                ApplyShadowCasting();
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

            if (IsCreated && !presentationVisible)
                SetActiveView(CAPI.ovrAvatar2EntityViewFlags.None);

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
            _userId = userId;

            // LoadUserWithFilters returns false — with no load request registered and
            // therefore no failure event ever arriving — when the access token is invalid
            // or the native call rejects immediately. The in-flight flag may only be set
            // when a request actually exists, or the trackers wedge forever.
            if (!LoadUserWithFilters(in _creationInfo.renderFilters))
            {
                UserAvatarLoadInFlight = false;
                UserAvatarLoadFailed = true;
                return false;
            }

            UserAvatarLoadInFlight = true;
            UserAvatarLoadFailed = false;
            return true;
        }

        /// <summary>
        /// Abandon tracking of an in-flight user-avatar load (the SDK never reports
        /// cancelled requests, and success events fire only once per entity). Marks the
        /// attempt failed so retry logic re-engages.
        /// </summary>
        public void AbandonUserAvatarLoadTracking()
        {
            if (!UserAvatarLoadInFlight)
                return;

            UserAvatarLoadInFlight = false;
            UserAvatarLoadFailed = true;
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
            ApplyShadowCasting();
        }

        /// <summary>
        /// Makes the avatar cast a shadow.
        /// </summary>
        /// <remarks>
        /// Eric's call (2026-08-24): the player's body should cast a shadow. The proxy body's
        /// renderers are set at creation in BlockiverseNetworkAvatarRig, but this entity's meshes
        /// are built by the SDK and default to whatever it chooses — so fixing only the proxy fixed
        /// the body a player with a working Meta avatar does not have, and Eric still saw no
        /// shadow. That is the same reach mistake as the keyboard hand suppression, made twice in
        /// one session: there are two bodies, and a change has to reach whichever one is on screen.
        ///
        /// Re-applied on every load rather than once, because the SDK swaps geometry underneath us
        /// — a preset loads first and the real likeness replaces it later, and each swap brings new
        /// renderers that never saw the original pass.
        ///
        /// KNOWN NO-OP, kept deliberately. Eric reported no avatar shadow even after this ran, and
        /// the reason is in Meta's shaders rather than here: Avatar-Meta, Cel-Avatar-Meta and
        /// Style-2-Avatar-Meta declare ZERO ShadowCaster passes (com.meta.xr.sdk.avatars@402f13fd),
        /// so a renderer set to cast is submitted to the shadow pass with no pass to draw. Avatars
        /// skin on the GPU via skin-to-texture, so a stock ShadowCaster would emit the bind pose
        /// anyway — omitting it was almost certainly intentional on Meta's side.
        ///
        /// The body shadow a player actually sees is cast by the proxy body in shadow-only mode;
        /// see BlockiverseNetworkAvatarRig.ApplyFallbackRendererVisibility. This method stays
        /// because it is free, correct in principle, and starts working by itself the day Meta adds
        /// a shadow pass — but do NOT read its presence as "avatar shadows are handled".
        /// </remarks>
        public void ApplyShadowCasting()
        {
            foreach (Renderer avatarRenderer in GetComponentsInChildren<Renderer>(includeInactive: true))
                avatarRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void HandleLoadFailed(OvrAvatarEntity entity, CAPI.ovrAvatar2LoadRequestInfo requestInfo)
        {
            // Only user (CDN) request failures end the tracked attempt: the SDK raises this
            // event for every failed load request on the entity, including preset zips and
            // asset requests, and attributing those to the profile load schedules spurious
            // retries. The provider owns retry policy and backoff.
            if (!UserAvatarLoadInFlight || requestInfo.type != CAPI.ovrAvatar2LoadRequestType.User)
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
