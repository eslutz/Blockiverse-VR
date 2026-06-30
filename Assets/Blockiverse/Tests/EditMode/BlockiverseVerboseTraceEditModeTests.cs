using System;
using System.Collections.Generic;
using System.IO;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseVerboseTraceEditModeTests
    {
        readonly List<GameObject> gameObjects = new();
        CapturingTraceSink sink;
        string diagnosticsDirectory;

        [SetUp]
        public void SetUp()
        {
            diagnosticsDirectory = Path.Combine(
                Path.GetTempPath(),
                "blockiverse-trace-diagnostics-" + Guid.NewGuid().ToString("N"));
            sink = new CapturingTraceSink();
            BlockiverseTrace.SetSinkForTesting(sink);
            BlockiverseTrace.SetSessionIdForTesting("trace-session");
            BlockiverseTrace.SetClockForTesting(() => 12.5d, () => 42);
            BlockiverseTrace.SetDiagnosticsDirectoryForTesting(diagnosticsDirectory);
            BlockiverseTrace.Enabled = true;
            PlayerPrefs.DeleteKey(BlockiverseTrace.VerboseTracePlayerPrefsKey);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in gameObjects)
            {
                if (gameObject != null)
                    UnityEngine.Object.DestroyImmediate(gameObject);
            }

            gameObjects.Clear();
            PlayerPrefs.DeleteKey(BlockiverseTrace.VerboseTracePlayerPrefsKey);
            if (!string.IsNullOrEmpty(diagnosticsDirectory) && Directory.Exists(diagnosticsDirectory))
                Directory.Delete(diagnosticsDirectory, recursive: true);

            BlockiverseTrace.ResetForTesting();
            BlockiverseRuntimeState.Reset();
        }

        [Test]
        public void RuntimeTraceEnablementIsOptInByDefault()
        {
            Assert.That(BlockiverseTrace.ShouldEnableFromRuntimeFlag(), Is.False);
        }

        [Test]
        public void RuntimeTraceCanBeEnabledWithPlayerPrefs()
        {
            PlayerPrefs.SetInt(BlockiverseTrace.VerboseTracePlayerPrefsKey, 1);

            Assert.That(BlockiverseTrace.ShouldEnableFromRuntimeFlag(), Is.True);
        }

        [Test]
        public void RuntimeTraceCanBeEnabledWithMarkerFile()
        {
            Directory.CreateDirectory(BlockiverseTrace.DiagnosticsDirectoryPath);
            File.WriteAllText(BlockiverseTrace.EnableVerboseTraceMarkerPath, string.Empty);

            Assert.That(BlockiverseTrace.ShouldEnableFromRuntimeFlag(), Is.True);
        }

        [Test]
        public void TraceFacadeSuppressesRecordsWhenDisabled()
        {
            BlockiverseTrace.Enabled = false;

            BlockiverseTrace.Write("feedback", "audio.cue", "{\"cue\":\"UiSelect\"}");

            Assert.That(sink.Records, Is.Empty);
        }

        [Test]
        public void TraceFacadeWritesStructuredRecordsToTheConfiguredSink()
        {
            BlockiverseTrace.Write("feedback", "audio.cue", "{\"cue\":\"UiSelect\"}");

            Assert.That(sink.Records, Has.Count.EqualTo(1));
            BlockiverseTraceRecord record = sink.Records[0];
            Assert.That(record.SessionId, Is.EqualTo("trace-session"));
            Assert.That(record.RealtimeSinceStartup, Is.EqualTo(12.5d));
            Assert.That(record.FrameCount, Is.EqualTo(42));
            Assert.That(record.Channel, Is.EqualTo("feedback"));
            Assert.That(record.EventName, Is.EqualTo("audio.cue"));
            Assert.That(record.PayloadJson, Is.EqualTo("{\"cue\":\"UiSelect\"}"));
            Assert.That(record.ToJsonLine(), Does.Contain("\"channel\":\"feedback\""));
            Assert.That(record.ToJsonLine(), Does.Contain("\"payload\":{\"cue\":\"UiSelect\"}"));
        }

        [Test]
        public void RollingTraceFileSinkWritesJsonlAndStartsANewFileAtTheByteLimit()
        {
            string directory = Path.Combine(Path.GetTempPath(), "blockiverse-trace-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                using var fileSink = new BlockiverseRollingTraceFileSink(
                    directory,
                    "test-session",
                    maxFileBytes: 180,
                    maxFileCount: 3);

                for (int i = 0; i < 6; i++)
                {
                    fileSink.Write(new BlockiverseTraceRecord(
                        "test-session",
                        i,
                        i,
                        "player",
                        "snapshot",
                        "{\"index\":" + i + ",\"message\":\"escaped \\\"quote\\\"\"}"));
                }

                string[] files = Directory.GetFiles(directory, "blockiverse-trace-test-session-*.jsonl");
                Assert.That(files.Length, Is.GreaterThanOrEqualTo(2));
                Assert.That(files.Length, Is.LessThanOrEqualTo(3));
                Assert.That(File.ReadAllText(files[0]), Does.Contain("\"sessionId\":\"test-session\""));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void VerboseTraceControllerCanCaptureAPlayerSnapshotOnDemand()
        {
            Blockiverse.Core.BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);

            GameObject rig = CreateGameObject("Trace Rig");
            rig.transform.position = new Vector3(1.25f, 2.5f, 3.75f);
            rig.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            BlockiverseVerboseTraceController controller = rig.AddComponent<BlockiverseVerboseTraceController>();

            controller.CaptureSnapshotNow();

            Assert.That(sink.Records, Has.Count.EqualTo(1));
            Assert.That(sink.Records[0].Channel, Is.EqualTo("player"));
            Assert.That(sink.Records[0].EventName, Is.EqualTo("snapshot"));
            Assert.That(sink.Records[0].PayloadJson, Does.Contain("\"rigPosition\""));
            Assert.That(sink.Records[0].PayloadJson, Does.Contain("\"x\":1.25"));
            Assert.That(sink.Records[0].PayloadJson, Does.Contain("\"allowWorldInput\":true"));
        }

        [Test]
        public void VerboseTraceControllerLogsCreativeBlockMutationEventsWithCanonicalIds()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 4, seed: 1234);
            var position = new BlockPosition(1, 1, 1);
            world.SetBlock(position, BlockRegistry.MeadowTurf, trackChange: false);

            GameObject interactionObject = CreateGameObject("Creative Interaction");
            CreativeInteractionController interaction = interactionObject.AddComponent<CreativeInteractionController>();
            interaction.Configure(world, BlockRegistry.Default, null, null, null);

            GameObject rig = CreateGameObject("Trace Rig");
            BlockiverseVerboseTraceController tracer = rig.AddComponent<BlockiverseVerboseTraceController>();
            tracer.Configure(null, null, interaction, null, null, null, null);

            interaction.TryBreakBlock(position);

            Assert.That(sink.Records, Has.Count.EqualTo(1));
            Assert.That(sink.Records[0].EventName, Is.EqualTo("interaction.block_mutation"));
            Assert.That(sink.Records[0].PayloadJson, Does.Contain("\"previousBlock\":\"meadow_turf\""));
            Assert.That(sink.Records[0].PayloadJson, Does.Contain("\"newBlock\":\"air\""));
        }

        [Test]
        public void VerboseTraceControllerLogsAudioCueEvents()
        {
            GameObject rig = CreateGameObject("Trace Rig");
            rig.AddComponent<AudioSource>();
            BlockiverseAudioCuePlayer audioCuePlayer = rig.AddComponent<BlockiverseAudioCuePlayer>();
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.UiSelect, AudioClip.Create("ui_select", 16, 1, 44100, false));

            BlockiverseVerboseTraceController tracer = rig.AddComponent<BlockiverseVerboseTraceController>();
            tracer.Configure(null, null, null, audioCuePlayer, null, null, null);

            audioCuePlayer.PlayCue(BlockiverseAudioCue.UiSelect);

            Assert.That(sink.Records, Has.Count.EqualTo(1));
            Assert.That(sink.Records[0].EventName, Is.EqualTo("feedback.audio_cue"));
            Assert.That(sink.Records[0].PayloadJson, Does.Contain("\"cue\":\"UiSelect\""));
        }

        [Test]
        public void VfxCuePlayerRaisesCuePlayedForTraceSubscribers()
        {
            GameObject root = CreateGameObject("VFX Root");
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();
            BlockiverseVfxCuePlayer player = root.AddComponent<BlockiverseVfxCuePlayer>();
            pool.ConfigureForTests(poolSize: 1);
            player.Configure(pool, settings: null);

            BlockiverseVfxCue? played = null;
            Vector3? position = null;
            player.CuePlayed += (cue, worldPosition) =>
            {
                played = cue;
                position = worldPosition;
            };

            player.PlayCue(BlockiverseVfxCue.BlockBreakDust, Vector3.one);

            Assert.That(played, Is.EqualTo(BlockiverseVfxCue.BlockBreakDust));
            Assert.That(position, Is.EqualTo(Vector3.one));
        }

        GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            gameObjects.Add(gameObject);
            return gameObject;
        }

        sealed class CapturingTraceSink : IBlockiverseTraceSink
        {
            public readonly List<BlockiverseTraceRecord> Records = new();

            public void Write(BlockiverseTraceRecord record)
            {
                Records.Add(record);
            }
        }
    }
}
