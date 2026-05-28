using System;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Networking
{
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

        static readonly Vector3 DefaultHeadLocalPosition = new(0.0f, 1.62f, 0.0f);
        static readonly Vector3 DefaultLeftHandLocalPosition = new(-0.38f, 1.18f, 0.28f);
        static readonly Vector3 DefaultRightHandLocalPosition = new(0.38f, 1.18f, 0.28f);

        readonly NetworkVariable<AvatarPose> synchronizedPose = new(
            AvatarPose.Default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        [SerializeField] bool fallbackProxyEnabled = true;
        [SerializeField] bool metaAvatarAvailable;
        [SerializeField] float poseSendRateHz = 30.0f;
        [SerializeField] Transform headTrackingSource;
        [SerializeField] Transform leftHandTrackingSource;
        [SerializeField] Transform rightHandTrackingSource;
        [SerializeField] Transform fallbackRoot;
        [SerializeField] Transform headAnchor;
        [SerializeField] Transform leftHandAnchor;
        [SerializeField] Transform rightHandAnchor;
        [SerializeField] Color ownerFallbackColor = new(0.2f, 0.68f, 0.94f, 1.0f);
        [SerializeField] Color remoteFallbackColor = new(0.94f, 0.62f, 0.22f, 1.0f);

        Renderer[] fallbackRenderers = Array.Empty<Renderer>();
        Material fallbackMaterial;
        float nextPoseSendTime;

        public bool FallbackProxyEnabled => fallbackProxyEnabled;
        public bool MetaAvatarAvailable => metaAvatarAvailable;
        public bool IsUsingFallbackProxy { get; private set; }
        public Transform FallbackRoot => fallbackRoot;
        public Transform HeadAnchor => headAnchor;
        public Transform LeftHandAnchor => leftHandAnchor;
        public Transform RightHandAnchor => rightHandAnchor;

        public void ConfigureTrackingSources(Transform head, Transform leftHand, Transform rightHand)
        {
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
                PublishPose();
            else
                ApplyPose(synchronizedPose.Value);
        }

        void LateUpdate()
        {
            if (!IsSpawned)
            {
                RefreshLocalTrackingPose();
                return;
            }

            if (IsOwner)
            {
                ApplyTrackingSources();
                PublishPose();
            }
            else
            {
                ApplyPose(synchronizedPose.Value);
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

            if (fallbackRoot != null)
                fallbackRoot.gameObject.SetActive(IsUsingFallbackProxy);
        }

        void PublishPose()
        {
            float minInterval = poseSendRateHz <= 0.0f ? 0.0f : 1.0f / poseSendRateHz;

            if (minInterval > 0.0f && Time.unscaledTime < nextPoseSendTime)
                return;

            nextPoseSendTime = Time.unscaledTime + minInterval;
            synchronizedPose.Value = AvatarPose.FromTransforms(
                transform,
                headAnchor,
                leftHandAnchor,
                rightHandAnchor);
        }

        void ApplyTrackingSources()
        {
            ResolveTrackingSources();

            if (headTrackingSource == null && leftHandTrackingSource == null && rightHandTrackingSource == null)
                return;

            ApplyLocalPose(
                ToLocalPose(headTrackingSource, DefaultHeadLocalPosition),
                ToLocalPose(leftHandTrackingSource, DefaultLeftHandLocalPosition),
                ToLocalPose(rightHandTrackingSource, DefaultRightHandLocalPosition));
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

            Color color = IsSpawned && IsOwner ? ownerFallbackColor : remoteFallbackColor;
            fallbackMaterial ??= CreateFallbackMaterial(color);
            ApplyFallbackMaterialColor(fallbackMaterial, color);

            foreach (Renderer fallbackRenderer in fallbackRenderers)
            {
                if (fallbackRenderer == null)
                    continue;

                fallbackRenderer.sharedMaterial = fallbackMaterial;
            }
        }

        void ResolveTrackingSources()
        {
            if (headTrackingSource == null && Camera.main != null)
                headTrackingSource = Camera.main.transform;

            if (leftHandTrackingSource == null)
                leftHandTrackingSource = FindNamedTransform("Left Controller");

            if (rightHandTrackingSource == null)
                rightHandTrackingSource = FindNamedTransform("Right Controller");
        }

        static Transform FindNamedTransform(string targetName)
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

            return primitive;
        }

        static Material CreateFallbackMaterial(Color color)
        {
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

        struct AvatarPose : INetworkSerializable, IEquatable<AvatarPose>
        {
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

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref RootPosition);
                serializer.SerializeValue(ref RootRotation);
                serializer.SerializeValue(ref HeadLocalPosition);
                serializer.SerializeValue(ref HeadLocalRotation);
                serializer.SerializeValue(ref LeftHandLocalPosition);
                serializer.SerializeValue(ref LeftHandLocalRotation);
                serializer.SerializeValue(ref RightHandLocalPosition);
                serializer.SerializeValue(ref RightHandLocalRotation);
            }

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
