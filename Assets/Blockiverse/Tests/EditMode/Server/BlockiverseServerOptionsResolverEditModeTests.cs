using System.Collections.Generic;
using Blockiverse.Server;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode.Server
{
    // The resolver is pure by design -- no file I/O, no Environment, no UnityEngine -- so every
    // precedence rule is provable with no fixtures and no player loop.
    public sealed class BlockiverseServerOptionsResolverEditModeTests
    {
        static BlockiverseServerOptionsResolver.Resolution Resolve(
            IReadOnlyList<string> arguments = null,
            IReadOnlyDictionary<string, string> environment = null,
            IReadOnlyDictionary<string, string> file = null) =>
            BlockiverseServerOptionsResolver.Resolve(
                arguments ?? new List<string>(),
                environment ?? new Dictionary<string, string>(),
                file ?? new Dictionary<string, string>());

        [Test]
        public void DefaultsResolveWithoutAnySource()
        {
            BlockiverseServerOptionsResolver.Resolution resolution = Resolve();

            Assert.That(resolution.Succeeded, Is.True, string.Join("; ", resolution.Problems));
            Assert.That(resolution.Options.Port, Is.EqualTo(7777));
            Assert.That(resolution.Options.ListenAddress, Is.EqualTo("0.0.0.0"));
            Assert.That(resolution.Options.WorldSeed, Is.Null, "Unset means random on first create, not zero.");
        }

        [Test]
        public void PrecedenceRunsFileThenEnvironmentThenArguments()
        {
            BlockiverseServerOptionsResolver.Resolution resolution = Resolve(
                file: new Dictionary<string, string> { ["server.port"] = "1111" },
                environment: new Dictionary<string, string> { ["BLOCKIVERSE_SERVER_PORT"] = "2222" },
                arguments: new List<string> { "--server-port", "3333" });

            Assert.That(resolution.Succeeded, Is.True, string.Join("; ", resolution.Problems));
            Assert.That(resolution.Options.Port, Is.EqualTo(3333),
                "A one-off command line must beat a deployment's environment, which must beat the image's file.");
        }

        [Test]
        public void EnvironmentBeatsFileWhenNoArgumentIsGiven()
        {
            BlockiverseServerOptionsResolver.Resolution resolution = Resolve(
                file: new Dictionary<string, string> { ["world.dir"] = "/from-file" },
                environment: new Dictionary<string, string> { ["BLOCKIVERSE_WORLD_DIR"] = "/from-env" });

            Assert.That(resolution.Options.WorldDirectory, Is.EqualTo("/from-env"));
        }

        [Test]
        public void ArgumentAcceptsBothSpacedAndInlineValues()
        {
            Assert.That(Resolve(arguments: new List<string> { "--world-name", "Spaced" }).Options.WorldName,
                Is.EqualTo("Spaced"));
            Assert.That(Resolve(arguments: new List<string> { "--world-name=Inline" }).Options.WorldName,
                Is.EqualTo("Inline"));
        }

        [Test]
        public void UnknownFileKeyIsFatalRatherThanIgnored()
        {
            BlockiverseServerOptionsResolver.Resolution resolution =
                Resolve(file: new Dictionary<string, string> { ["world.dr"] = "/data" });

            Assert.That(resolution.Succeeded, Is.False,
                "A silently defaulted typo is the same class of bug as a world that never grows.");
            Assert.That(resolution.Problems[0], Does.Contain("world.dr"));
            Assert.That(resolution.Problems[0], Does.Contain("world.dir"),
                "A near miss should be suggested; that is the whole value of failing loudly here.");
        }

        [Test]
        public void UnknownEnvironmentVariableAndArgumentAreAlsoFatal()
        {
            Assert.That(Resolve(environment: new Dictionary<string, string> { ["BLOCKIVERSE_WORLD_DIRR"] = "/x" }).Succeeded,
                Is.False);
            Assert.That(Resolve(arguments: new List<string> { "--not-a-setting", "1" }).Succeeded, Is.False);
        }

        [Test]
        public void NonBlockiverseEnvironmentVariablesAreIgnored()
        {
            BlockiverseServerOptionsResolver.Resolution resolution = Resolve(
                environment: new Dictionary<string, string> { ["PATH"] = "/usr/bin", ["HOME"] = "/root" });

            Assert.That(resolution.Succeeded, Is.True,
                "Only BLOCKIVERSE_-prefixed variables are ours; the rest of the environment is not a config error.");
        }

        [Test]
        public void UnparsableAndOutOfRangeValuesAreReported()
        {
            Assert.That(Resolve(file: new Dictionary<string, string> { ["server.port"] = "not-a-port" }).Succeeded, Is.False);
            Assert.That(Resolve(file: new Dictionary<string, string> { ["server.port"] = "70000" }).Succeeded, Is.False);
            Assert.That(Resolve(file: new Dictionary<string, string> { ["persistence.autosave_seconds"] = "5" }).Succeeded,
                Is.False, "Below the 30s floor.");
            Assert.That(Resolve(file: new Dictionary<string, string> { ["world.preset"] = "infinite" }).Succeeded, Is.False);
            Assert.That(Resolve(file: new Dictionary<string, string> { ["security.require_secret"] = "maybe" }).Succeeded, Is.False);
        }

        [Test]
        public void MissingArgumentValueIsReported()
        {
            Assert.That(Resolve(arguments: new List<string> { "--server-port" }).Succeeded, Is.False);
            Assert.That(Resolve(arguments: new List<string> { "--server-port", "--world-name", "x" }).Succeeded, Is.False,
                "The next token starting with -- is the next option, not this one's value.");
        }

        [Test]
        public void RequireSecretWithoutASecretRefusesToStart()
        {
            BlockiverseServerOptionsResolver.Resolution resolution =
                Resolve(file: new Dictionary<string, string> { ["security.require_secret"] = "true" });

            Assert.That(resolution.Succeeded, Is.False,
                "An operator asking for a private server must never be handed an open one.");
        }

        [Test]
        public void TlsWithoutMaterialRefusesToStart()
        {
            BlockiverseServerOptionsResolver.Resolution resolution =
                Resolve(file: new Dictionary<string, string> { ["security.tls.enabled"] = "true" });

            Assert.That(resolution.Succeeded, Is.False);
        }

        [Test]
        public void BooleanSpellingsAreAccepted()
        {
            foreach (string yes in new[] { "true", "yes", "on", "1", "TRUE" })
            {
                Assert.That(Resolve(file: new Dictionary<string, string> { ["persistence.save_on_stop"] = yes })
                    .Options.SaveOnStop, Is.True, $"'{yes}' should mean true");
            }

            foreach (string no in new[] { "false", "no", "off", "0" })
            {
                Assert.That(Resolve(file: new Dictionary<string, string> { ["persistence.save_on_stop"] = no })
                    .Options.SaveOnStop, Is.False, $"'{no}' should mean false");
            }
        }

        [Test]
        public void PlayerCountAboveTheSupportedCeilingIsHonouredButAdvised()
        {
            BlockiverseServerOptionsResolver.Resolution resolution =
                Resolve(file: new Dictionary<string, string> { ["server.max_players"] = "16" });

            Assert.That(resolution.Succeeded, Is.True, "The ceiling is operator's risk, not an error.");
            Assert.That(resolution.Options.MaxPlayers, Is.EqualTo(16));
            Assert.That(resolution.Options.ExceedsSupportedPlayerCount, Is.True);
            Assert.That(resolution.Options.Advisories(), Is.Not.Empty);
        }

        [Test]
        public void NameMappingsAreMechanicalInBothDirections()
        {
            Assert.That(BlockiverseServerOptionsResolver.EnvironmentNameFor("world.dir"), Is.EqualTo("BLOCKIVERSE_WORLD_DIR"));
            Assert.That(BlockiverseServerOptionsResolver.ArgumentNameFor("world.dir"), Is.EqualTo("--world-dir"));
            Assert.That(BlockiverseServerOptionsResolver.EnvironmentNameFor("security.tls.cert_path"),
                Is.EqualTo("BLOCKIVERSE_SECURITY_TLS_CERT_PATH"));

            // Every key must be reachable from all three sources or a setting exists in one form
            // and not another, which is exactly the kind of gap nobody notices.
            foreach (string key in BlockiverseServerOptionsResolver.KnownKeys)
            {
                Assert.That(BlockiverseServerOptionsResolver.EnvironmentNameFor(key), Does.StartWith("BLOCKIVERSE_"));
                Assert.That(BlockiverseServerOptionsResolver.ArgumentNameFor(key), Does.StartWith("--"));
                Assert.That(BlockiverseServerOptionsResolver.ArgumentNameFor(key), Does.Not.Contain("_"));
            }
        }

        [Test]
        public void ConfigTextParsesKeyValuePairsAndComments()
        {
            var problems = new List<string>();
            Dictionary<string, string> values = BlockiverseServerOptionsResolver.ParseConfigText(
                "# a comment\n\nserver.port = 7788\nworld.name = My World\n", problems);

            Assert.That(problems, Is.Empty);
            Assert.That(values["server.port"], Is.EqualTo("7788"));
            Assert.That(values["world.name"], Is.EqualTo("My World"), "Values may contain spaces.");
        }

        [Test]
        public void ConfigTextReportsMalformedLines()
        {
            var problems = new List<string>();
            BlockiverseServerOptionsResolver.ParseConfigText("this is not a pair\n", problems);

            Assert.That(problems, Is.Not.Empty);
            Assert.That(problems[0], Does.Contain("line 1"));
        }

        [Test]
        public void DescribeNeverLeaksTheSecret()
        {
            BlockiverseServerOptionsResolver.Resolution resolution =
                Resolve(file: new Dictionary<string, string> { ["server.secret"] = "hunter2-do-not-print-me" });

            Assert.That(resolution.Options.Describe(), Does.Not.Contain("hunter2-do-not-print-me"),
                "The boot banner goes to logs an operator may paste into an issue.");
        }
    }
}
