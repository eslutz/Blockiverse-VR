using System;
using System.Collections.Generic;
using System.IO;
using Blockiverse.Persistence;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode.Server
{
    // The property under test is narrow and important: an operator may declare a save root during
    // startup, and nothing after that can add one. Widening WHERE saves may go must not widen WHEN
    // that can be decided.
    public sealed class BlockiverseSavePathPolicyEditModeTests
    {
        string temporaryRoot;
        Func<IEnumerable<string>> previousProvider;

        [SetUp]
        public void SetUp()
        {
            previousProvider = BlockiverseSavePathPolicy.DefaultRootProvider;
            BlockiverseSavePathPolicy.ResetForTesting();
            temporaryRoot = Path.Combine(Path.GetTempPath(), "blockiverse-savepolicy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            BlockiverseSavePathPolicy.DefaultRootProvider = () => Array.Empty<string>();
        }

        [TearDown]
        public void TearDown()
        {
            BlockiverseSavePathPolicy.ResetForTesting();
            BlockiverseSavePathPolicy.DefaultRootProvider = previousProvider;

            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, recursive: true);
            }
            catch (Exception)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }

        [Test]
        public void PathOutsideEveryRootIsRefused()
        {
            Assert.That(BlockiverseSavePathPolicy.IsTrusted(Path.Combine(temporaryRoot, "world.vxlworld")), Is.False);
        }

        [Test]
        public void RegisteredRootMakesItsChildrenTrusted()
        {
            Assert.That(BlockiverseSavePathPolicy.TryRegisterAdditionalRoot(temporaryRoot, out string failure), Is.True, failure);
            Assert.That(BlockiverseSavePathPolicy.IsTrusted(Path.Combine(temporaryRoot, "world.vxlworld")), Is.True);
        }

        [Test]
        public void DefaultRootsAreHonoured()
        {
            BlockiverseSavePathPolicy.DefaultRootProvider = () => new[] { temporaryRoot };
            Assert.That(BlockiverseSavePathPolicy.IsTrusted(Path.Combine(temporaryRoot, "world.vxlworld")), Is.True);
        }

        [Test]
        public void RelativeMissingAndEmptyPathsAreRefused()
        {
            Assert.That(BlockiverseSavePathPolicy.TryRegisterAdditionalRoot("relative/path", out string relative), Is.False);
            Assert.That(relative, Does.Contain("absolute"));

            string absent = Path.Combine(temporaryRoot, "does-not-exist");
            Assert.That(BlockiverseSavePathPolicy.TryRegisterAdditionalRoot(absent, out string missing), Is.False);
            Assert.That(missing, Does.Contain("does not exist"),
                "Creating it here would let a typo silently become a new save location.");

            Assert.That(BlockiverseSavePathPolicy.TryRegisterAdditionalRoot("", out _), Is.False);
        }

        [Test]
        public void RegistrationAfterSealingThrows()
        {
            BlockiverseSavePathPolicy.SealForSession();

            Assert.Throws<InvalidOperationException>(
                () => BlockiverseSavePathPolicy.TryRegisterAdditionalRoot(temporaryRoot, out _),
                "A late registration must be a loud bug, not a quiet redirect of where the world is written.");
        }

        [Test]
        public void SiblingDirectoryWithASharedPrefixIsNotUnderTheRoot()
        {
            // "/data-other" must not count as being inside "/data": a prefix match without a
            // separator check is the classic way a path allow-list leaks.
            Assert.That(BlockiverseSavePathPolicy.IsUnderRoot("/data-other/world", "/data"), Is.False);
            Assert.That(BlockiverseSavePathPolicy.IsUnderRoot("/data/world", "/data"), Is.True);
            Assert.That(BlockiverseSavePathPolicy.IsUnderRoot("/data", "/data"), Is.True);
        }

        [Test]
        public void RegisteringTheSameRootTwiceIsIdempotent()
        {
            BlockiverseSavePathPolicy.TryRegisterAdditionalRoot(temporaryRoot, out _);
            BlockiverseSavePathPolicy.TryRegisterAdditionalRoot(temporaryRoot, out _);

            Assert.That(BlockiverseSavePathPolicy.RegisteredAdditionalRoots.Count, Is.EqualTo(1));
        }
    }
}
