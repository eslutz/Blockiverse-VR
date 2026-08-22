using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blockiverse.Networking
{
    // Used in two modes:
    // - On the local XR rig prefab as an unspawned pose/fallback-avatar proxy; do not add a
    //   NetworkObject to BlockiverseXRRig just to satisfy the NetworkBehaviour base type.
    // - On the spawned network player prefab with a NetworkObject, where RPC pose relay runs.
    [DisallowMultipleComponent]
    public sealed class BlockiverseNetworkAvatarRig : NetworkBehaviour
    {
        const string FallbackRootName = "Fallback Proxy Avatar";
        const string BodyName = "Fallback Body";
        const string HeadAnchorName = "Fallback Head Anchor";
        const string LeftHandAnchorName = "Fallback Left Hand Anchor";
        const string RightHandAnchorName = "Fallback Right Hand Anchor";
        const string HeadVisualName = "Fallback Head";
        const string LeftHandVisualName = "Fallback Left Hand";
        const string RightHandVisualName = "Fallback Right Hand";
        const string CameraOffsetName = "Camera Offset";
        const string LeftControllerName = "Left Controller";
        const string RightControllerName = "Right Controller";
        const float TrackingFallbackSearchIntervalSeconds = 1.0f;

        static readonly Vector3 DefaultHeadLocalPosition = new(0.0f, 1.62f, 0.0f);
        static readonly Vector3 DefaultLeftHandLocalPosition = new(-0.38f, 1.18f, 0.28f);
        static readonly Vector3 DefaultRightHandLocalPosition = new(0.38f, 1.18f, 0.28f);

        [SerializeField] bool fallbackProxyEnabled = true;
        [SerializeField] bool firstPersonFallbackVisualsEnabled;
        [SerializeField] bool firstPersonFallbackVisualsSuppressed;
        [SerializeField] bool metaAvatarAvailable;
        [SerializeField] float poseSendRateHz = 30.0f;
        [SerializeField] float remotePoseInterpolationSpeed = 18.0f;
        [SerializeField] float stalenessThreshold = 3.0f;
        [SerializeField] Transform rootTrackingSource;
        [SerializeField] Transform headTrackingSource;
        [SerializeField] Transform leftHandTrackingSource;
        [SerializeField] Transform rightHandTrackingSource;
        [SerializeField] Transform fallbackRoot;
        [SerializeField] Transform headAnchor;
        [SerializeField] Transform leftHandAnchor;
        [SerializeField] Transform rightHandAnchor;
        // One colour for every player, deliberately. There used to be an owner/remote split, but
        // it disambiguated nothing: your own hands are always the pair attached to your view, and
        // you only ever see someone else's from across the room. A warm tone reads as a hand; the
        // blue that the owner half used did not.
        [SerializeField] Color fallbackColor = new(0.94f, 0.62f, 0.22f, 1.0f);

        Renderer[] fallbackRenderers = Array.Empty<Renderer>();
        Material fallbackMaterial;
        uint nextPoseSequence = 1;
        uint lastAppliedPoseSequence;
        AvatarPose targetRemotePose = AvatarPose.Default;
        AvatarPose smoothedRemotePose = AvatarPose.Default;
        bool hasRemotePose;
        float nextPoseSendTime;
        float nextTrackingFallbackSearchTime;

        public bool FallbackProxyEnabled => fallbackProxyEnabled;
        public bool FirstPersonFallbackVisualsEnabled => firstPersonFallbackVisualsEnabled;
        public bool FirstPersonFallbackVisualsSuppressed => firstPersonFallbackVisualsSuppressed;
        public bool MetaAvatarAvailable => metaAvatarAvailable;
        public bool IsUsingFallbackProxy { get; private set; }
        public bool FallbackRenderersVisible { get; private set; }
        public float RemotePoseInterpolationSpeed => remotePoseInterpolationSpeed;
        public Transform FallbackRoot => fallbackRoot;
        public Transform HeadAnchor => headAnchor;
        public Transform LeftHandAnchor => leftHandAnchor;
        public Transform RightHandAnchor => rightHandAnchor;
        public float LastRemotePoseTime { get; private set; }
        public bool IsPoseStale { get; private set; }
        public bool IsStreamStale { get; private set; }
        public bool IsSpawnedForTest { get; set; }

        public void ConfigureTrackingSources(Transform head, Transform leftHand, Transform rightHand)
        {
            ConfigureTrackingSources(null, head, leftHand, rightHand);
        }

        public void ConfigureTrackingSources(Transform root, Transform head, Transform leftHand, Transform rightHand)
        {
            rootTrackingSource = root;
            headTrackingSource = head;
            leftHandTrackingSource = leftHand;
            rightHandTrackingSource = rightHand;
        }

        void Awake()
        {
            EnsureFallbackProxy();
            RefreshAvatarMode();
        }

        public override void OnNetworkSpawn()
        {
            EnsureFallbackProxy();
            RefreshAvatarMode();
            ApplyFallbackPalette();

            if (IsOwner)
            {
                PublishPose();
            }
            else
            {
                LastRemotePoseTime = Time.unscaledTime;
                ApplySmoothedRemotePose(snap: true);
            }
        }

        void LateUpdate()
        {
            bool isSpawned = IsSpawned || IsSpawnedForTest;
            if (!isSpawned)
            {
                RefreshLocalTrackingPose();
                return;
            }

            bool isOwner = IsOwner && !IsSpawnedForTest;
            if (isOwner)
            {
                ApplyTrackingSources();
                PublishPose();
            }
            else
            {
                ApplySmoothedRemotePose();
                bool poseStale = hasRemotePose && (Time.unscaledTime - LastRemotePoseTime) > stalenessThreshold;
                if (poseStale != IsPoseStale)
                {
                    IsPoseStale = poseStale;
                    UpdateStalenessVisibility();
                }
            }
        }

        public override void OnDestroy()
        {
            if (fallbackMaterial != null)
                DestroyUnityObject(fallbackMaterial);

            base.OnDestroy();
        }

        public void ConfigureFallbackProxy(bool enabled)
        {
            if (fallbackProxyEnabled == enabled && fallbackRoot != null)
                return;

            fallbackProxyEnabled = enabled;
            RefreshAvatarMode();
        }

        public void ConfigureFirstPersonFallbackVisuals(bool enabled)
        {
            if (firstPersonFallbackVisualsEnabled == enabled && fallbackRoot != null)
                return;

            firstPersonFallbackVisualsEnabled = enabled;
            RefreshAvatarMode();
        }

        public void SetFirstPersonFallbackVisualsSuppressed(bool suppressed)
        {
            if (firstPersonFallbackVisualsSuppressed == suppressed && fallbackRoot != null)
                return;

            firstPersonFallbackVisualsSuppressed = suppressed;
            ApplyFallbackRendererVisibility();
        }

        public void SetMetaAvatarAvailable(bool available)
        {
            if (metaAvatarAvailable == available && fallbackRoot != null)
                return;

            metaAvatarAvailable = available;
            RefreshAvatarMode();
        }

        public void SetLocalRigPose(Pose headPose, Pose leftHandPose, Pose rightHandPose)
        {
            EnsureFallbackProxy();
            ApplyLocalPose(headPose, leftHandPose, rightHandPose);

            if (IsSpawned && IsOwner)
                PublishPose();
        }

        public void RefreshLocalTrackingPose()
        {
            EnsureFallbackProxy();
            ApplyTrackingSources();
        }

        public void RefreshAvatarMode()
        {
            EnsureFallbackProxy();
            IsUsingFallbackProxy = fallbackProxyEnabled && !metaAvatarAvailable;

            UpdateStalenessVisibility();
        }

        void PublishPose()
        {
            float minInterval = poseSendRateHz <= 0.0f ? 0.0f : 1.0f / poseSendRateHz;

            if (minInterval > 0.0f && Time.unscaledTime < nextPoseSendTime)
                return;

            nextPoseSendTime = Time.unscaledTime + minInterval;
            AvatarPose pose = AvatarPose.FromTransforms(
                transform,
                headAnchor,
                leftHandAnchor,
                rightHandAnchor);
            pose.Sequence = AllocatePoseSequence();

            SubmitAvatarPoseRpc(pose);
        }

        uint AllocatePoseSequence()
        {
            unchecked
            {
                nextPoseSequence++;
            }

            // Zero marks an unsequenced pose (a directly applied local/test pose), so it is
            // never handed out as a real sequence number.
            if (nextPoseSequence == 0)
                nextPoseSequence = 1;

            return nextPoseSequence;
        }

        // InvokePermission.Owner preserves the old [ServerRpc] default (which required
        // ownership): only the player this rig belongs to may publish its pose.
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void SubmitAvatarPoseRpc(AvatarPose pose)
        {
            ReceiveAvatarPoseRpc(pose);
        }

        public void ApplyRemotePose(AvatarPose pose)
        {
            // Poses travel unreliably, so they can arrive out of order. Applying an older pose
            // after a newer one snaps a remote head backwards — the kind of jitter VR punishes
            // hardest. Sequence 0 means "unsequenced" (a directly applied pose) and is accepted.
            if (pose.Sequence != 0)
            {
                if (hasRemotePose && lastAppliedPoseSequence != 0 && !IsNewerPoseSequence(pose.Sequence, lastAppliedPoseSequence))
                    return;

                lastAppliedPoseSequence = pose.Sequence;
            }

            targetRemotePose = pose;
            LastRemotePoseTime = Time.unscaledTime;
            if (IsPoseStale)
            {
                IsPoseStale = false;
                UpdateStalenessVisibility();
            }
            if (!hasRemotePose)
            {
                smoothedRemotePose = pose;
                hasRemotePose = true;
                ApplyPose(smoothedRemotePose);
            }
        }

        // SendTo.NotOwner reproduces the old hand-built recipient list (every connected client
        // except the owner, host included) without the per-send list rebuild.
        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        void ReceiveAvatarPoseRpc(AvatarPose pose)
        {
            if (IsOwner)
                return;

            ApplyRemotePose(pose);
        }

        // Serial-number comparison: correct across the uint wrap, unlike a plain '>'.
        static bool IsNewerPoseSequence(uint incoming, uint lastApplied) =>
            (int)(incoming - lastApplied) > 0;

        public void SetStreamStale(bool stale)
        {
            if (IsStreamStale != stale)
            {
                IsStreamStale = stale;
                UpdateStalenessVisibility();
            }
        }

        public static bool TryResolvePlayerHeadWorldPosition(NetworkObject playerObject, out Vector3 position)
        {
            position = default;
            if (playerObject == null)
                return false;

            BlockiverseNetworkAvatarRig avatarRig = playerObject.GetComponent<BlockiverseNetworkAvatarRig>();
            Transform headTransform = avatarRig?.HeadAnchor != null ? avatarRig.HeadAnchor : playerObject.transform;
            position = headTransform.position;
            return true;
        }

        public void UpdateStalenessVisibility()
        {
            bool metaVisible = metaAvatarAvailable && !IsStreamStale && !IsPoseStale;
            bool fallbackVisible = fallbackProxyEnabled && (IsStreamStale || !metaAvatarAvailable) && !IsPoseStale;

            if (fallbackRoot != null)
            {
                fallbackRoot.gameObject.SetActive(fallbackVisible);
            }

            Transform metaEntityNode = transform.Find("Meta Horizon Avatar Entity");
            if (metaEntityNode != null)
            {
                metaEntityNode.gameObject.SetActive(metaVisible);
            }

            // The fallback proxy serves both the never-available and the stale-stream
            // cases; keep the renderer state in lockstep with the root, otherwise a
            // stale Meta stream activates a root whose renderers were disabled when
            // the Meta avatar first became available and the player disappears.
            IsUsingFallbackProxy = fallbackVisible;

            if (IsPoseStale)
            {
                // Everything is hidden while the pose itself is stale; the fallback
                // root is inactive so no renderer state needs to change.
                FallbackRenderersVisible = false;
            }
            else
            {
                ApplyFallbackRendererVisibility();
            }
        }

        void ApplyTrackingSources()
        {
            ResolveTrackingSources();

            if (headTrackingSource == null && leftHandTrackingSource == null && rightHandTrackingSource == null)
                return;

            ApplyRootTrackingSource();
            ApplyLocalPose(
                ToLocalPose(headTrackingSource, DefaultHeadLocalPosition),
                ToLocalPose(leftHandTrackingSource, DefaultLeftHandLocalPosition),
                ToLocalPose(rightHandTrackingSource, DefaultRightHandLocalPosition));
        }

        void ApplyRootTrackingSource()
        {
            if (rootTrackingSource == null || rootTrackingSource == transform)
                return;

            transform.SetPositionAndRotation(rootTrackingSource.position, rootTrackingSource.rotation);
        }

        Pose ToLocalPose(Transform source, Vector3 fallbackPosition)
        {
            if (source == null)
                return new Pose(fallbackPosition, Quaternion.identity);

            return new Pose(
                transform.InverseTransformPoint(source.position),
                Quaternion.Inverse(transform.rotation) * source.rotation);
        }

        void ApplyPose(AvatarPose pose)
        {
            transform.SetPositionAndRotation(pose.RootPosition, pose.RootRotation);
            ApplyLocalPose(
                new Pose(pose.HeadLocalPosition, pose.HeadLocalRotation),
                new Pose(pose.LeftHandLocalPosition, pose.LeftHandLocalRotation),
                new Pose(pose.RightHandLocalPosition, pose.RightHandLocalRotation));
        }

        void ApplySmoothedRemotePose(bool snap = false)
        {
            if (!hasRemotePose)
            {
                smoothedRemotePose = targetRemotePose;
                ApplyPose(smoothedRemotePose);
                return;
            }

            float t = snap
                ? 1.0f
                : 1.0f - Mathf.Exp(-Mathf.Max(0.0f, remotePoseInterpolationSpeed) * Time.deltaTime);
            smoothedRemotePose = AvatarPose.Lerp(smoothedRemotePose, targetRemotePose, t);
            ApplyPose(smoothedRemotePose);
        }

        void ApplyLocalPose(Pose headPose, Pose leftHandPose, Pose rightHandPose)
        {
            if (headAnchor != null)
                headAnchor.SetLocalPositionAndRotation(headPose.position, headPose.rotation);

            if (leftHandAnchor != null)
                leftHandAnchor.SetLocalPositionAndRotation(leftHandPose.position, leftHandPose.rotation);

            if (rightHandAnchor != null)
                rightHandAnchor.SetLocalPositionAndRotation(rightHandPose.position, rightHandPose.rotation);
        }

        void EnsureFallbackProxy()
        {
            fallbackRoot = EnsureChild(transform, fallbackRoot, FallbackRootName);
            headAnchor = EnsureChild(fallbackRoot, headAnchor, HeadAnchorName);
            leftHandAnchor = EnsureChild(fallbackRoot, leftHandAnchor, LeftHandAnchorName);
            rightHandAnchor = EnsureChild(fallbackRoot, rightHandAnchor, RightHandAnchorName);

            headAnchor.SetLocalPositionAndRotation(DefaultHeadLocalPosition, Quaternion.identity);
            leftHandAnchor.SetLocalPositionAndRotation(DefaultLeftHandLocalPosition, Quaternion.identity);
            rightHandAnchor.SetLocalPositionAndRotation(DefaultRightHandLocalPosition, Quaternion.identity);

            EnsurePrimitive(
                fallbackRoot,
                BodyName,
                PrimitiveType.Capsule,
                new Vector3(0.0f, 0.85f, 0.0f),
                Quaternion.identity,
                new Vector3(0.36f, 0.72f, 0.36f));

            EnsurePrimitive(
                headAnchor,
                HeadVisualName,
                PrimitiveType.Cube,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.28f, 0.24f, 0.28f));

            EnsurePrimitive(
                leftHandAnchor,
                LeftHandVisualName,
                PrimitiveType.Cube,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.16f, 0.16f, 0.16f));

            EnsurePrimitive(
                rightHandAnchor,
                RightHandVisualName,
                PrimitiveType.Cube,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.16f, 0.16f, 0.16f));

            fallbackRenderers = fallbackRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            ApplyFallbackPalette();
        }

        void ApplyFallbackPalette()
        {
            if (fallbackRenderers == null || fallbackRenderers.Length == 0)
                return;

            Color color = fallbackColor;
            fallbackMaterial ??= CreateFallbackMaterial(color);
            ApplyFallbackMaterialColor(fallbackMaterial, color);

            foreach (Renderer fallbackRenderer in fallbackRenderers)
            {
                if (fallbackRenderer == null)
                    continue;

                fallbackRenderer.sharedMaterial = fallbackMaterial;
            }

            ApplyFallbackRendererVisibility();
        }

        void ApplyFallbackRendererVisibility()
        {
            bool showThirdPersonProxy = IsUsingFallbackProxy && ShouldRenderThirdPersonFallbackVisuals();
            bool showFirstPersonHands = IsUsingFallbackProxy &&
                firstPersonFallbackVisualsEnabled &&
                !firstPersonFallbackVisualsSuppressed;
            bool anyVisible = false;

            if (fallbackRenderers == null)
                return;

            foreach (Renderer fallbackRenderer in fallbackRenderers)
            {
                if (fallbackRenderer == null)
                    continue;

                bool visible = showThirdPersonProxy ||
                    (showFirstPersonHands && IsFirstPersonFallbackRenderer(fallbackRenderer));
                fallbackRenderer.enabled = visible;
                anyVisible |= visible;
            }

            FallbackRenderersVisible = anyVisible;
        }

        bool ShouldRenderThirdPersonFallbackVisuals()
        {
            return IsSpawned && !IsOwner;
        }

        static bool IsFirstPersonFallbackRenderer(Renderer fallbackRenderer)
        {
            return fallbackRenderer.transform.name == LeftHandVisualName ||
                fallbackRenderer.transform.name == RightHandVisualName;
        }

        void ResolveTrackingSources()
        {
            if (headTrackingSource == null && Camera.main != null)
                headTrackingSource = Camera.main.transform;

            ResolveHandSourcesFromKnownRig();

            if ((leftHandTrackingSource == null || rightHandTrackingSource == null) &&
                Time.unscaledTime >= nextTrackingFallbackSearchTime)
            {
                nextTrackingFallbackSearchTime = Time.unscaledTime + TrackingFallbackSearchIntervalSeconds;

                if (leftHandTrackingSource == null)
                    leftHandTrackingSource = FindNamedTransformGlobally(LeftControllerName);

                if (rightHandTrackingSource == null)
                    rightHandTrackingSource = FindNamedTransformGlobally(RightControllerName);
            }

            if (rootTrackingSource == null)
                rootTrackingSource = InferTrackingRootSource();
        }

        void ResolveHandSourcesFromKnownRig()
        {
            Transform cameraOffset = ResolveCameraOffset();

            if (leftHandTrackingSource == null)
                leftHandTrackingSource = cameraOffset != null ? cameraOffset.Find(LeftControllerName) : null;

            if (rightHandTrackingSource == null)
                rightHandTrackingSource = cameraOffset != null ? cameraOffset.Find(RightControllerName) : null;
        }

        Transform ResolveCameraOffset()
        {
            if (rootTrackingSource != null)
            {
                Transform cameraOffset = rootTrackingSource.Find(CameraOffsetName);
                if (cameraOffset != null)
                    return cameraOffset;
            }

            if (headTrackingSource != null && headTrackingSource.parent != null)
            {
                Transform parent = headTrackingSource.parent;
                if (parent.name == CameraOffsetName)
                    return parent;
            }

            return null;
        }

        Transform InferTrackingRootSource()
        {
            Transform source = headTrackingSource ?? leftHandTrackingSource ?? rightHandTrackingSource;
            Transform root = source != null ? source.root : null;
            return root != null && root != source && root != transform ? root : null;
        }

        static Transform FindNamedTransformGlobally(string targetName)
        {
            foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.name == targetName)
                    return candidate;
            }

            return null;
        }

        static Transform EnsureChild(Transform parent, Transform current, string name)
        {
            if (current != null)
                return current;

            Transform existing = parent.Find(name);

            if (existing != null)
                return existing;

            GameObject child = new(name);
            child.transform.SetParent(parent, worldPositionStays: false);
            return child.transform;
        }

        static GameObject EnsurePrimitive(
            Transform parent,
            string name,
            PrimitiveType primitiveType,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale)
        {
            Transform existing = parent.Find(name);
            GameObject primitive = existing != null ? existing.gameObject : GameObject.CreatePrimitive(primitiveType);
            primitive.name = name;
            primitive.transform.SetParent(parent, worldPositionStays: false);
            primitive.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            primitive.transform.localScale = localScale;

            Collider collider = primitive.GetComponent<Collider>();

            if (collider != null)
                DestroyUnityObject(collider);

            // The player has hands and no body, so a cast shadow is two disembodied blobs on the
            // ground beside them. Every other non-terrain renderer in the project already opts out
            // (ray visual, placement preview, lightning bolt, chunk fluid); CreatePrimitive just
            // defaults to On and nobody had turned it off here.
            Renderer primitiveRenderer = primitive.GetComponent<Renderer>();

            if (primitiveRenderer != null)
                primitiveRenderer.shadowCastingMode = ShadowCastingMode.Off;

            return primitive;
        }

        static Material CreateFallbackMaterial(Color color)
        {
            // LIT. An earlier pass moved this to Unlit to kill the directional gradient that gave
            // away hands being brightly shaded inside a pitch-black cave -- but that gradient is
            // also what makes them read as objects rather than as flat cut-outs, and losing it was
            // immediately obvious.
            //
            // Keeping Lit is fine now: BlockiverseHandLightDriver scales the ALBEDO by the voxel
            // light sampled at each hand, so in a sealed room the albedo is near black and the lit
            // result is dark however bright the scene's directional light happens to be. The
            // shading survives; the cave gate comes from the multiply.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var material = new Material(shader)
            {
                name = "Blockiverse Fallback Proxy Avatar",
            };
            ApplyFallbackMaterialColor(material, color);
            return material;
        }

        static void ApplyFallbackMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else
                material.color = color;
        }

        static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        public struct AvatarPose : INetworkSerializable, IEquatable<AvatarPose>
        {
            /// <summary>Range, in metres, of the quantized head/hand offsets from the rig root.</summary>
            const float LocalOffsetRangeMeters = 4.0f;

            /// <summary>Largest magnitude any component of a unit quaternion's smallest three can take.</summary>
            const float SmallestThreeRange = 0.70710678f;

            /// <summary>
            /// Monotonic per-owner send counter. Poses go out unreliably, so this is what lets a
            /// receiver discard one that overtook a newer pose in flight. Zero means unsequenced.
            /// Deliberately excluded from <see cref="Equals(AvatarPose)"/>: it is transport
            /// metadata, not part of the pose itself.
            /// </summary>
            public uint Sequence;

            public Vector3 RootPosition;
            public Quaternion RootRotation;
            public Vector3 HeadLocalPosition;
            public Quaternion HeadLocalRotation;
            public Vector3 LeftHandLocalPosition;
            public Quaternion LeftHandLocalRotation;
            public Vector3 RightHandLocalPosition;
            public Quaternion RightHandLocalRotation;

            public static AvatarPose Default => new()
            {
                RootPosition = Vector3.zero,
                RootRotation = Quaternion.identity,
                HeadLocalPosition = DefaultHeadLocalPosition,
                HeadLocalRotation = Quaternion.identity,
                LeftHandLocalPosition = DefaultLeftHandLocalPosition,
                LeftHandLocalRotation = Quaternion.identity,
                RightHandLocalPosition = DefaultRightHandLocalPosition,
                RightHandLocalRotation = Quaternion.identity
            };

            public static AvatarPose FromTransforms(
                Transform root,
                Transform head,
                Transform leftHand,
                Transform rightHand)
            {
                return new AvatarPose
                {
                    RootPosition = root != null ? root.position : Vector3.zero,
                    RootRotation = root != null ? root.rotation : Quaternion.identity,
                    HeadLocalPosition = head != null ? head.localPosition : DefaultHeadLocalPosition,
                    HeadLocalRotation = head != null ? head.localRotation : Quaternion.identity,
                    LeftHandLocalPosition = leftHand != null ? leftHand.localPosition : DefaultLeftHandLocalPosition,
                    LeftHandLocalRotation = leftHand != null ? leftHand.localRotation : Quaternion.identity,
                    RightHandLocalPosition = rightHand != null ? rightHand.localPosition : DefaultRightHandLocalPosition,
                    RightHandLocalRotation = rightHand != null ? rightHand.localRotation : Quaternion.identity
                };
            }

            public static AvatarPose Lerp(AvatarPose from, AvatarPose to, float t)
            {
                t = Mathf.Clamp01(t);
                return new AvatarPose
                {
                    RootPosition = Vector3.LerpUnclamped(from.RootPosition, to.RootPosition, t),
                    RootRotation = Quaternion.SlerpUnclamped(from.RootRotation, to.RootRotation, t),
                    HeadLocalPosition = Vector3.LerpUnclamped(from.HeadLocalPosition, to.HeadLocalPosition, t),
                    HeadLocalRotation = Quaternion.SlerpUnclamped(from.HeadLocalRotation, to.HeadLocalRotation, t),
                    LeftHandLocalPosition = Vector3.LerpUnclamped(from.LeftHandLocalPosition, to.LeftHandLocalPosition, t),
                    LeftHandLocalRotation = Quaternion.SlerpUnclamped(from.LeftHandLocalRotation, to.LeftHandLocalRotation, t),
                    RightHandLocalPosition = Vector3.LerpUnclamped(from.RightHandLocalPosition, to.RightHandLocalPosition, t),
                    RightHandLocalRotation = Quaternion.SlerpUnclamped(from.RightHandLocalRotation, to.RightHandLocalRotation, t),
                };
            }

            // 50 bytes on the wire instead of 112. Poses are sent per player at 30 Hz for the
            // whole session, so the shape of this method is the avatar system's entire bandwidth
            // cost. The root keeps full float precision because it carries world coordinates that
            // reach the far side of a 256-block world; everything else is body-scale and is
            // quantized: offsets to 16-bit fixed point over +/-4 m (~0.12 mm), rotations to a
            // 32-bit smallest-three packing (~0.16 degrees).
            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Sequence);
                serializer.SerializeValue(ref RootPosition);

                if (serializer.IsWriter)
                {
                    uint rootRotation = CompressRotation(RootRotation);
                    uint headRotation = CompressRotation(HeadLocalRotation);
                    uint leftRotation = CompressRotation(LeftHandLocalRotation);
                    uint rightRotation = CompressRotation(RightHandLocalRotation);
                    serializer.SerializeValue(ref rootRotation);
                    serializer.SerializeValue(ref headRotation);
                    serializer.SerializeValue(ref leftRotation);
                    serializer.SerializeValue(ref rightRotation);

                    WriteOffset(serializer, ref HeadLocalPosition);
                    WriteOffset(serializer, ref LeftHandLocalPosition);
                    WriteOffset(serializer, ref RightHandLocalPosition);
                    return;
                }

                uint packedRoot = 0;
                uint packedHead = 0;
                uint packedLeft = 0;
                uint packedRight = 0;
                serializer.SerializeValue(ref packedRoot);
                serializer.SerializeValue(ref packedHead);
                serializer.SerializeValue(ref packedLeft);
                serializer.SerializeValue(ref packedRight);
                RootRotation = DecompressRotation(packedRoot);
                HeadLocalRotation = DecompressRotation(packedHead);
                LeftHandLocalRotation = DecompressRotation(packedLeft);
                RightHandLocalRotation = DecompressRotation(packedRight);

                HeadLocalPosition = ReadOffset(serializer);
                LeftHandLocalPosition = ReadOffset(serializer);
                RightHandLocalPosition = ReadOffset(serializer);
            }

            static void WriteOffset<T>(BufferSerializer<T> serializer, ref Vector3 offset) where T : IReaderWriter
            {
                short x = QuantizeOffset(offset.x);
                short y = QuantizeOffset(offset.y);
                short z = QuantizeOffset(offset.z);
                serializer.SerializeValue(ref x);
                serializer.SerializeValue(ref y);
                serializer.SerializeValue(ref z);
            }

            static Vector3 ReadOffset<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                short x = 0;
                short y = 0;
                short z = 0;
                serializer.SerializeValue(ref x);
                serializer.SerializeValue(ref y);
                serializer.SerializeValue(ref z);
                return new Vector3(DequantizeOffset(x), DequantizeOffset(y), DequantizeOffset(z));
            }

            static short QuantizeOffset(float value)
            {
                float clamped = Mathf.Clamp(value, -LocalOffsetRangeMeters, LocalOffsetRangeMeters);
                return (short)Mathf.RoundToInt(clamped / LocalOffsetRangeMeters * short.MaxValue);
            }

            static float DequantizeOffset(short value) =>
                value / (float)short.MaxValue * LocalOffsetRangeMeters;

            /// <summary>
            /// Smallest-three packing: 2 bits naming the largest component, then the other three
            /// at 10 bits each. The dropped component is rebuilt from unit length, and its sign
            /// costs nothing because q and -q are the same rotation — the whole quaternion is
            /// negated so the dropped one is always positive.
            /// </summary>
            public static uint CompressRotation(Quaternion rotation)
            {
                float x = rotation.x;
                float y = rotation.y;
                float z = rotation.z;
                float w = rotation.w;

                float magnitude = Mathf.Sqrt(x * x + y * y + z * z + w * w);
                if (magnitude < 1e-6f)
                {
                    x = 0.0f;
                    y = 0.0f;
                    z = 0.0f;
                    w = 1.0f;
                }
                else
                {
                    x /= magnitude;
                    y /= magnitude;
                    z /= magnitude;
                    w /= magnitude;
                }

                int largest = 0;
                float largestAbs = Mathf.Abs(x);

                if (Mathf.Abs(y) > largestAbs)
                {
                    largest = 1;
                    largestAbs = Mathf.Abs(y);
                }

                if (Mathf.Abs(z) > largestAbs)
                {
                    largest = 2;
                    largestAbs = Mathf.Abs(z);
                }

                if (Mathf.Abs(w) > largestAbs)
                    largest = 3;

                float largestValue = largest switch
                {
                    0 => x,
                    1 => y,
                    2 => z,
                    _ => w,
                };

                if (largestValue < 0.0f)
                {
                    x = -x;
                    y = -y;
                    z = -z;
                    w = -w;
                }

                (float a, float b, float c) = largest switch
                {
                    0 => (y, z, w),
                    1 => (x, z, w),
                    2 => (x, y, w),
                    _ => (x, y, z),
                };

                return ((uint)largest << 30) |
                       ((uint)QuantizeRotationComponent(a) << 20) |
                       ((uint)QuantizeRotationComponent(b) << 10) |
                       (uint)QuantizeRotationComponent(c);
            }

            public static Quaternion DecompressRotation(uint packed)
            {
                int largest = (int)(packed >> 30);
                float a = DequantizeRotationComponent((int)((packed >> 20) & 0x3FFu));
                float b = DequantizeRotationComponent((int)((packed >> 10) & 0x3FFu));
                float c = DequantizeRotationComponent((int)(packed & 0x3FFu));
                float d = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - (a * a + b * b + c * c)));

                return largest switch
                {
                    0 => new Quaternion(d, a, b, c),
                    1 => new Quaternion(a, d, b, c),
                    2 => new Quaternion(a, b, d, c),
                    _ => new Quaternion(a, b, c, d),
                };
            }

            static int QuantizeRotationComponent(float value)
            {
                float normalized = Mathf.Clamp(value / SmallestThreeRange, -1.0f, 1.0f);
                return Mathf.Clamp(Mathf.RoundToInt((normalized * 0.5f + 0.5f) * 1023.0f), 0, 1023);
            }

            static float DequantizeRotationComponent(int quantized) =>
                (quantized / 1023.0f * 2.0f - 1.0f) * SmallestThreeRange;

            public bool Equals(AvatarPose other)
            {
                return RootPosition == other.RootPosition &&
                       RootRotation == other.RootRotation &&
                       HeadLocalPosition == other.HeadLocalPosition &&
                       HeadLocalRotation == other.HeadLocalRotation &&
                       LeftHandLocalPosition == other.LeftHandLocalPosition &&
                       LeftHandLocalRotation == other.LeftHandLocalRotation &&
                       RightHandLocalPosition == other.RightHandLocalPosition &&
                       RightHandLocalRotation == other.RightHandLocalRotation;
            }

            public override bool Equals(object obj)
            {
                return obj is AvatarPose other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    RootPosition,
                    RootRotation,
                    HeadLocalPosition,
                    HeadLocalRotation,
                    LeftHandLocalPosition,
                    LeftHandLocalRotation,
                    RightHandLocalPosition,
                    RightHandLocalRotation);
            }
        }
    }
}
