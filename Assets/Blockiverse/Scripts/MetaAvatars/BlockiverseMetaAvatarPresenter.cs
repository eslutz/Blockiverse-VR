using System;
using Blockiverse.Networking;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseMetaAvatarPresenter : MonoBehaviour
    {
        [SerializeField] MonoBehaviour providerBehaviour;
        [SerializeField] BlockiverseNetworkAvatarRig fallbackRig;
        [SerializeField] MetaAvatarPresentationMode presentationMode = MetaAvatarPresentationMode.RemoteThirdPerson;
        [SerializeField] Transform headTrackingSource;
        [SerializeField] Transform leftHandTrackingSource;
        [SerializeField] Transform rightHandTrackingSource;
        [SerializeField] bool hideFirstPersonHead = true;
        [SerializeField] string lastFallbackReason = "Meta avatar provider is not configured.";

        IBlockiverseMetaAvatarProvider provider;

        public MetaAvatarPresentationMode PresentationMode => presentationMode;
        public bool AvatarReady => provider != null && provider.IsAvatarReady;
        public string LastFallbackReason => lastFallbackReason;

        void Awake()
        {
            ResolveReferences();
            ConfigureProvider();
            RefreshAvatarState();
        }

        void LateUpdate()
        {
            ResolveReferences();
            provider?.TickProvider();
            RefreshAvatarState();
        }

        public void Configure(
            IBlockiverseMetaAvatarProvider avatarProvider,
            BlockiverseNetworkAvatarRig avatarFallbackRig,
            MetaAvatarTrackingSources sources,
            MetaAvatarPresentationMode mode)
        {
            provider = avatarProvider;
            providerBehaviour = avatarProvider as MonoBehaviour;
            fallbackRig = avatarFallbackRig;
            presentationMode = mode;
            headTrackingSource = sources.Head;
            leftHandTrackingSource = sources.LeftHand;
            rightHandTrackingSource = sources.RightHand;
            hideFirstPersonHead = mode == MetaAvatarPresentationMode.LocalFirstPerson;
            ConfigureProvider();
            RefreshAvatarState();
        }

        public void Configure(
            MonoBehaviour avatarProvider,
            BlockiverseNetworkAvatarRig avatarFallbackRig,
            Transform head,
            Transform leftHand,
            Transform rightHand,
            MetaAvatarPresentationMode mode)
        {
            Configure(
                avatarProvider as IBlockiverseMetaAvatarProvider,
                avatarFallbackRig,
                new MetaAvatarTrackingSources(head, leftHand, rightHand),
                mode);
        }

        public void RefreshAvatarState()
        {
            ResolveReferences();

            bool avatarReady = provider != null && provider.IsAvatarReady;
            if (fallbackRig != null)
            {
                fallbackRig.ConfigureFallbackProxy(true);
                fallbackRig.SetMetaAvatarAvailable(avatarReady);
            }

            lastFallbackReason = avatarReady
                ? string.Empty
                : provider?.FallbackReason ?? "Meta avatar provider is not configured.";
        }

        public bool TryRecordLocalStream(out byte[] streamData)
        {
            ResolveReferences();
            streamData = Array.Empty<byte>();

            if (presentationMode != MetaAvatarPresentationMode.LocalFirstPerson || provider == null)
                return false;

            return provider.TryRecordStream(out streamData);
        }

        public void ApplyRemoteStream(byte[] streamData)
        {
            ResolveReferences();

            if (presentationMode != MetaAvatarPresentationMode.RemoteThirdPerson || provider == null)
                return;

            provider.ApplyStreamData(streamData ?? Array.Empty<byte>());
            RefreshAvatarState();
        }

        void ResolveReferences()
        {
            if (fallbackRig == null)
                fallbackRig = GetComponent<BlockiverseNetworkAvatarRig>();

            if (provider == null)
            {
                if (providerBehaviour is IBlockiverseMetaAvatarProvider typedProvider)
                    provider = typedProvider;
                else
                    provider = FindProviderOnGameObject();
            }

            if (providerBehaviour == null && provider is MonoBehaviour behaviour)
                providerBehaviour = behaviour;
        }

        IBlockiverseMetaAvatarProvider FindProviderOnGameObject()
        {
            foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour is IBlockiverseMetaAvatarProvider typedProvider)
                    return typedProvider;
            }

            return null;
        }

        void ConfigureProvider()
        {
            ResolveTrackingSources();
            provider?.Configure(
                new MetaAvatarTrackingSources(headTrackingSource, leftHandTrackingSource, rightHandTrackingSource),
                presentationMode,
                hideFirstPersonHead && presentationMode == MetaAvatarPresentationMode.LocalFirstPerson);
        }

        void ResolveTrackingSources()
        {
            if (headTrackingSource != null || presentationMode != MetaAvatarPresentationMode.LocalFirstPerson)
                return;

            Transform cameraOffset = transform.Find("Camera Offset");
            if (cameraOffset == null)
                return;

            headTrackingSource = cameraOffset.Find("Main Camera");
            leftHandTrackingSource = cameraOffset.Find("Left Controller");
            rightHandTrackingSource = cameraOffset.Find("Right Controller");
        }
    }
}
