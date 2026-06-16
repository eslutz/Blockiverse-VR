using System.IO;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseVfxCuePlayerEditModeTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
                Object.DestroyImmediate(root);
            BlockiverseRuntimeState.Reset();
        }

        [Test]
        public void VfxPoolPrewarmsAndReusesOneShotParticleSystems()
        {
            root = new GameObject("VFX Root");
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();
            pool.ConfigureForTests(poolSize: 2);

            Assert.That(pool.PrewarmedCount, Is.EqualTo(2));

            pool.Play(BlockiverseVfxCue.BlockBreakDust, Vector3.zero, Color.white, 1.0f);
            pool.Play(BlockiverseVfxCue.BlockPlacePuff, Vector3.one, Color.gray, 1.0f);
            pool.Play(BlockiverseVfxCue.ResourceSpark, Vector3.up, Color.cyan, 1.0f);

            Assert.That(pool.PrewarmedCount, Is.EqualTo(2));
            Assert.That(pool.PlayCount, Is.EqualTo(3));
        }

        [Test]
        public void VfxPoolPrewarmedSystemsDoNotEmitDefaultParticles()
        {
            root = new GameObject("VFX Root");
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();
            pool.ConfigureForTests(poolSize: 3);

            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(includeInactive: true);

            Assert.That(systems, Has.Length.EqualTo(3));
            Assert.That(systems, Has.All.Matches<ParticleSystem>(system => !system.main.playOnAwake));
            Assert.That(systems, Has.All.Matches<ParticleSystem>(system => !system.emission.enabled));
            Assert.That(systems, Has.All.Matches<ParticleSystem>(system => !system.isPlaying));
        }

        [Test]
        public void VfxCuePlayerUsesReducedParticleSetting()
        {
            root = new GameObject("VFX Root");
            BlockiverseFeedbackSettings settings = root.AddComponent<BlockiverseFeedbackSettings>();
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();
            BlockiverseVfxCuePlayer player = root.AddComponent<BlockiverseVfxCuePlayer>();

            pool.ConfigureForTests(poolSize: 4);
            settings.ReducedParticles = true;
            player.Configure(pool, settings);

            player.PlayCue(BlockiverseVfxCue.BlockBreakDust, Vector3.zero);

            Assert.That(pool.LastIntensity, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void VfxCuePlayerSuppressesLightningWhenReducedFlashIsEnabled()
        {
            root = new GameObject("VFX Root");
            BlockiverseFeedbackSettings settings = root.AddComponent<BlockiverseFeedbackSettings>();
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();
            BlockiverseVfxCuePlayer player = root.AddComponent<BlockiverseVfxCuePlayer>();

            pool.ConfigureForTests(poolSize: 2);
            settings.ReducedFlash = true;
            player.Configure(pool, settings);

            player.PlayCue(BlockiverseVfxCue.LightningFlash, Vector3.zero);

            Assert.That(pool.PlayCount, Is.Zero, "Reduced Flash should suppress lightning VFX rather than attenuate it.");

            player.PlayCue(BlockiverseVfxCue.RainSplash, Vector3.zero);

            Assert.That(pool.PlayCount, Is.EqualTo(1), "Reduced Flash should not suppress non-flash weather VFX.");
        }

        [Test]
        public void WeatherFeedbackDoesNotScatterHeadsetVfxWhileMenusBlockWorldInput()
        {
            root = new GameObject("Weather Feedback Root");
            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(root.transform, worldPositionStays: false);
            cameraObject.AddComponent<Camera>();

            CreativeWorldManager worldManager = root.AddComponent<CreativeWorldManager>();
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();
            BlockiverseVfxCuePlayer player = root.AddComponent<BlockiverseVfxCuePlayer>();
            WeatherFeedbackController feedback = root.AddComponent<WeatherFeedbackController>();

            pool.ConfigureForTests(poolSize: 1);
            player.Configure(pool, settings: null);
            SetPrivateField(feedback, "worldManager", worldManager);
            SetPrivateField(feedback, "vfxCuePlayer", player);
            SetPrivateField(feedback, "lastWeatherState", WeatherState.Fog);

            BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);

            InvokePrivate(feedback, "TickPrecipitationVfx");

            Assert.That(pool.PlayCount, Is.Zero,
                "Weather fog/snow VFX must not spawn from the headset while the app is at menu input.");
        }

        [Test]
        public void WeatherFeedbackDoesNotStartAmbientLoopByDefault()
        {
            root = new GameObject("Weather Feedback Root");
            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(root.transform, worldPositionStays: false);
            cameraObject.AddComponent<Camera>();

            CreativeWorldManager worldManager = root.AddComponent<CreativeWorldManager>();
            root.AddComponent<AudioSource>();
            BlockiverseAudioCuePlayer audioCuePlayer = root.AddComponent<BlockiverseAudioCuePlayer>();
            WeatherFeedbackController feedback = root.AddComponent<WeatherFeedbackController>();

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.DayAmbienceLoop, CreateClip("day_ambience_loop"));
            SetPrivateField(feedback, "worldManager", worldManager);
            SetPrivateField(feedback, "audioCuePlayer", audioCuePlayer);

            InvokePrivate(feedback, "UpdateAmbienceLoop");

            Assert.That(audioCuePlayer.IsLoopActive(BlockiverseAudioCue.DayAmbienceLoop), Is.False,
                "The weather controller must not auto-start a constant day/night/cave ambience loop in clear weather.");
        }

        [Test]
        public void VfxPoolMapsGeneratedSpritesToRuntimeCues()
        {
            root = new GameObject("VFX Root");
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();

            Sprite blockDust = CreateSprite();
            Sprite blockPuff = CreateSprite();
            Sprite resourceSpark = CreateSprite();
            Sprite craftSpark = CreateSprite();
            Sprite rainSplash = CreateSprite();
            Sprite snowflake = CreateSprite();
            Sprite fogWisp = CreateSprite();
            Sprite ember = CreateSprite();

            pool.ConfigureParticleSprites(
                blockDust,
                blockPuff,
                resourceSpark,
                craftSpark,
                rainSplash,
                snowflake,
                fogWisp,
                ember);

            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.BlockBreakDust), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.BlockPlacePuff), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.ResourceSpark), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.CraftSuccessSpark), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.RainSplash), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.SnowflakeDrift), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.FogWisp), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.CampfireEmber), Is.True);
        }

        [Test]
        public void GeneratedVfxParticleMaterialUsesTransparentAlphaBlending()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.VfxParticleMaterialPath);

            Assert.That(material, Is.Not.Null);
            Assert.That(material.GetTag("RenderType", searchFallbacks: false), Is.EqualTo("Transparent"));
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            AssertMaterialFloat(material, "_Surface", 1.0f);
            AssertMaterialFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            AssertMaterialFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            AssertMaterialFloat(material, "_ZWrite", 0.0f);
            Assert.That(material.GetColor("_BaseColor").a, Is.LessThan(1.0f));
        }

        [Test]
        public void VfxPoolReusesPerPlayParticleConfigurationScratch()
        {
            string source = File.ReadAllText("Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs");

            Assert.That(source, Does.Contain("readonly ParticleSystem.Burst[] burstScratch"));
            Assert.That(source, Does.Contain("MaterialPropertyBlock propertyBlock;"));
            Assert.That(source, Does.Contain("propertyBlock ??= new();"));
            Assert.That(source, Does.Not.Contain("SetBursts(new[]"));
            Assert.That(source, Does.Not.Contain("MaterialPropertyBlock propertyBlock = new"));
        }

        static Sprite CreateSprite()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            return Sprite.Create(texture, new Rect(0.0f, 0.0f, 2.0f, 2.0f), new Vector2(0.5f, 0.5f));
        }

        static AudioClip CreateClip(string name)
        {
            return AudioClip.Create(name, 16, 1, 44100, false);
        }

        static void AssertMaterialFloat(Material material, string propertyName, float expected)
        {
            Assert.That(material.HasProperty(propertyName), Is.True, $"{material.name} must expose {propertyName}.");
            Assert.That(material.GetFloat(propertyName), Is.EqualTo(expected).Within(0.001f));
        }

        static void SetPrivateField<TValue>(object target, string fieldName, TValue value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}.");
            field.SetValue(target, value);
        }

        static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing private method {methodName}.");
            method.Invoke(target, null);
        }
    }
}
