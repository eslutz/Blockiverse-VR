using Blockiverse.UI;
using Blockiverse.VR;
using UnityEditor;
using UnityEngine;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        // The canonical controller-mapping copy. The shipped text now lives in the UI Toolkit
        // ControllerMapping document, but this const stays here — and in this file — because
        // BlockiverseBootstrapEditModeTests.GeneratedControllerMappingMentionsBothTeleportSticks
        // reads this path off disk and asserts five of these lines verbatim, and
        // ControlsScreensEditModeTests names it as the source the document is checked against.
        // Retiring it means repointing both tests at the UXML, not deleting it here.
        const string ControllerMappingText =
            "Dominant trigger: press UI / break\n" +
            "Dominant grip: place / use\n" +
            "Support grip: blocks menu\n" +
            "Menu: pause\n" +
            "Dominant stick: snap turn\n" +
            "Dominant stick click: toggle block editing\n" +
            "Dominant primary button: jump / swim up\n" +
            "Dominant secondary button: crouch / swim down\n" +
            "Support stick: move\n" +
            "Support stick click: sprint\n" +
            "Either stick hold up: teleport aim, release to land";

        // What is left of the routed-menu generator after the uGUI panels went. Everything this
        // used to build now lives in UI Toolkit (EnsureUiToolkitMenus), but the two components
        // below cannot move with it: UiToolkitMenuHost finds BlockiverseMenuController by scene
        // search rather than adding it, and nothing else in the editor tree mounts either of
        // these. Delete this method and the Toolkit host wires null at bootstrap — no compile
        // error, no failing test, just an inert menu frontend on device.
        static void EnsureXrRigGameMenus(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");

            if (cameraOffset == null)
                return;

            BlockiverseMenuController controller = EnsureComponent<BlockiverseMenuController>(rig);
            EnsureComponent<BlockiverseWorldSessionController>(rig);

            // Hardware Menu is routed by the controller at runtime (it adds the listener itself in
            // ResolveRuntimeReferences). A previously generated rig may have serialized a
            // persistent listener for the same handler, which would fire it twice per press.
            if (inputRig != null)
            {
                RemovePersistentListeners(inputRig.MenuPressed, controller, nameof(BlockiverseMenuController.OnMenuPressed));
                EditorUtility.SetDirty(inputRig);
            }

            // Not menu content: this strips stale pointer-projection children and puts the
            // controller visuals back on the main camera layer. It lives in the composition-layer
            // partial and this is still its only call site.
            EnsureGeneratedVrUiPanels(cameraOffset);

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(rig);
        }
    }
}
