using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // Guards the one defect class this suite is structurally blind to: a stylesheet rule whose
    // selector matches nothing.
    //
    // The Toolkit screens shipped to a headset with every text field rendering as a bright white
    // rectangle containing no visible text. Cause: `.hs-field` styles the OUTER TextField, while
    // the inner input element draws its own background from the default theme and owns the text,
    // so the stock near-white input covered our panel colour and the bone-coloured text vanished
    // into it. All 1399 EditMode tests passed, and could not have done otherwise — EditMode has no
    // panel, resolves no styles, and every assertion in the Toolkit suite is about structure or
    // behaviour. "The tests are green" said nothing whatsoever about what the screen looked like.
    //
    // A selector that matches nothing fails silently in exactly the same way as the bug it was
    // written to fix, so this pins both halves: that a real TextField still exposes the hooks our
    // USS names, and that our USS still names them.
    public sealed class ToolkitTextFieldStyleEditModeTests
    {
        const string BaseStylesheetPath = "Assets/Blockiverse/UI/Styles/Base.uss";

        // What UI Toolkit calls the inner input. Both are asserted because Base.uss matches on
        // both, and a Unity version that renamed one would otherwise take the styling away
        // without any test going red.
        const string InputElementName = "unity-text-input";
        const string InputElementClass = "unity-base-text-field__input";

        static VisualElement FindInput(TextField field)
        {
            foreach (VisualElement element in field.Query<VisualElement>().Build())
            {
                if (element == field)
                    continue;

                if (element.name == InputElementName || element.ClassListContains(InputElementClass))
                    return element;
            }

            return null;
        }

        static string DescribeTree(VisualElement root, int depth = 0)
        {
            var sb = new StringBuilder();
            sb.Append(' ', depth * 2)
              .Append(root.GetType().Name)
              .Append(" name='").Append(root.name).Append('\'')
              .Append(" classes=[").Append(string.Join(",", root.GetClasses())).AppendLine("]");

            foreach (VisualElement child in root.Children())
                sb.Append(DescribeTree(child, depth + 1));

            return sb.ToString();
        }

        [Test]
        public void ARealTextFieldExposesTheInnerInputOurStylesheetTargets()
        {
            var field = new TextField();
            field.AddToClassList("hs-field");

            VisualElement input = FindInput(field);

            Assert.That(input, Is.Not.Null,
                "No inner input element found on a TextField. Base.uss styles the input by name " +
                $"'#{InputElementName}' and class '.{InputElementClass}'; if Unity renamed it, the " +
                "field renders with the stock white theme background and the fix is silently gone.\n" +
                "Actual tree:\n" + DescribeTree(field));

            // Assert BOTH hooks, not just the one that happened to match: Base.uss relies on each
            // independently, and a half-match would still style the field today while leaving a
            // trap for the next upgrade.
            Assert.That(input.name, Is.EqualTo(InputElementName),
                "Inner input name changed.\n" + DescribeTree(field));
            Assert.That(input.ClassListContains(InputElementClass), Is.True,
                $"Inner input no longer carries '{InputElementClass}'.\n" + DescribeTree(field));
        }

        [Test]
        public void BaseStylesheetStillStylesTheInnerInputAndNotJustTheOuterField()
        {
            Assert.That(File.Exists(BaseStylesheetPath), Is.True);
            string uss = File.ReadAllText(BaseStylesheetPath);

            Assert.That(uss, Does.Contain($".hs-field #{InputElementName}"),
                "Base.uss no longer styles the text field's inner input by name. Styling only " +
                "'.hs-field' leaves the stock near-white input covering it — the exact defect " +
                "that reached the headset.");
            Assert.That(uss, Does.Contain($".hs-field .{InputElementClass}"),
                "Base.uss no longer styles the text field's inner input by class.");

            // A background alone is not enough: bone text on the stock white was half the defect,
            // so the rule must set a colour too.
            int inputRuleStart = uss.IndexOf($".hs-field #{InputElementName}", System.StringComparison.Ordinal);
            int inputRuleEnd = uss.IndexOf('}', inputRuleStart);
            string inputRule = uss.Substring(inputRuleStart, inputRuleEnd - inputRuleStart);

            Assert.That(inputRule, Does.Contain("background-color"),
                "The inner-input rule must set a background, or the theme's own shows through.");
            Assert.That(inputRule, Does.Contain("color:"),
                "The inner-input rule must set a text colour; the field inherited --hs-ink and " +
                "rendered bone-on-white, which is why the text looked absent rather than wrong.");
        }

        [Test]
        public void EveryTextFieldInShippedDocumentsCarriesTheStyledClass()
        {
            var offenders = new List<string>();

            foreach (string path in Directory.GetFiles("Assets/Blockiverse/UI/Documents", "*.uxml"))
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    if (!line.Contains("TextField"))
                        continue;

                    // A TextField without hs-field gets no inner-input styling at all, so it would
                    // ship with the same white box even after this fix.
                    if (!line.Contains("hs-field"))
                        offenders.Add($"{Path.GetFileName(path)}: {line.Trim()}");
                }
            }

            Assert.That(offenders, Is.Empty,
                "TextField(s) not carrying 'hs-field', so the inner-input styling never applies:\n  " +
                string.Join("\n  ", offenders));
        }
    }
}
