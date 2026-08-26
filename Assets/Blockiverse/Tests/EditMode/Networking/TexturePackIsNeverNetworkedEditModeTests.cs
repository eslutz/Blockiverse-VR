using System.Collections.Generic;
using System.IO;
using System.Text;
using Blockiverse.Core;
using Blockiverse.Networking;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode.Networking
{
    // Texture selection must never travel between peers, and these tests exist to keep it that way
    // rather than to prove it once.
    //
    // Two reasons it matters, and only one of them is technical. A client cannot use a token for a
    // pack it does not have, so sending one buys nothing -- but the obvious "fix" for that
    // (transfer the pack too) turns a local render of somebody's art into a redistribution of it.
    // Keeping the value off the wire entirely is what stops that conversation from ever starting.
    //
    // The guards are deliberately structural. A behavioural test only catches the field that
    // exists today; these catch the one somebody adds next year.
    public sealed class TexturePackIsNeverNetworkedEditModeTests
    {
        [Test]
        public void TheApprovalPayloadCarriesNoTextureValue()
        {
            var session = new UnityEngine.GameObject("approval-payload").AddComponent<BlockiverseNetworkSession>();
            try
            {
                session.Configure(BlockiverseNetworkConfig.Default.WithJoinCode("texture-test"));

                string payload = Encoding.UTF8.GetString(session.CreateApprovalPayload());

                foreach (string builtIn in BlockTextureSetIds.All)
                {
                    Assert.That(payload, Does.Not.Contain(builtIn),
                        $"The approval payload contains the texture set id '{builtIn}'.");
                }

                Assert.That(payload, Does.Not.Contain(BlockiverseTextureSelection.PackTokenPrefix),
                    "The approval payload contains a texture pack token.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(session.gameObject);
            }
        }

        [Test]
        public void TheWorldSnapshotHeaderHasNoTextureField()
        {
            // Reflection rather than a byte comparison: this fails the day someone ADDS a texture
            // field, which is the failure worth catching. A size assertion alone would not say why.
            foreach (var property in typeof(MultiplayerChunkAuthoritySync.WorldSnapshotHeader).GetProperties())
            {
                Assert.That(property.Name.ToLowerInvariant(), Does.Not.Contain("texture"),
                    $"WorldSnapshotHeader gained a texture field ('{property.Name}'). Texture "
                    + "selection is local to each peer and must never be replicated.");
            }

            foreach (var field in typeof(MultiplayerChunkAuthoritySync.WorldSnapshotHeader).GetFields())
            {
                Assert.That(field.Name.ToLowerInvariant(), Does.Not.Contain("texture"),
                    $"WorldSnapshotHeader gained a texture field ('{field.Name}').");
            }
        }

        [Test]
        public void OnlyTheThreeExpectedNetworkingFilesMentionTextureSelectionAtAll()
        {
            // A file-set guard, and the cheapest one that actually works: it fails the moment a
            // texture value is threaded into a new message type, before anyone has to notice that
            // a struct grew a field.
            //
            // The three legitimate mentions are the local-only interface method, the host reading
            // its own save, and the client applying its own preference.
            var expected = new HashSet<string>
            {
                "IMultiplayerWorldContext.cs",
                "MultiplayerWorldPersistence.cs",
                "MultiplayerChunkAuthoritySync.cs",
            };

            var actual = new HashSet<string>();
            foreach (string path in Directory.GetFiles("Assets/Blockiverse/Scripts/Networking", "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(path);
                if (text.Contains("TextureSet") || text.Contains("TexturePack") || text.Contains("TextureSelection"))
                    actual.Add(Path.GetFileName(path));
            }

            Assert.That(actual, Is.EquivalentTo(expected),
                "The set of Networking files touching texture selection changed. If a new file "
                + "needs it, confirm the value stays LOCAL and never reaches a payload, snapshot, "
                + "RPC or NetworkVariable -- then update this list.");
        }

        [Test]
        public void TheClientAppliesItsOwnPreferenceRatherThanInheritingAStaleValue()
        {
            // Before this existed, a joining client rendered with whatever CreativeWorldManager's
            // serialized field happened to hold -- its default, or a leftover from a single-player
            // session earlier in the same process. That was accidental; this makes it specified.
            string original = BlockiverseTexturePackPreferences.Token;
            try
            {
                BlockiverseTexturePackPreferences.Token = BlockTextureSetIds.Ai;
                Assert.That(BlockiverseTexturePackPreferences.Token, Is.EqualTo(BlockTextureSetIds.Ai));

                BlockiverseTexturePackPreferences.Token = "pack:mossy_stones";
                Assert.That(BlockiverseTexturePackPreferences.Token, Is.EqualTo("pack:mossy_stones"),
                    "The preference did not survive a pack token, so a client could not choose one.");
            }
            finally
            {
                BlockiverseTexturePackPreferences.Token = original;
            }
        }

        [Test]
        public void ThePreferenceNormalizesAnythingHandEditedOrLeftByAnOlderBuild()
        {
            string original = BlockiverseTexturePackPreferences.Token;
            try
            {
                UnityEngine.PlayerPrefs.SetString(BlockiverseTexturePackPreferences.TokenPrefsKey, "pack:../../etc");
                Assert.That(BlockiverseTexturePackPreferences.Token, Is.EqualTo(BlockTextureSetIds.Default),
                    "A malformed stored preference escaped normalization.");

                UnityEngine.PlayerPrefs.SetString(BlockiverseTexturePackPreferences.TokenPrefsKey, "");
                Assert.That(BlockiverseTexturePackPreferences.Token, Is.EqualTo(BlockTextureSetIds.Default));
            }
            finally
            {
                BlockiverseTexturePackPreferences.Token = original;
            }
        }
    }
}
