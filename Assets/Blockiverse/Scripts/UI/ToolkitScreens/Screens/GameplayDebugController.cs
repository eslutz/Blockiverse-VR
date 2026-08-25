using System;
using System.Text;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // The diagnostic readout: where you are, what the world is doing, and what the frame costs.
    // OFF by default, toggled from the Settings screen.
    //
    // ── Why frame time is on here at all ─────────────────────────────────────
    //
    // Because it is the one number that cannot be obtained any other way while a headset is on the
    // player's face. This project's UI has never had a device performance capture — ADR 0010 still
    // lists "no UI performance baseline exists" as open — and a readout inside the headset turns
    // that from a deferred task into something checkable during any session. Same for GC per
    // frame: the HUD's stated target is zero bytes in steady gameplay, and without a readout that
    // target is unfalsifiable exactly where it matters.
    //
    // ── The overlay must not be the reason the numbers moved ─────────────────
    //
    // Everything here is built to cost nothing when nothing changed:
    //  - 4 Hz refresh, not per-frame. Frame time is SAMPLED every frame into a rolling average
    //    (one float add) but only RENDERED four times a second.
    //  - Twelve fixed labels, each gated on its own last-rendered string. A steady scene assigns
    //    no text at all.
    //  - One reusable StringBuilder. Composing twelve lines per refresh with interpolation would
    //    allocate on a panel whose whole purpose includes reporting allocation.
    //  - Hidden means display:none on the BODY, so a disabled overlay lays out nothing. Not the
    //    root: the base class writes an inline style.display there, and inline beats USS.
    //
    // ── Nothing here is localized, on purpose ────────────────────────────────
    //
    // "xyz", "chunk", "fps" are diagnostic identifiers, not prose — the same reason F3-style
    // readouts elsewhere are untranslated. The VALUES are raw canonical ids and enum names too
    // ("meadow", "Rain", "Freshwater") rather than display names, because a diagnostic readout
    // should print the identifier you would put in a bug report or grep the source for. A
    // localized "Meadow" is worse here than the id that actually addresses the biome.
    //
    // This also keeps the screen off BlockiverseLocalization, which the migrated screens in this
    // assembly deliberately avoid.
    // Left edge of view, mirroring the vitals readout on the right. Y is 0.08, not 0.12: at 0.12 the
    // panel's top edge reached 0.30 and clipped the status toast's bottom edge at 0.28. The lower
    // value clears the toast by 20 mm and the mining bar by 15 mm.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/GameplayDebug.uxml",
        520, 360, UiToolkitPlacementProfile.Hud,
        HudLocalX = -0.42f, HudLocalY = 0.08f, HudLocalZ = 1.10f, NonInteractive = true)]
    public sealed class GameplayDebugController : UiToolkitScreenController
    {
        // Fast enough to feel live, slow enough that the readout is legible rather than a blur —
        // and slow enough that twelve string comparisons four times a second is genuinely free.
        public const float RefreshIntervalSeconds = 0.25f;

        // Weight for the exponential moving average of frame time. ~0.1 settles in about twenty
        // frames: responsive enough to show a hitch, damped enough that the number is readable
        // instead of flickering through every value between 8 and 12 ms.
        const float FrameTimeSmoothing = 0.1f;

        // Applied to bv-debug-body, NOT bv-screen-root: the base class writes an inline
        // style.display onto the root when the router shows the HUD, and an inline style outranks
        // every USS rule in UI Toolkit, so a hidden class on the root is silently ignored.
        const string HiddenClass = "dbg-body--hidden";

        // The root keeps its display untouched; only its paint is suppressed, so the plate does
        // not sit in the player's view as an empty opaque rectangle while the overlay is off.
        const string MutedClass = "dbg-root--muted";

        static readonly string[] Compass =
        {
            "N", "NE", "E", "SE", "S", "SW", "W", "NW",
        };

        readonly StringBuilder builder = new(96);

        Label positionLine;
        Label chunkLine;
        Label facingLine;
        Label biomeLine;
        Label targetLine;
        Label timeLine;
        Label weatherLine;
        Label climateLine;
        Label placeLine;
        Label sessionLine;
        Label perfLine;
        Label worldLine;
        VisualElement debugRoot;
        VisualElement debugBody;

        BlockiverseComfortSettings comfortSettings;
        CreativeWorldManager worldManager;
        CreativeInteractionController interactionController;
        BlockiverseNetworkSession networkSession;
        Transform head;

        // ProfilerRecorder only exists where the profiler is compiled in — development builds and
        // the editor. In a release player it never becomes valid, and the readout says so with a
        // dash rather than printing zero: "no data" and "zero bytes allocated" are opposite
        // conclusions, and conflating them would make a release build look like it hit the target.
        ProfilerRecorder gcRecorder;
        bool gcRecorderValid;

        float smoothedFrameSeconds;
        float nextRefreshTime;

        bool lastEnabled;
        bool lastEnabledValid;

        readonly string[] lastLines = new string[12];

        public override string ScreenId => MenuActions.GameplayHudScreen;

        public bool IsOverlayVisible => lastEnabledValid && lastEnabled;

        // Counts text assignments that actually reached an element.
        //
        // Exists because the obvious test of the render gate — refresh twice, assert the label's
        // text is the same reference — cannot fail: UI Toolkit's TextElement setter returns early
        // when the incoming string is equal, so the old reference survives whether or not this
        // class gated anything. Asserting on that is indistinguishable from asserting nothing.
        // A counter incremented only on a real write is the observable the gate actually claims.
        public int TextWriteCount { get; private set; }

        // Test seam: bind settings directly rather than discovering them from a scene.
        public void ConfigureComfortSettings(BlockiverseComfortSettings settings)
        {
            comfortSettings = settings;
            lastEnabledValid = false;
            ApplyEnabled();
        }


        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            debugRoot = Require<VisualElement>(root, ScreenRootElementName, ref allFound);
            debugBody = Require<VisualElement>(root, "bv-debug-body", ref allFound);
            positionLine = Require<Label>(root, "bv-debug-position", ref allFound);
            chunkLine = Require<Label>(root, "bv-debug-chunk", ref allFound);
            facingLine = Require<Label>(root, "bv-debug-facing", ref allFound);
            biomeLine = Require<Label>(root, "bv-debug-biome", ref allFound);
            targetLine = Require<Label>(root, "bv-debug-target", ref allFound);
            timeLine = Require<Label>(root, "bv-debug-time", ref allFound);
            weatherLine = Require<Label>(root, "bv-debug-weather", ref allFound);
            climateLine = Require<Label>(root, "bv-debug-climate", ref allFound);
            placeLine = Require<Label>(root, "bv-debug-place", ref allFound);
            sessionLine = Require<Label>(root, "bv-debug-session", ref allFound);
            perfLine = Require<Label>(root, "bv-debug-perf", ref allFound);
            worldLine = Require<Label>(root, "bv-debug-world", ref allFound);

            Array.Clear(lastLines, 0, lastLines.Length);
            lastEnabledValid = false;
            ApplyEnabled();
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
        }

        protected override void OnUnregisterCallbacks()
        {
        }

        protected override void OnDetach()
        {
            debugRoot = null;
            debugBody = null;
            positionLine = null;
            chunkLine = null;
            facingLine = null;
            biomeLine = null;
            targetLine = null;
            timeLine = null;
            weatherLine = null;
            climateLine = null;
            placeLine = null;
            sessionLine = null;
            perfLine = null;
            worldLine = null;
        }

        protected override void OnShown()
        {
            BindFromScene();
            ApplyEnabled();
        }

        // The recorder's lifetime hangs off OnAwake/OnDestroy, NOT OnEnable/OnDisable.
        //
        // UiToolkitScreenController declares private `void OnEnable() => Attach()` and
        // `void OnDisable() => Detach()`. Unity dispatches lifecycle messages by name to the
        // MOST-DERIVED declaration only, so declaring OnEnable here hid the base's — Attach()
        // never ran, OnAttach never ran, every cached element stayed null, and Refresh() returned
        // at its first null check. The overlay was inert in the built game while the EditMode
        // tests passed, because they call AttachForTest directly and never go through OnEnable.
        //
        // The base declares no OnDestroy, so that one is safe to take (seven sibling screens
        // already do). OnAwake is a real virtual seam on the base.
        protected override void OnAwake()
        {
            base.OnAwake();
            BindFromScene();

            // "GC Allocated In Frame" is the managed-allocation counter. Starting the recorder is
            // harmless where the profiler is absent — it simply never reports Valid.
            gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            gcRecorderValid = gcRecorder.Valid;
        }

        void OnDestroy()
        {
            if (gcRecorder.Valid)
                gcRecorder.Dispose();

            gcRecorderValid = false;
        }

        void Update()
        {
            // Sampled every frame, rendered at 4 Hz. The average has to see every frame or a hitch
            // between refreshes would never appear in it — which would make the readout worse than
            // useless, because it would look calm precisely when it should not.
            float delta = Time.unscaledDeltaTime;

            if (delta > 0f)
            {
                smoothedFrameSeconds = smoothedFrameSeconds <= 0f
                    ? delta
                    : Mathf.Lerp(smoothedFrameSeconds, delta, FrameTimeSmoothing);
            }

            if (!IsVisible || Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;

            ApplyEnabled();

            if (lastEnabled)
                Refresh();
        }

        void BindFromScene()
        {
            comfortSettings ??= BlockiverseSceneLookup.Find<BlockiverseComfortSettings>(FindObjectsInactive.Include);
            worldManager ??= BlockiverseSceneLookup.Find<CreativeWorldManager>(FindObjectsInactive.Include);
            interactionController ??= BlockiverseSceneLookup.Find<CreativeInteractionController>(FindObjectsInactive.Include);
            networkSession ??= BlockiverseSceneLookup.Find<BlockiverseNetworkSession>(FindObjectsInactive.Include);

            if (head == null)
            {
                Camera main = Camera.main;
                head = main != null ? main.transform : null;
            }
        }

        void ApplyEnabled()
        {
            if (debugRoot == null || debugBody == null)
                return;

            bool enabled = comfortSettings != null && comfortSettings.DebugOverlayEnabled;

            if (lastEnabledValid && lastEnabled == enabled)
                return;

            lastEnabled = enabled;
            lastEnabledValid = true;
            debugBody.EnableInClassList(HiddenClass, !enabled);
            debugRoot.EnableInClassList(MutedClass, !enabled);
        }

        public void Refresh()
        {
            if (positionLine == null)
                return;

            BindFromScene();

            Vector3 world = head != null ? head.position : Vector3.zero;
            BlockPosition block = CreativeInteractionController.ToBlockPosition(world);

            builder.Clear();
            builder.Append("xyz ").Append(block.X).Append(" / ").Append(block.Y).Append(" / ").Append(block.Z);
            SetLine(0, positionLine, builder);

            // Chunk space, because that is the coordinate a save-region or chunk-boundary bug is
            // reported in — region files are named r.<rx>.<rz> and chunks are 16 blocks. Floor
            // division, not truncation: -1 / 16 is 0 in C# but belongs to chunk -1.
            int chunkX = Mathf.FloorToInt(block.X / (float)WorldConstants.ChunkSize);
            int chunkZ = Mathf.FloorToInt(block.Z / (float)WorldConstants.ChunkSize);
            int localX = block.X - chunkX * WorldConstants.ChunkSize;
            int localZ = block.Z - chunkZ * WorldConstants.ChunkSize;

            builder.Clear();
            builder.Append("chunk ").Append(chunkX).Append(',').Append(chunkZ)
                .Append("  local ").Append(localX).Append(',').Append(block.Y).Append(',').Append(localZ);
            SetLine(1, chunkLine, builder);

            float yaw = head != null ? head.eulerAngles.y : 0f;
            int yawDegrees = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f));
            string compass = Compass[Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 45f) % 8];

            builder.Clear();
            builder.Append("facing ").Append(compass).Append(' ').Append(yawDegrees).Append('°');
            SetLine(2, facingLine, builder);

            builder.Clear();
            builder.Append("biome ").Append(ResolveBiome(block));
            SetLine(3, biomeLine, builder);

            SetLine(4, targetLine, ResolveTarget());

            ComposeTimeAndWeather(block);
            ComposePlace(world);
            ComposeSession();
            ComposePerf();
            ComposeWorld();
        }

        string ResolveBiome(BlockPosition block)
        {
            if (worldManager == null)
                return "—";

            int index = worldManager.BiomeIndexAt(block.X, block.Z);
            string canonical = SurvivalBiomeResolver.CanonicalIdForBiomeIndex(index);

            // Null means the world genuinely has no biomes — a flat creative or void-builder
            // preset — which is a real state rather than a lookup failure.
            return canonical ?? "none";
        }

        // What the CONTROLLER ray is on, not what the head is pointed at. This game aims with the
        // hand, so a head-relative reading would name the wrong block almost every time.
        string ResolveTarget()
        {
            if (interactionController == null || !interactionController.CurrentTarget.HasValue)
                return "target —";

            BlockPosition target = interactionController.CurrentTarget.Value;

            builder.Clear();
            builder.Append("target ");

            if (worldManager?.World != null && worldManager.Registry != null &&
                interactionController.TryGetBlock(target, out BlockId block))
            {
                builder.Append(worldManager.Registry.Get(block).Name);
            }
            else
            {
                builder.Append('?');
            }

            builder.Append(" @ ").Append(target.X).Append(',').Append(target.Y).Append(',').Append(target.Z);
            return builder.ToString();
        }

        void ComposeTimeAndWeather(BlockPosition block)
        {
            WorldTimeClock clock = worldManager != null ? worldManager.WorldTimeClock : null;

            if (clock == null)
            {
                SetLine(5, timeLine, "time —");
            }
            else
            {
                // The raw tick is here because simulation bugs are reported in ticks: weather
                // transitions, smelting and crop growth all advance on WorldTimeClock, not on a
                // wall clock. The day number and clock time are for the human reading it.
                long ticks = clock.TotalElapsedTicks;
                int day = (int)(ticks / WorldConstants.TicksPerDay) + 1;
                float normalized = clock.NormalizedTime;
                int minutesOfDay = Mathf.Clamp(Mathf.FloorToInt(normalized * 1440f), 0, 1439);

                builder.Clear();
                builder.Append("day ").Append(day).Append("  ");

                int hour = minutesOfDay / 60;
                int minute = minutesOfDay % 60;

                if (hour < 10)
                    builder.Append('0');

                builder.Append(hour).Append(':');

                if (minute < 10)
                    builder.Append('0');

                builder.Append(minute).Append("  t ").Append(ticks);
                SetLine(5, timeLine, builder);
            }

            if (worldManager == null || !worldManager.TryEvaluateEnvironment(block, out EnvironmentState environment))
            {
                SetLine(6, weatherLine, "weather —");
                SetLine(7, climateLine, "temp —");
                return;
            }

            builder.Clear();
            builder.Append("weather ").Append(environment.Weather.ToString());

            if (environment.Precipitation != PrecipitationKind.None)
            {
                builder.Append("  precip ")
                    .Append(environment.Precipitation.ToString())
                    .Append(' ')
                    .Append(Mathf.RoundToInt(environment.PrecipitationIntensity * 100f))
                    .Append('%');
            }

            SetLine(6, weatherLine, builder);

            // One decimal on temperature, because the freezing decision and the cold-exposure gate
            // both turn on fractions of a degree.
            builder.Clear();
            builder.Append("temp ").Append(environment.Temperature.ToString("0.0")).Append("°C")
                .Append("  cloud ").Append(Mathf.RoundToInt(environment.CloudCoverage * 100f)).Append('%')
                .Append("  fog ").Append(Mathf.RoundToInt(environment.FogDensity * 100f)).Append('%');

            SetLine(7, climateLine, builder);
        }

        void ComposePlace(Vector3 world)
        {
            builder.Clear();
            builder.Append("underground ");
            builder.Append(worldManager != null && worldManager.IsHeadUnderground(world) ? "yes" : "no");

            if (worldManager != null && worldManager.TryGetFluidFamilyAt(world, out FluidFamily fluid))
                builder.Append("  fluid ").Append(fluid.ToString());

            SetLine(8, placeLine, builder);
        }

        void ComposeSession()
        {
            builder.Clear();
            builder.Append("session ");
            builder.Append(networkSession != null ? networkSession.CurrentMode.ToString() : "Offline");
            SetLine(9, sessionLine, builder);
        }

        void ComposePerf()
        {
            float ms = smoothedFrameSeconds * 1000f;
            int fps = smoothedFrameSeconds > 0f ? Mathf.RoundToInt(1f / smoothedFrameSeconds) : 0;

            builder.Clear();
            builder.Append("frame ").Append(ms.ToString("0.0")).Append(" ms  ").Append(fps).Append(" fps");
            builder.Append("  gc ");

            if (gcRecorderValid && gcRecorder.Valid)
                builder.Append(gcRecorder.LastValue).Append(" B");
            else
                builder.Append('—');

            SetLine(10, perfLine, builder);
        }

        void ComposeWorld()
        {
            builder.Clear();
            builder.Append("seed ");
            builder.Append(worldManager?.Settings != null ? worldManager.Settings.Seed.ToString() : "—");
            builder.Append("  mode ").Append(worldManager != null ? worldManager.GameModeString : "—");
            SetLine(11, worldLine, builder);
        }

        // Every write goes through here, so a steady scene assigns no text at all. Text assignment
        // regenerates a Label's layout in retained mode whether or not the string changed.
        // Takes the BUILDER, not a string.
        //
        // Every caller used to pass builder.ToString(), which put the allocation upstream of the
        // gate: the string was materialised on all twelve lines every refresh and then usually
        // thrown away because nothing had changed. That is 48 dead strings a second on the one
        // panel whose job includes reporting how much the frame allocates.
        //
        // Comparing the builder's characters against the cached string touches no heap, so a
        // steady scene now allocates nothing at all and ToString() runs only for a line that
        // genuinely changed.
        void SetLine(int index, Label label, StringBuilder pending)
        {
            if (label == null)
                return;

            if (Matches(lastLines[index], pending))
                return;

            string text = pending.ToString();
            lastLines[index] = text;
            label.text = text;
            TextWriteCount++;
        }

        // For the placeholder lines and ResolveTarget, which hand over an already-built string.
        // The literals are interned constants, so this overload allocates nothing either.
        void SetLine(int index, Label label, string text)
        {
            if (label == null || string.Equals(lastLines[index], text, StringComparison.Ordinal))
                return;

            lastLines[index] = text;
            label.text = text;
            TextWriteCount++;
        }

        static bool Matches(string cached, StringBuilder pending)
        {
            if (cached == null || cached.Length != pending.Length)
                return false;

            for (int i = 0; i < cached.Length; i++)
            {
                if (cached[i] != pending[i])
                    return false;
            }

            return true;
        }
    }
}
