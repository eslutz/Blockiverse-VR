using System;
using System.Collections.Generic;
using Blockiverse.Core;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseLogEditModeTests
    {
        CapturingLogSink sink;

        [SetUp]
        public void SetUp()
        {
            sink = new CapturingLogSink();
            BlockiverseLog.SetSinkForTesting(sink);
            BlockiverseLog.DevelopmentInfoEnabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            BlockiverseLog.ResetSinkForTesting();
        }

        [Test]
        public void InfoLogsAreSuppressedWhenDevelopmentInfoLoggingIsDisabled()
        {
            BlockiverseLog.DevelopmentInfoEnabled = false;

            BlockiverseLog.Info(BlockiverseLogCategory.Renderer, "hidden renderer details");

            Assert.That(sink.Entries, Is.Empty);
        }

        [Test]
        public void WarningsStillReachSinkWhenDevelopmentInfoLoggingIsDisabled()
        {
            BlockiverseLog.DevelopmentInfoEnabled = false;

            BlockiverseLog.Warning(BlockiverseLogCategory.Persistence, "world save failure");

            Assert.That(sink.Entries, Has.Count.EqualTo(1));
            Assert.That(sink.Entries[0].Level, Is.EqualTo(LogType.Warning));
            Assert.That(sink.Entries[0].FormattedMessage, Does.StartWith("[Blockiverse][Persistence]"));
        }

        [Test]
        public void LogEntryPreservesCategoryLevelMessageExceptionAndContext()
        {
            var context = ScriptableObject.CreateInstance<ScriptableObject>();
            var exception = new InvalidOperationException("diagnostic failure");

            try
            {
                BlockiverseLog.Error(BlockiverseLogCategory.Assets, "atlas validation failed", exception, context);

                Assert.That(sink.Entries, Has.Count.EqualTo(1));
                BlockiverseLogEntry entry = sink.Entries[0];
                Assert.That(entry.Category, Is.EqualTo(BlockiverseLogCategory.Assets));
                Assert.That(entry.Level, Is.EqualTo(LogType.Error));
                Assert.That(entry.Message, Is.EqualTo("atlas validation failed"));
                Assert.That(entry.Exception, Is.SameAs(exception));
                Assert.That(entry.Context, Is.SameAs(context));
                Assert.That(entry.FormattedMessage, Is.EqualTo("[Blockiverse][Assets] atlas validation failed"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(context);
            }
        }

        [Test]
        public void SceneLookupFindsInactiveComponentsAndNamedGameObjects()
        {
            var target = new GameObject("Scene Lookup Probe");
            target.SetActive(false);
            LookupProbe probe = target.AddComponent<LookupProbe>();

            try
            {
                Assert.That(BlockiverseSceneLookup.Find<LookupProbe>(FindObjectsInactive.Include), Is.SameAs(probe));
                target.SetActive(true);
                Assert.That(BlockiverseSceneLookup.FindGameObject("Scene Lookup Probe"), Is.SameAs(target));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        sealed class LookupProbe : MonoBehaviour
        {
        }

        sealed class CapturingLogSink : IBlockiverseLogSink
        {
            public readonly List<BlockiverseLogEntry> Entries = new();

            public void Log(BlockiverseLogEntry entry)
            {
                Entries.Add(entry);
            }
        }
    }
}
