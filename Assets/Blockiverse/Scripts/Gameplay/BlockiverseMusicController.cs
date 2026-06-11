using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Where the player is, musically: the title/pause menus before a world exists, or daylight,
    // night, and underground inside a session.
    public enum BlockiverseMusicContext
    {
        None,
        Menu,
        Day,
        Night,
        Cave
    }

    // Pure scheduling rules for the music bed (engine-free so EditMode tests drive them
    // directly): which context is active, how long the silence gaps between tracks run, and the
    // fade envelope a playing track follows. Music is sparse by design — tracks play once and
    // give the world's ambience room before the next one rolls in.
    public static class BlockiverseMusicScheduling
    {
        // A short lead-in before the first track of a context, long breathing gaps after each
        // track, and a quick retry when a context has no clip yet.
        public const float FirstTrackDelaySeconds = 8.0f;
        public const float MinGapSeconds = 50.0f;
        public const float MaxGapSeconds = 130.0f;
        public const float RetryDelaySeconds = 10.0f;

        public const float FadeInSeconds = 2.5f;
        public const float FadeOutSeconds = 2.0f;
        // Context switches (entering a cave, day turning to night) duck the current track out
        // faster than its natural tail.
        public const float SwitchFadeOutSeconds = 1.2f;

        public static BlockiverseMusicContext ResolveContext(bool worldActive, bool underground, float normalizedTime)
        {
            if (!worldActive)
                return BlockiverseMusicContext.Menu;
            if (underground)
                return BlockiverseMusicContext.Cave;
            return WorldTimeClock.IsDay(normalizedTime)
                ? BlockiverseMusicContext.Day
                : BlockiverseMusicContext.Night;
        }

        public static float RollGapSeconds(System.Random rng)
        {
            if (rng == null)
                return MinGapSeconds;
            return MinGapSeconds + (float)rng.NextDouble() * (MaxGapSeconds - MinGapSeconds);
        }

        // Trapezoid fade for a once-through track: fade in from the start, fade out into the
        // clip's end. Returns the 0–1 envelope level at `playbackTime`.
        public static float ResolveFadeLevel(float playbackTime, float clipLength)
        {
            if (clipLength <= 0.0f || playbackTime <= 0.0f || playbackTime >= clipLength)
                return 0.0f;

            float fadeIn = Mathf.Clamp01(playbackTime / FadeInSeconds);
            float fadeOut = Mathf.Clamp01((clipLength - playbackTime) / FadeOutSeconds);
            return Mathf.Min(fadeIn, fadeOut);
        }
    }

    // Plays the generated music bed: picks the track for the current context (menu / day /
    // night / cave), fades it in, lets it finish, then waits out a long gap before the next.
    // Context changes duck the current track and reschedule. Volume follows the Music category
    // (master × music sliders, mute) every frame, so settings apply immediately. Music is pure
    // local presentation — nothing here syncs or affects the simulation.
    [DisallowMultipleComponent]
    public sealed class BlockiverseMusicController : MonoBehaviour
    {
        const float PollIntervalSeconds = 1.0f;

        [SerializeField] BlockiverseFeedbackSettings feedbackSettings;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] AudioClip menuTrackClip;
        [SerializeField] AudioClip dayTrackClip;
        [SerializeField] AudioClip nightTrackClip;
        [SerializeField] AudioClip caveTrackClip;

        AudioSource musicSource;
        System.Random rng;
        BlockiverseMusicContext currentContext = BlockiverseMusicContext.None;
        float nextPollTime;
        float nextTrackTime;
        float switchFadeEndTime = float.NegativeInfinity;

        public BlockiverseMusicContext CurrentContext => currentContext;
        public bool IsPlayingTrack => musicSource != null && musicSource.isPlaying;

        public void ConfigureFeedbackSettings(BlockiverseFeedbackSettings settings)
        {
            feedbackSettings = settings;
        }

        public void ConfigureClips(AudioClip menuTrack, AudioClip dayTrack, AudioClip nightTrack, AudioClip caveTrack)
        {
            menuTrackClip = menuTrack;
            dayTrackClip = dayTrack;
            nightTrackClip = nightTrack;
            caveTrackClip = caveTrack;
        }

        public AudioClip ResolveTrackClip(BlockiverseMusicContext context) => context switch
        {
            BlockiverseMusicContext.Menu => menuTrackClip,
            BlockiverseMusicContext.Day => dayTrackClip,
            BlockiverseMusicContext.Night => nightTrackClip,
            BlockiverseMusicContext.Cave => caveTrackClip,
            _ => null,
        };

        void OnEnable()
        {
            rng ??= new System.Random();
            EnsureMusicSource();
            DiscoverDependencies();
            currentContext = BlockiverseMusicContext.None;
            nextTrackTime = Time.time + BlockiverseMusicScheduling.FirstTrackDelaySeconds;
        }

        void OnDisable()
        {
            if (musicSource != null)
                musicSource.Stop();
            currentContext = BlockiverseMusicContext.None;
        }

        void Update()
        {
            if (Time.time >= nextPollTime)
            {
                nextPollTime = Time.time + PollIntervalSeconds;
                UpdateContext();
            }

            if (musicSource == null)
                return;

            if (musicSource.isPlaying)
            {
                UpdatePlayingTrack();
            }
            else if (musicSource.clip != null)
            {
                // The track ran out on its own — rest through a long gap before the next one.
                musicSource.clip = null;
                nextTrackTime = Time.time + BlockiverseMusicScheduling.RollGapSeconds(rng);
            }
            else if (Time.time >= nextTrackTime)
            {
                TryStartTrack();
            }
        }

        void UpdateContext()
        {
            DiscoverDependencies();

            bool worldActive = worldManager != null && worldManager.World != null;
            BlockiverseMusicContext resolved = BlockiverseMusicScheduling.ResolveContext(
                worldActive, worldActive && IsHeadUnderground(), ResolveNormalizedTime());

            if (resolved == currentContext)
                return;

            currentContext = resolved;

            if (musicSource != null && musicSource.isPlaying)
            {
                // Duck the stale context's track out quickly; the regular scheduler then starts
                // the new context's track after a short lead-in.
                switchFadeEndTime = Time.time + BlockiverseMusicScheduling.SwitchFadeOutSeconds;
            }
            else
            {
                nextTrackTime = Mathf.Min(
                    nextTrackTime,
                    Time.time + BlockiverseMusicScheduling.FirstTrackDelaySeconds);
            }
        }

        void UpdatePlayingTrack()
        {
            float fade = BlockiverseMusicScheduling.ResolveFadeLevel(
                musicSource.time, musicSource.clip != null ? musicSource.clip.length : 0.0f);

            if (switchFadeEndTime > float.NegativeInfinity)
            {
                float switchFade = Mathf.Clamp01(
                    (switchFadeEndTime - Time.time) / BlockiverseMusicScheduling.SwitchFadeOutSeconds);
                fade = Mathf.Min(fade, switchFade);

                if (switchFade <= 0.0f)
                {
                    musicSource.Stop();
                    musicSource.clip = null;
                    switchFadeEndTime = float.NegativeInfinity;
                    nextTrackTime = Time.time + BlockiverseMusicScheduling.FirstTrackDelaySeconds;
                    return;
                }
            }

            musicSource.volume = ResolveMusicVolume() * fade;
        }

        void TryStartTrack()
        {
            AudioClip clip = ResolveTrackClip(currentContext);
            if (clip == null)
            {
                nextTrackTime = Time.time + BlockiverseMusicScheduling.RetryDelaySeconds;
                return;
            }

            switchFadeEndTime = float.NegativeInfinity;
            musicSource.clip = clip;
            musicSource.volume = 0.0f;
            musicSource.Play();
        }

        float ResolveMusicVolume() =>
            feedbackSettings != null
                ? feedbackSettings.ResolveVolume(BlockiverseAudioCategory.Music)
                : 1.0f;

        float ResolveNormalizedTime() =>
            worldManager != null && worldManager.WorldTimeClock != null
                ? worldManager.WorldTimeClock.NormalizedTime
                : 0.25f;

        // Underground = no sky above the head cell, same O(1) sky-map answer the ambience
        // driver uses.
        bool IsHeadUnderground()
        {
            VoxelSkyLightMap skyLight = worldManager.Renderer != null ? worldManager.Renderer.SkyLight : null;
            if (skyLight == null)
                return false;

            Camera head = Camera.main;
            if (head == null || worldManager.World == null)
                return false;

            BlockPosition cell = CreativeInteractionController.ToBlockPosition(head.transform.position);
            return worldManager.World.Bounds.Contains(cell) && !skyLight.HasSkyAccess(cell);
        }

        void EnsureMusicSource()
        {
            if (musicSource != null)
                return;

            // A dedicated child source keeps the music stream independent of the cue player's
            // one-shot source on the same rig object.
            var sourceObject = new GameObject("Music Audio Source");
            sourceObject.transform.SetParent(transform, worldPositionStays: false);
            musicSource = sourceObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = false;
            musicSource.spatialBlend = 0f;
        }

        void DiscoverDependencies()
        {
            if (!Application.isPlaying)
                return;

            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (feedbackSettings == null)
                feedbackSettings = GetComponent<BlockiverseFeedbackSettings>();
        }
    }
}
