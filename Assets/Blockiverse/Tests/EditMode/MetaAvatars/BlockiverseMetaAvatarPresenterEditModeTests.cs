using System;
using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.MetaAvatars.EditMode
{
    public sealed class BlockiverseMetaAvatarPresenterEditModeTests
    {
        GameObject root;
        GameObject head;
        GameObject leftHand;
        GameObject rightHand;

        [TearDown]
        public void TearDown()
        {
            Destroy(root);
            Destroy(head);
            Destroy(leftHand);
            Destroy(rightHand);
        }

        [Test]
        public void LocalFirstPersonAvatarKeepsFallbackUntilProviderReady()
        {
            BlockiverseMetaAvatarPresenter presenter = CreatePresenter(out BlockiverseNetworkAvatarRig fallbackRig, out FakeMetaAvatarProvider provider);
            var sources = CreateTrackingSources();

            presenter.Configure(provider, fallbackRig, sources, MetaAvatarPresentationMode.LocalFirstPerson);
            provider.IsAvatarReady = false;
            provider.FallbackReason = "Meta user avatar is still loading.";

            presenter.RefreshAvatarState();

            Assert.That(provider.Mode, Is.EqualTo(MetaAvatarPresentationMode.LocalFirstPerson));
            Assert.That(provider.HideFirstPersonHead, Is.True);
            Assert.That(provider.Sources.Head, Is.SameAs(head.transform));
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.True);
            Assert.That(fallbackRig.MetaAvatarAvailable, Is.False);
            Assert.That(presenter.LastFallbackReason, Is.EqualTo(provider.FallbackReason));

            provider.IsAvatarReady = true;
            presenter.RefreshAvatarState();

            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.False);
            Assert.That(fallbackRig.MetaAvatarAvailable, Is.True);
            Assert.That(presenter.LastFallbackReason, Is.Empty);
        }

        [Test]
        public void RemoteAvatarKeepsFallbackUntilStreamDataIsApplied()
        {
            BlockiverseMetaAvatarPresenter presenter = CreatePresenter(out BlockiverseNetworkAvatarRig fallbackRig, out FakeMetaAvatarProvider provider);
            var stream = new byte[] { 1, 2, 3, 4 };

            presenter.Configure(provider, fallbackRig, MetaAvatarTrackingSources.Empty, MetaAvatarPresentationMode.RemoteThirdPerson);
            presenter.RefreshAvatarState();

            Assert.That(provider.Mode, Is.EqualTo(MetaAvatarPresentationMode.RemoteThirdPerson));
            Assert.That(provider.HideFirstPersonHead, Is.False);
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.True);

            presenter.ApplyRemoteStream(stream);
            presenter.RefreshAvatarState();

            Assert.That(provider.LastAppliedStream, Is.EqualTo(stream));
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.False);
            Assert.That(fallbackRig.MetaAvatarAvailable, Is.True);
        }

        [Test]
        public void LocalStreamCaptureReturnsProviderBytesWhenAvatarIsReady()
        {
            BlockiverseMetaAvatarPresenter presenter = CreatePresenter(out BlockiverseNetworkAvatarRig fallbackRig, out FakeMetaAvatarProvider provider);
            byte[] expected = { 9, 8, 7 };

            provider.IsAvatarReady = true;
            provider.RecordedStream = expected;
            presenter.Configure(provider, fallbackRig, MetaAvatarTrackingSources.Empty, MetaAvatarPresentationMode.LocalFirstPerson);

            Assert.That(presenter.TryRecordLocalStream(out byte[] actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        BlockiverseMetaAvatarPresenter CreatePresenter(
            out BlockiverseNetworkAvatarRig fallbackRig,
            out FakeMetaAvatarProvider provider)
        {
            root = new GameObject("Meta Avatar Presenter Test");
            fallbackRig = root.AddComponent<BlockiverseNetworkAvatarRig>();
            provider = root.AddComponent<FakeMetaAvatarProvider>();
            return root.AddComponent<BlockiverseMetaAvatarPresenter>();
        }

        MetaAvatarTrackingSources CreateTrackingSources()
        {
            head = new GameObject("Main Camera");
            leftHand = new GameObject("Left Controller");
            rightHand = new GameObject("Right Controller");
            return new MetaAvatarTrackingSources(head.transform, leftHand.transform, rightHand.transform);
        }

        static void Destroy(GameObject gameObject)
        {
            if (gameObject != null)
                UnityEngine.Object.DestroyImmediate(gameObject);
        }

        sealed class FakeMetaAvatarProvider : MonoBehaviour, IBlockiverseMetaAvatarProvider
        {
            public bool IsAvatarReady { get; set; }
            public string FallbackReason { get; set; } = string.Empty;
            public bool HideFirstPersonHead { get; private set; }
            public MetaAvatarPresentationMode Mode { get; private set; }
            public MetaAvatarTrackingSources Sources { get; private set; }
            public byte[] LastAppliedStream { get; private set; } = Array.Empty<byte>();
            public byte[] RecordedStream { get; set; } = Array.Empty<byte>();

            public void Configure(MetaAvatarTrackingSources sources, MetaAvatarPresentationMode mode, bool hideFirstPersonHead)
            {
                Sources = sources;
                Mode = mode;
                HideFirstPersonHead = hideFirstPersonHead;
            }

            public void TickProvider()
            {
            }

            public bool TryRecordStream(out byte[] streamData)
            {
                streamData = RecordedStream;
                return IsAvatarReady && streamData.Length > 0;
            }

            public void ApplyStreamData(byte[] streamData)
            {
                LastAppliedStream = streamData;
                IsAvatarReady = streamData is { Length: > 0 };
            }
        }
    }
}
