using NUnit.Framework;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Blockiverse.Tests.EditMode
{
    // SPIKE gates S1 and S6 (plan Phase 0). Deliberately NO [SetUp] and no Play mode:
    // ten existing test files assert literal English through the live resolver with exactly this
    // cold shape, so whichever resolve path passes here is the one the facade must use.
    public sealed class LocalizationSpikeEditModeTests
    {
        const string Table = "UI_Spike";

        static Locale English()
        {
            // THE S1 FINDING: AvailableLocales is empty until the package's async initialization
            // completes. Nothing initializes it in EditMode, so the first caller must force it —
            // this line is what the facade will do lazily on first Text() call. Without it, the
            // first tests to run see no locales while later tests coast on the completed init
            // (observed: alphabetically-first tests failed, later ones passed).
            LocalizationSettings.InitializationOperation.WaitForCompletion();

            var locales = LocalizationSettings.AvailableLocales;
            Assert.That(locales, Is.Not.Null, "AvailableLocales unavailable in EditMode");
            Locale en = locales.GetLocale("en");
            Assert.That(en, Is.Not.Null, "en locale not registered");
            return en;
        }

        // S1 primary: the explicit-locale overload — the path that cannot depend on any
        // selection having run.
        [Test]
        public void S1_ExplicitLocaleResolvesSynchronouslyCold()
        {
            string result = LocalizationSettings.StringDatabase
                .GetLocalizedStringAsync(Table, "spike.plain", English())
                .WaitForCompletion();

            Assert.That(result, Is.EqualTo("Hello from the table"));
        }

        // S1 finding, pinned: the no-argument path does NOT resolve in EditMode even after
        // forced initialization and with ProjectLocale set — no locale-selection pass runs
        // outside Play mode. This is WHY the facade always passes the locale explicitly
        // (Selected ?? Project ?? en). If this test ever starts failing because the package
        // began resolving here, the facade can be simplified — that would be good news, not a
        // regression, which is why the assertion carries this comment.
        [Test]
        public void S1_NoArgumentPathDoesNotResolveInEditMode()
        {
            LocalizationSettings.InitializationOperation.WaitForCompletion();

            string result = LocalizationSettings.StringDatabase
                .GetLocalizedStringAsync(Table, "spike.plain")
                .WaitForCompletion();

            Assert.That(result, Is.Null,
                "The no-arg path started resolving in EditMode — the facade's explicit-locale " +
                "requirement may be removable. Investigate before celebrating.");
        }

        [Test]
        public void S6_PositionalPatternFormatsLikeStringFormat()
        {
            string result = LocalizationSettings.StringDatabase
                .GetLocalizedStringAsync(Table, "spike.positional", English(),
                    arguments: new object[] { 1, 12, 24 })
                .WaitForCompletion();

            Assert.That(result, Is.EqualTo("Slots 1-12 / 24"));
        }

        [Test]
        public void S6_NamedSmartPatternResolvesNamedArguments()
        {
            string result = LocalizationSettings.StringDatabase
                .GetLocalizedStringAsync(Table, "spike.smart", English(),
                    arguments: new object[] { new { count = 5, place = "the crate" } })
                .WaitForCompletion();

            Assert.That(result, Is.EqualTo("5 items in the crate"));
        }

        [Test]
        public void MissingKeyIsDetectableWithoutThrowing()
        {
            string result = LocalizationSettings.StringDatabase
                .GetLocalizedStringAsync(Table, "spike.does_not_exist", English())
                .WaitForCompletion();

            Assert.That(result, Is.Not.EqualTo("Hello from the table"));
        }
    }
}
