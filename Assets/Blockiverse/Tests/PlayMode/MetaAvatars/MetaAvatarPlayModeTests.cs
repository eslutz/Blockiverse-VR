using System.Collections;
using System.Collections.Generic;
using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    /// <summary>
    /// PlayMode coverage for the editor-mockable Meta avatar seam (N4). Uses
    /// <see cref="BlockiverseEditorMockMetaAvatarProvider"/> so presenter, fallback-switching,
    /// and the stream fragment/reassembly forwarding path run in Editor PlayMode without the
    /// Meta Avatar SDK.
    /// </summary>
    public sealed class MetaAvatarPlayModeTests
    {
        // PlayMode coverage for the editor-mockable Meta avatar provider seam (N4).
        readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
            {
                if (go != null)
                    Object.Destroy(go);
            }

            spawned.Clear();
        }

        BlockiverseMetaAvatarPresenter CreatePresenter(
            MetaAvatarPresentationMode mode,
            out BlockiverseEditorMockMetaAvatarProvider provider,
            out BlockiverseNetworkAvatarRig fallbackRig,
            bool attachProvider = true)
        {
            var root = new GameObject("Mock Meta Avatar");
            spawned.Add(root);

            fallbackRig = root.AddComponent<BlockiverseNetworkAvatarRig>();
            provider = attachProvider ? root.AddComponent<BlockiverseEditorMockMetaAvatarProvider>() : null;
            var presenter = root.AddComponent<BlockiverseMetaAvatarPresenter>();
            presenter.Configure(provider, fallbackRig, MetaAvatarTrackingSources.Empty, mode);
            return presenter;
        }

        static byte[] MakePattern(int length)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)(i * 31 + 7);

            return data;
        }

        [UnityTest]
        public IEnumerator LocalSelfAvatar_InitializesWhenProviderReady()
        {
            BlockiverseMetaAvatarPresenter presenter = CreatePresenter(
                MetaAvatarPresentationMode.LocalFirstPerson, out var provider, out var fallbackRig);

            provider.IsAvatarReady = true;
            provider.SetRecordedStreamSize(2048);
            presenter.RefreshAvatarState();
            yield return null;

            Assert.That(presenter.AvatarReady, Is.True);
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.False);
            Assert.That(presenter.TryRecordLocalStream(out byte[] recorded), Is.True);
            Assert.That(recorded.Length, Is.EqualTo(2048));
        }

        [UnityTest]
        public IEnumerator RemoteAvatar_AppearsWhenStreamArrives()
        {
            BlockiverseMetaAvatarPresenter presenter = CreatePresenter(
                MetaAvatarPresentationMode.RemoteThirdPerson, out var provider, out var fallbackRig);

            presenter.RefreshAvatarState();
            yield return null;

            // No stream yet: remote avatar not ready, fallback proxy showing.
            Assert.That(presenter.AvatarReady, Is.False);
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.True);

            presenter.ApplyRemoteStream(MakePattern(512));
            yield return null;

            Assert.That(presenter.AvatarReady, Is.True);
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.False);
            Assert.That(provider.LastAppliedStream.Length, Is.EqualTo(512));
        }

        [UnityTest]
        public IEnumerator FallbackProxy_EngagesWhenProviderUnavailable()
        {
            BlockiverseMetaAvatarPresenter presenter = CreatePresenter(
                MetaAvatarPresentationMode.RemoteThirdPerson, out _, out var fallbackRig,
                attachProvider: false);

            presenter.RefreshAvatarState();
            yield return null;

            Assert.That(presenter.AvatarReady, Is.False);
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.True);
            Assert.That(presenter.LastFallbackReason, Does.Contain("not configured"));
        }

        [UnityTest]
        public IEnumerator RemoteStreamAboveLegacyCap_FragmentsAndReassemblesToRemotePresenter()
        {
            BlockiverseMetaAvatarPresenter presenter = CreatePresenter(
                MetaAvatarPresentationMode.RemoteThirdPerson, out var provider, out _);

            // A representative stream larger than the old 8 KiB single-message cap that Step 9 removed.
            byte[] data = MakePattern(12000);
            Assert.That(data.Length, Is.GreaterThan(8 * 1024));
            Assert.That(data.Length, Is.LessThanOrEqualTo(MetaAvatarStreamMessage.MaxStreamBytes));

            // Fragment exactly as the relay's send path does.
            var fragments = new List<MetaAvatarStreamMessage>();
            int fragmentCount = MetaAvatarStreamReassembler.Fragment(7UL, 1.0, 1u, data, fragments);
            Assert.That(fragmentCount, Is.GreaterThan(1));

            // Reassemble exactly as the relay's receive path does, then forward to the remote presenter.
            var reassembler = new MetaAvatarStreamReassembler();
            byte[] complete = null;
            foreach (MetaAvatarStreamMessage fragment in fragments)
            {
                Assert.That(fragment.HasValidPayload, Is.True);
                if (reassembler.TryReassemble(fragment, out byte[] assembled, out double _))
                    complete = assembled;
            }

            Assert.That(complete, Is.Not.Null);
            presenter.ApplyRemoteStream(complete);
            yield return null;

            CollectionAssert.AreEqual(data, provider.LastAppliedStream);
            Assert.That(presenter.AvatarReady, Is.True);
        }

        [UnityTest]
        public IEnumerator RemoteAvatar_HidesWhenPoseOrStreamStopsAndReappearsOnResume()
        {
            var root = new GameObject("Remote Avatar Staleness Test");
            spawned.Add(root);

            var fallbackRig = root.AddComponent<BlockiverseNetworkAvatarRig>();
            fallbackRig.IsSpawnedForTest = true;
            var provider = root.AddComponent<BlockiverseEditorMockMetaAvatarProvider>();
            var presenter = root.AddComponent<BlockiverseMetaAvatarPresenter>();
            presenter.Configure(provider, fallbackRig, MetaAvatarTrackingSources.Empty, MetaAvatarPresentationMode.RemoteThirdPerson);

            // Create Meta Horizon Avatar Entity child so we can test its visibility
            var metaEntity = new GameObject("Meta Horizon Avatar Entity");
            metaEntity.transform.SetParent(root.transform);

            // Set up a relay so we can trigger stream receipt
            var relay = root.AddComponent<MetaAvatarStreamRelay>();
            
            // Prime local stream arrival
            presenter.ApplyRemoteStream(MakePattern(512));
            yield return null;

            // Prime pose arrival so hasRemotePose becomes true
            var receivePoseMethod = typeof(BlockiverseNetworkAvatarRig).GetMethod("ApplyRemotePose",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            Assert.That(receivePoseMethod, Is.Not.Null);
            receivePoseMethod.Invoke(fallbackRig, new object[] { BlockiverseNetworkAvatarRig.AvatarPose.Default });
            yield return null;

            Assert.That(presenter.AvatarReady, Is.True);
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.False);
            Assert.That(fallbackRig.IsPoseStale, Is.False);
            Assert.That(fallbackRig.IsStreamStale, Is.False);
            Assert.That(metaEntity.activeSelf, Is.True);

            // 1. Test Pose Staleness for Meta Avatar (not fallback proxy)
            var lastPoseField = typeof(BlockiverseNetworkAvatarRig).GetProperty("LastRemotePoseTime", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.That(lastPoseField, Is.Not.Null);
            
            // Age the pose timestamp
            lastPoseField.SetValue(fallbackRig, Time.unscaledTime - 4.0f);
            yield return null; // Let LateUpdate run

            Assert.That(fallbackRig.IsPoseStale, Is.True);
            // Verify visibility is turned off on Meta Avatar Entity when stale
            Assert.That(metaEntity.activeSelf, Is.False);

            // Receive new pose -> should restore visibility
            receivePoseMethod.Invoke(fallbackRig, new object[] { BlockiverseNetworkAvatarRig.AvatarPose.Default });
            yield return null;

            Assert.That(fallbackRig.IsPoseStale, Is.False);
            Assert.That(metaEntity.activeSelf, Is.True);

            // 2. Test Stream Staleness for Meta Avatar
            fallbackRig.SetStreamStale(true);
            yield return null;

            Assert.That(fallbackRig.IsStreamStale, Is.True);
            Assert.That(metaEntity.activeSelf, Is.False);

            // Resume stream -> restores visibility
            fallbackRig.SetStreamStale(false);
            yield return null;

            Assert.That(fallbackRig.IsStreamStale, Is.False);
            Assert.That(metaEntity.activeSelf, Is.True);

            // 3. Test Staleness for Fallback Proxy (when Meta Avatar is NOT active)
            provider.IsAvatarReady = false;
            yield return null;

            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.True);
            Assert.That(fallbackRig.FallbackRoot.gameObject.activeSelf, Is.True);

            // Make pose stale
            lastPoseField.SetValue(fallbackRig, Time.unscaledTime - 4.0f);
            yield return null;

            Assert.That(fallbackRig.IsPoseStale, Is.True);
            Assert.That(fallbackRig.FallbackRoot.gameObject.activeSelf, Is.False);

            // Receive new pose -> restores visibility
            receivePoseMethod.Invoke(fallbackRig, new object[] { BlockiverseNetworkAvatarRig.AvatarPose.Default });
            yield return null;

            Assert.That(fallbackRig.IsPoseStale, Is.False);
            Assert.That(fallbackRig.FallbackRoot.gameObject.activeSelf, Is.True);
        }
    }
}