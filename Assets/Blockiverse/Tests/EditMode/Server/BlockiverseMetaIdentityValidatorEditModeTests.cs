using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blockiverse.Server;
using NUnit.Framework;

namespace Blockiverse.Tests.Server.EditMode
{
    public sealed class BlockiverseMetaIdentityValidatorEditModeTests
    {
        static bool ValidateBlocking(BlockiverseMetaIdentityValidator validator, ulong userId, string nonce)
        {
            using var done = new ManualResetEventSlim();
            bool result = false;
            validator.Validate(userId, nonce, valid => { result = valid; done.Set(); });
            Assert.That(done.Wait(TimeSpan.FromSeconds(5)), Is.True, "validator never completed");
            return result;
        }

        [Test]
        public void ValidResponseAuthorizes()
        {
            var validator = new BlockiverseMetaIdentityValidator(
                "12345", "secret", (url, fields) => Task.FromResult("{\"is_valid\": true}"));

            Assert.That(ValidateBlocking(validator, 42, "nonce"), Is.True);
        }

        [Test]
        public void InvalidAndErrorResponsesRefuse()
        {
            foreach (string body in new[]
            {
                "{\"is_valid\": false}",
                "{\"error\":{\"message\":\"bad nonce\"}}",
                "{\"is_valid\": \"true\"}",
                "",
                null,
            })
            {
                var validator = new BlockiverseMetaIdentityValidator(
                    "12345", "secret", (url, fields) => Task.FromResult(body));
                Assert.That(ValidateBlocking(validator, 42, "nonce"), Is.False, $"body: {body ?? "(null)"}");
            }
        }

        [Test]
        public void TransportFailureRefusesRatherThanAdmits()
        {
            var validator = new BlockiverseMetaIdentityValidator(
                "12345", "secret",
                (url, fields) => Task.FromException<string>(new InvalidOperationException("endpoint down")));

            Assert.That(ValidateBlocking(validator, 42, "nonce"), Is.False,
                "An unreachable Meta endpoint must never become an open door.");
        }

        [Test]
        public void RequestCarriesTheRightFields()
        {
            IReadOnlyDictionary<string, string> seen = null;
            string seenUrl = null;
            var validator = new BlockiverseMetaIdentityValidator(
                "12345", "app-secret",
                (url, fields) => { seenUrl = url; seen = fields; return Task.FromResult("{\"is_valid\":true}"); });

            ValidateBlocking(validator, 42, "the-nonce");

            Assert.That(seenUrl, Is.EqualTo(BlockiverseMetaIdentityValidator.ValidationEndpoint));
            Assert.That(seen["access_token"], Is.EqualTo("OC|12345|app-secret"));
            Assert.That(seen["nonce"], Is.EqualTo("the-nonce"));
            Assert.That(seen["user_id"], Is.EqualTo("42"));
        }

        [Test]
        public void MissingInputsRefuseWithoutCallingTheEndpoint()
        {
            bool called = false;
            var validator = new BlockiverseMetaIdentityValidator(
                "12345", "secret", (url, fields) => { called = true; return Task.FromResult("{\"is_valid\":true}"); });

            Assert.That(ValidateBlocking(validator, 0, "nonce"), Is.False);
            Assert.That(ValidateBlocking(validator, 42, " "), Is.False);
            Assert.That(called, Is.False);
        }

        [Test]
        public void MissingCredentialsAreAConstructionError()
        {
            Assert.Throws<ArgumentException>(() => new BlockiverseMetaIdentityValidator("", "secret"));
            Assert.Throws<ArgumentException>(() => new BlockiverseMetaIdentityValidator("12345", " "));
        }
    }
}
