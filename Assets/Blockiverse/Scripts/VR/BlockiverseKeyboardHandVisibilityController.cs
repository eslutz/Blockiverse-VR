using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using UnityEngine;

namespace Blockiverse.VR
{
    // Hides the first-person fallback hands while the Quest system keyboard is on screen, so the
    // player is not typing through a pair of floating hands.
    //
    // The visibility signal used to come from BlockiverseSystemKeyboardField, a uGUI component
    // that owned TouchScreenKeyboard on behalf of a TMP_InputField and raised a static
    // KeyboardVisibilityChanged event. UI Toolkit opens the system keyboard itself — the
    // UIElements module carries TouchScreenTextEditorEventHandler / OpenTouchScreenKeyboard and
    // exposes ITextEdition.touchScreenKeyboard — so with the uGUI fields gone there was no longer
    // anything raising that event, and this controller would have sat subscribed to a dead source
    // with the hands never hiding. Nothing would have failed; the hands would just be wrong.
    //
    // Reading TouchScreenKeyboard.visible instead makes the signal independent of who opened the
    // keyboard, which is what we actually mean. It also preserves the property the old event was
    // careful about: this is "a keyboard is genuinely on screen", not "a text field has focus", so
    // an Open() call that never surfaces an overlay leaves the player with their hands rather than
    // with neither hands nor a keyboard.
    [DisallowMultipleComponent]
    public sealed class BlockiverseKeyboardHandVisibilityController : MonoBehaviour
    {
        [SerializeField] BlockiverseNetworkAvatarRig avatarRig;
        [SerializeField] BlockiverseMetaAvatarPresenter avatarPresenter;

        bool lastAppliedVisible;

        public static bool KeyboardVisible => TouchScreenKeyboard.visible;

        public void Configure(BlockiverseNetworkAvatarRig rig)
        {
            avatarRig = rig;
            Apply(KeyboardVisible);
        }

        void OnEnable()
        {
            ResolveAvatarRig();
            lastAppliedVisible = false;
            Apply(KeyboardVisible);
        }

        void OnDisable()
        {
            Apply(false);
        }

        // Edge-triggered: the poll is a static bool read, but the avatar rig call is not something
        // to make every frame.
        void Update()
        {
            bool visible = KeyboardVisible;
            if (visible != lastAppliedVisible)
                Apply(visible);
        }

        void Apply(bool visible)
        {
            lastAppliedVisible = visible;
            ResolveAvatarRig();

            // BOTH bodies, because a player only ever has one of them and telling the wrong one
            // hides nothing. The fallback call was the whole implementation while block hands were
            // all anyone saw; once Meta avatars load, it suppresses a proxy that is not being
            // rendered while the real avatar stays on screen — which is what Eric saw, with the
            // hands frozen because the entity holds its last pose once the keyboard overlay takes
            // focus.
            avatarRig?.SetFirstPersonFallbackVisualsSuppressed(visible);
            avatarPresenter?.SetFirstPersonVisualsSuppressed(visible);
        }

        void ResolveAvatarRig()
        {
            if (avatarRig == null)
            {
                avatarRig = GetComponent<BlockiverseNetworkAvatarRig>();
                if (avatarRig == null)
                    avatarRig = GetComponentInParent<BlockiverseNetworkAvatarRig>(includeInactive: true);
                if (avatarRig == null)
                    avatarRig = GetComponentInChildren<BlockiverseNetworkAvatarRig>(includeInactive: true);
            }

            if (avatarPresenter == null)
            {
                avatarPresenter = GetComponent<BlockiverseMetaAvatarPresenter>();
                if (avatarPresenter == null)
                    avatarPresenter = GetComponentInParent<BlockiverseMetaAvatarPresenter>(includeInactive: true);
                if (avatarPresenter == null)
                    avatarPresenter = GetComponentInChildren<BlockiverseMetaAvatarPresenter>(includeInactive: true);
            }
        }
    }
}
