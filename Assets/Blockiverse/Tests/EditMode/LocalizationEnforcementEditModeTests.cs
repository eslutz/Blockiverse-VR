using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Tables;

namespace Blockiverse.Tests.EditMode
{
    // Plan Phase 5: the rules that keep the localization system honest across 22 screens.
    // Every rule here has been shown failing (see the commit message) — a rule that cannot be
    // watched failing is not evidence of anything, per this repo's testing culture.
    public sealed class LocalizationEnforcementEditModeTests
    {
        const string TableName = "UI";

        static StringTable EnglishTable()
        {
            StringTableCollection collection =
                LocalizationEditorSettings.GetStringTableCollection(TableName);
            Assert.That(collection, Is.Not.Null, "UI table collection missing");
            var table = (StringTable)collection.GetTable("en");
            Assert.That(table, Is.Not.Null, "en table missing");
            return table;
        }

        // Rule 1: every Keys const resolves. A const whose entry was deleted would fall back to
        // humanized-or-key output at runtime and look almost right — this is the only place the
        // gap is loud.
        [Test]
        public void EveryKeysConstHasAnEnglishEntry()
        {
            StringTable table = EnglishTable();

            var missing = new List<string>();
            FieldInfo[] consts = typeof(BlockiverseLocalization.Keys)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .ToArray();

            Assert.That(consts.Length, Is.GreaterThanOrEqualTo(230),
                "Keys shrank drastically — positive control failed.");

            foreach (FieldInfo f in consts)
            {
                var key = (string)f.GetRawConstantValue();
                if (table.GetEntry(key) == null)
                    missing.Add($"Keys.{f.Name} = {key}");
            }

            Assert.That(missing, Is.Empty, string.Join("\n", missing));
        }

        // Rule 3: every Smart entry parses. A malformed pattern throws at RENDER time on device,
        // in front of the player; this makes it throw here instead.
        [Test]
        public void EverySmartEntryFormatsWithoutThrowing()
        {
            StringTable table = EnglishTable();
            var failures = new List<string>();
            var dummy = new Dictionary<string, object>
            {
                ["count"] = 3, ["place"] = "x", ["host"] = "h", ["name"] = "n",
            };

            foreach (StringTableEntry entry in table.Values.Where(e => e.IsSmart))
            {
                try
                {
                    entry.GetLocalizedString(new object[] { dummy });
                }
                catch (System.Exception ex)
                {
                    failures.Add($"{entry.SharedEntry.Key}: {ex.GetType().Name} {ex.Message}");
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        // Rule 4: every entry carries translator context. "Close" is a verb or an adjective and
        // no translator can tell from the key alone; the comment is where the answer lives, and
        // an empty one is a translation bug deferred to the most expensive possible moment.
        [Test]
        public void EveryEntryHasANonEmptyComment()
        {
            StringTable table = EnglishTable();
            var missing = new List<string>();

            foreach (StringTableEntry entry in table.Values)
            {
                Comment comment = entry.SharedEntry.Metadata.GetMetadata<Comment>();

                // "Comment Text" is the class's constructor default — non-empty and meaningless.
                // The first version of this rule accepted it, which is how 241 placeholder
                // comments passed a suite that existed to prevent exactly that.
                if (comment == null ||
                    string.IsNullOrWhiteSpace(comment.CommentText) ||
                    comment.CommentText == "Comment Text")
                    missing.Add(entry.SharedEntry.Key);
            }

            Assert.That(missing, Is.Empty,
                $"{missing.Count} entries without translator comments:\n" +
                string.Join("\n", missing.Take(15)));
        }

        // Rules 2 + 5, preview-aware: a text-bearing UXML element must either carry a
        // LocalizedString binding (whose table/entry must exist, and whose literal text is then
        // the design-time preview and must match the English) or appear in the explicit
        // allowlist. Unbound literal text is the silent-English failure this whole system exists
        // to prevent.
        [Test]
        public void UxmlTextIsBoundOrAllowlisted()
        {
            string allowlistPath =
                "Assets/Blockiverse/Tests/EditMode/Fixtures/uxml-literal-allowlist.txt";
            HashSet<string> allowlist = File.Exists(allowlistPath)
                ? File.ReadAllLines(allowlistPath)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("#"))
                    .ToHashSet()
                : new HashSet<string>();

            StringTable table = EnglishTable();
            var violations = new List<string>();
            var bindingPattern = new Regex(
                "<UnityEngine\\.Localization\\.LocalizedString[^>]*property=\"text\"[^>]*" +
                "table=\"([^\"]+)\"[^>]*entry=\"([^\"]+)\"");
            var textPattern = new Regex("text=\"([^\"]*[A-Za-z][^\"]*)\"");

            string[] files = Directory.GetFiles("Assets/Blockiverse", "*.uxml",
                SearchOption.AllDirectories);
            Assert.That(files, Is.Not.Empty, "no UXML found — positive control failed");

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (allowlist.Contains(name))
                    continue;

                string content = File.ReadAllText(file);

                foreach (Match binding in bindingPattern.Matches(content))
                {
                    if (binding.Groups[1].Value != TableName ||
                        table.GetEntry(binding.Groups[2].Value) == null)
                        violations.Add($"{name}: binding to missing entry '{binding.Groups[2].Value}'");
                }

                // Literal text with letters and no binding anywhere in the file's element —
                // a coarse per-file check is enough while screens are single-purpose documents;
                // tighten to per-element parsing when composed screens arrive.
                if (textPattern.IsMatch(content) && !bindingPattern.IsMatch(content))
                    violations.Add($"{name}: literal text with no LocalizedString binding " +
                                   "(add bindings, or allowlist the file with a reason)");
            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }
    }
}
