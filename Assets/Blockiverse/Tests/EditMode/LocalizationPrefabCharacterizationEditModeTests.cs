using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Plan enforcement test #6, landing FIRST (before any shim work): freezes the 133 serialized
    // BlockiverseLocalizedText.localizationKey values in the XR rig prefab.
    //
    // Why this exists: those keys were assigned by an English-string reverse lookup that is
    // first-wins over duplicate values — the shipped mapping is arbitrary (every "Close" button
    // is bound to ui.action.error.close) but it IS the shipped behavior, and an accidental
    // bootstrapper re-run under a changed lookup would silently rewrite all 133 components.
    // This test makes that rewrite loud. A DELIBERATE regeneration updates the fixture in the
    // same commit, with the reason in the commit message.
    public sealed class LocalizationPrefabCharacterizationEditModeTests
    {
        const string PrefabPath = "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab";
        const string FixturePath =
            "Assets/Blockiverse/Tests/EditMode/Fixtures/prefab-localization-keys.txt";

        static readonly Regex KeyLine = new(@"^\s*localizationKey:\s*(\S+)\s*$", RegexOptions.Multiline);

        [Test]
        public void SerializedLocalizationKeysMatchTheFrozenFixture()
        {
            Assert.That(File.Exists(PrefabPath), Is.True);
            Assert.That(File.Exists(FixturePath), Is.True);

            Dictionary<string, int> actual = KeyLine.Matches(File.ReadAllText(PrefabPath))
                .Select(m => m.Groups[1].Value)
                .GroupBy(k => k)
                .ToDictionary(g => g.Key, g => g.Count());

            var expected = new Dictionary<string, int>();

            foreach (string raw in File.ReadAllLines(FixturePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                int space = line.IndexOf(' ');
                expected[line[(space + 1)..]] = int.Parse(line[..space]);
            }

            // Positive controls before any comparison: an empty fixture or an unparsed prefab
            // would make the multiset comparison pass vacuously.
            Assert.That(expected.Values.Sum(), Is.EqualTo(134), "fixture lost components");
            Assert.That(expected.Count, Is.EqualTo(75), "fixture lost distinct keys");
            Assert.That(expected["ui.action.error.close"], Is.EqualTo(11),
                "the known Close-collision count changed in the fixture itself");

            var drift = new List<string>();

            foreach (string key in expected.Keys.Union(actual.Keys).OrderBy(k => k))
            {
                expected.TryGetValue(key, out int want);
                actual.TryGetValue(key, out int have);
                if (want != have)
                    drift.Add($"{key}: fixture {want}, prefab {have}");
            }

            Assert.That(drift, Is.Empty,
                "Serialized localization bindings changed. If this was a deliberate bootstrapper " +
                "re-run, regenerate the fixture in this same commit:\n" + string.Join("\n", drift));
        }
    }
}
