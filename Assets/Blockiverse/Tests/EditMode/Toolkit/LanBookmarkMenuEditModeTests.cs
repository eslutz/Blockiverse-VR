using System.IO;
using Blockiverse.Networking;
using Blockiverse.Persistence;
using Blockiverse.UI;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // The bookmark menu on the LAN screen (PR #343 review: RememberedServers /
    // JoinRememberedServer existed as dead APIs with no UI). The rows mirror
    // BlockiverseServerBookmarkStore's most-recent-first order, so row index == store index
    // and a tap routes through the previously-unwired JoinRememberedServer.
    //
    // The store has no path seam — it reads Application.persistentDataPath/servers.json —
    // so these tests snapshot and restore the real file around every run. Not doing that
    // would either leak fixture servers into the developer's own bookmark list or wipe it.
    public sealed class LanBookmarkMenuEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/LanMultiplayerScreen.uxml";

        GameObject screenObject;
        GameObject sessionObject;
        LanMultiplayerScreenController controller;
        VisualElement root;
        string bookmarkFilePath;
        byte[] preservedBookmarkFile;

        [SetUp]
        public void SetUp()
        {
            bookmarkFilePath = Path.Combine(
                Application.persistentDataPath, BlockiverseServerBookmarkStore.FileName);
            preservedBookmarkFile = File.Exists(bookmarkFilePath)
                ? File.ReadAllBytes(bookmarkFilePath)
                : null;
            File.Delete(bookmarkFilePath);

            screenObject = new GameObject("LAN Bookmark Screen");
            controller = screenObject.AddComponent<LanMultiplayerScreenController>();

            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, "LanMultiplayerScreen.uxml did not load — document path drifted.");
            root = tree.Instantiate();
            controller.AttachForTest(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (preservedBookmarkFile != null)
                File.WriteAllBytes(bookmarkFilePath, preservedBookmarkFile);
            else
                File.Delete(bookmarkFilePath);

            if (screenObject != null)
                Object.DestroyImmediate(screenObject);

            if (sessionObject != null)
                Object.DestroyImmediate(sessionObject);
        }

        Button BookmarkSlot(int oneBasedIndex) => root.Q<Button>($"bv-lan-bookmark-slot-{oneBasedIndex}");

        // Positive control: an empty store must hide the whole section — and this proves the
        // suite is not asserting against rows some other mechanism left visible.
        [Test]
        public void EmptyStoreHidesEveryRowAndTheHeading()
        {
            controller.RefreshBookmarkList();

            Assert.That(root.Q<Label>("bv-lan-bookmark-heading").style.display.value,
                Is.EqualTo(DisplayStyle.None));

            for (int slot = 1; slot <= LanMultiplayerScreenController.BookmarkSlotCount; slot++)
                Assert.That(BookmarkSlot(slot).style.display.value, Is.EqualTo(DisplayStyle.None),
                    $"slot {slot} should be hidden with an empty store");
        }

        [Test]
        public void RememberedServersRenderMostRecentFirstWithTheHeading()
        {
            BlockiverseServerBookmarkStore.Remember("10.0.0.1:7777");
            BlockiverseServerBookmarkStore.Remember("10.0.0.2:7777");

            controller.RefreshBookmarkList();

            Assert.That(root.Q<Label>("bv-lan-bookmark-heading").style.display.value,
                Is.EqualTo(DisplayStyle.Flex));
            // Remember moves the newest to the front, so slot 1 is the LAST server joined.
            Assert.That(BookmarkSlot(1).style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(BookmarkSlot(1).text, Does.Contain("10.0.0.2"));
            Assert.That(BookmarkSlot(2).text, Does.Contain("10.0.0.1"));
            Assert.That(BookmarkSlot(3).style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void NicknamedServersShowTheNicknameWithTheAddress()
        {
            BlockiverseServerBookmarkStore.Remember("play.example.com:7777", nickname: "Eric's Server");

            controller.RefreshBookmarkList();

            Assert.That(BookmarkSlot(1).text, Does.Contain("Eric's Server"));
            Assert.That(BookmarkSlot(1).text, Does.Contain("play.example.com"));
        }

        [Test]
        public void TheListCapsAtTheSlotCountWithoutHidingTheRest()
        {
            for (int server = 1; server <= LanMultiplayerScreenController.BookmarkSlotCount + 2; server++)
                BlockiverseServerBookmarkStore.Remember($"10.0.1.{server}:7777");

            controller.RefreshBookmarkList();

            for (int slot = 1; slot <= LanMultiplayerScreenController.BookmarkSlotCount; slot++)
                Assert.That(BookmarkSlot(slot).style.display.value, Is.EqualTo(DisplayStyle.Flex),
                    $"slot {slot} should show one of the recent servers");

            // Most recent first: the newest survives the cap, the oldest two fall off-screen
            // (but stay in the store — rejoining by address resurfaces them).
            Assert.That(BookmarkSlot(1).text, Does.Contain("10.0.1.6"));
            Assert.That(BlockiverseServerBookmarkStore.Load().Count,
                Is.EqualTo(LanMultiplayerScreenController.BookmarkSlotCount + 2));
        }

        [Test]
        public void JoinRememberedServerSeedsTheFieldsFromTheBookmark()
        {
            // The port is deliberately unparseable (the parser accepts any colon-free
            // string as a bare host, so only a bad port actually fails TryParse): the fields
            // are seeded BEFORE the join attempt, and the rejected address stops the flow at
            // the join-failed status instead of reaching NetworkManager.StartClient, which
            // NREs in EditMode with no transport wired. The successful-join path (including the
            // store re-order) is what the invalid bookmark cannot cover; it needs a live
            // Netcode session and belongs to the PlayMode multiplayer suite.
            BlockiverseServerBookmarkStore.Remember("10.0.0.9:notaport", secret: "hunter2", useTls: true);
            CreateSession();
            controller.RefreshBookmarkList();

            controller.JoinRememberedServer(0);

            Assert.That(root.Q<TextField>("bv-lan-address").value, Is.EqualTo("10.0.0.9:notaport"));
            Assert.That(root.Q<TextField>("bv-lan-secret").value, Is.EqualTo("hunter2"));
            Assert.That(root.Q<Toggle>("bv-lan-encryption").value, Is.True);
        }

        [Test]
        public void JoinRememberedServerRejectsAnInvalidIndexWithoutThrowing()
        {
            controller.RefreshBookmarkList();

            Assert.DoesNotThrow(() => controller.JoinRememberedServer(0));
            Assert.DoesNotThrow(() => controller.JoinRememberedServer(-1));
        }

        // The row wiring uses the discovery slots' exact-subscription bookkeeping; the balance
        // invariant is what proves a re-attach cannot leave a row firing twice per tap.
        [Test]
        public void ReattachKeepsTheCallbackBalanceAtOne()
        {
            BlockiverseServerBookmarkStore.Remember("10.0.0.1:7777");

            controller.AttachForTest(root);
            controller.AttachForTest(root);

            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));
        }

        BlockiverseNetworkSession CreateSession()
        {
            sessionObject = new GameObject("Network Session");
            sessionObject.AddComponent<UnityTransport>();
            sessionObject.AddComponent<NetworkManager>();
            BlockiverseNetworkSession session = sessionObject.AddComponent<BlockiverseNetworkSession>();
            controller.Configure(session);
            return session;
        }
    }
}
