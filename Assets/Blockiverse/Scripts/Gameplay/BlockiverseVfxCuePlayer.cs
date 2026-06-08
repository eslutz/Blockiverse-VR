using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseVfxCuePlayer : MonoBehaviour
    {
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] BlockiverseVfxPool pool;
        [SerializeField] BlockiverseFeedbackSettings feedbackSettings;

        bool subscribed;

        public CreativeInteractionController InteractionController => interactionController;
        public BlockiverseVfxPool Pool => pool;
        public BlockiverseFeedbackSettings FeedbackSettings => feedbackSettings;

        public void Configure(BlockiverseVfxPool targetPool, BlockiverseFeedbackSettings settings)
        {
            pool = targetPool;
            feedbackSettings = settings;
        }

        public void Configure(CreativeInteractionController controller, BlockiverseVfxPool targetPool, BlockiverseFeedbackSettings settings)
        {
            Unsubscribe();
            interactionController = controller;
            pool = targetPool;
            feedbackSettings = settings;
            Subscribe();
        }

        public void PlayCue(BlockiverseVfxCue cue, Vector3 position)
        {
            EnsureReferences();
            if (pool == null)
                return;

            float intensity = feedbackSettings != null && feedbackSettings.ReducedParticles ? 0.5f : 1.0f;
            if (cue == BlockiverseVfxCue.LightningFlash && feedbackSettings != null && feedbackSettings.ReducedFlash)
                intensity *= 0.35f;

            pool.Play(cue, position, TintForCue(cue), intensity);
        }

        void OnEnable()
        {
            EnsureReferences();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void EnsureReferences()
        {
            if (pool == null)
                pool = GetComponent<BlockiverseVfxPool>();
            if (feedbackSettings == null)
                feedbackSettings = GetComponent<BlockiverseFeedbackSettings>();
            if (interactionController == null && Application.isPlaying)
                interactionController = FindFirstObjectByType<CreativeInteractionController>();
        }

        void Subscribe()
        {
            if (subscribed || interactionController == null)
                return;

            interactionController.BlockMutationApplied += OnBlockMutationApplied;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || interactionController == null)
                return;

            interactionController.BlockMutationApplied -= OnBlockMutationApplied;
            subscribed = false;
        }

        void OnBlockMutationApplied(BlockChange change)
        {
            Vector3 center = new(change.Position.X + 0.5f, change.Position.Y + 0.5f, change.Position.Z + 0.5f);
            PlayCue(
                change.NewBlock == BlockRegistry.Air ? BlockiverseVfxCue.BlockBreakDust : BlockiverseVfxCue.BlockPlacePuff,
                center);
        }

        static Color TintForCue(BlockiverseVfxCue cue)
        {
            return cue switch
            {
                BlockiverseVfxCue.ResourceSpark => new Color(0.45f, 0.95f, 1.0f),
                BlockiverseVfxCue.CraftSuccessSpark => new Color(1.0f, 0.78f, 0.28f),
                BlockiverseVfxCue.CraftFailPuff => new Color(0.55f, 0.55f, 0.55f),
                BlockiverseVfxCue.RainSplash => new Color(0.55f, 0.85f, 1.0f),
                BlockiverseVfxCue.SnowflakeDrift => Color.white,
                BlockiverseVfxCue.FogWisp => new Color(0.75f, 0.82f, 0.82f),
                BlockiverseVfxCue.TorchSpark or BlockiverseVfxCue.CampfireEmber => new Color(1.0f, 0.48f, 0.15f),
                _ => new Color(0.75f, 0.68f, 0.55f)
            };
        }
    }
}
