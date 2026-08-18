# Project Overview
- Game Title: Blockiverse VR
- High-Level Concept: A blocks/voxel game built for Meta Quest in VR (supporting single-player and multiplayer survival).
- Players: Single player, local/network multiplayer
- Inspiration / Reference Games: Minecraft VR, Blockiverse
- Tone / Art Direction: Pixel art/retro voxel style
- Target Platform: Android (Quest 3 / 3S)
- Screen Orientation / Resolution: Landscape 1920x1080
- Render Pipeline: URP (Universal Render Pipeline)

# Game Mechanics
## Core Gameplay Loop
Voxel mining, building, survival, and real-time multiplayer synchronization.
## Controls and Input Methods
XRI (XR Interaction Toolkit) with dual-stick movement/telemetry, custom hand tracking or controller-based interactions.

# UI
The project features world-space VR canvases with tracked device graphic raycasters. It implements a sophisticated, cross-platform composition layer routing architecture using the official **Unity XR Composition Layers** package (`com.unity.xr.compositionlayers` 2.4.0) with custom rendering scales and input mirroring, rather than Oculus-specific components.

# Key Asset & Context
- `Assets/Blockiverse/Scenes/Boot.unity` - The boot scene containing the full network/survival runtime stack and the active XR Rig.
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs` - The entry point for the project setup bootstrapper.
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Scenes.cs` - Handles bootstrapping scene assets and ensuring single root GameObjects.
- `Assets/Blockiverse/Tests/EditMode/BlockiverseBootstrapEditModeTests.cs` - Validates project, scene, and asset configuration assertions.
- `Assets/Blockiverse/Tests/EditMode/CompositionLayerUiEditModeTests.cs` - Asserts the correct structure and configuration of Unity XR Composition Layers on the menus.

# Analysis of Meta Project Setup Tool Recommendations
An inspection of the project settings, scene state, and visual reference context reveals three main items of interest:

1. **Duplicate XR Rig in Boot Scene (Real Issue)**:
   - **Current State**: The `Boot.unity` scene has two root `BlockiverseXRRig` GameObjects loaded simultaneously: one is a connected prefab instance, and the other is a regular (NotAPrefab) copy.
   - **Resolution**: Clean up the scene by removing the duplicate copy, leaving only one instance of the `BlockiverseXRRig` prefab.

2. **ASTC Texture Compression (Real Issue)**:
   - **Current State**: The project's active Android build subtarget is currently set to `Generic` instead of `ASTC`.
   - **Resolution**: Ensure the texture compression subtarget is set to `ASTC` to reduce loading times and GPU memory footprint on Quest 3 / 3S.

3. **OVROverlayCanvas Recommendation (False Positive / Intended Design)**:
   - **Current State**: The Meta Project Setup Tool recommends adding `OVROverlayCanvas` to world-space canvases to improve text quality.
   - **Resolution**: The project utilizes the cross-platform **Unity XR Composition Layers** package (`com.unity.xr.compositionlayers`) for menu rendering, as verified by `CompositionLayerUiEditModeTests`. Adding `OVROverlayCanvas` would conflict with this architecture and fail automated test assertions. Thus, the warning should be ignored/bypassed as it represents a false positive of the Oculus-specific tool.

# Implementation Steps

## Step 1: Run the Blockiverse Project Bootstrapper
- **Description**: Run the custom project bootstrapper to automatically apply Quest-optimized player settings (including ASTC texture compression) and regenerate a clean `Boot.unity` scene without stale or duplicate assets.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: No

## Step 2: Manually Inspect and Clean Up Duplicate Rig in Boot Scene (If Needed)
- **Description**: Open the `Boot.unity` scene and verify if there is still a duplicate `BlockiverseXRRig`. If any duplicate remains, delete the NotAPrefab duplicate and save the scene.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

## Step 3: Run Validation and Unit Tests
- **Description**: Execute the project's edit-mode and play-mode tests (specifically `BlockiverseBootstrapEditModeTests` and `CompositionLayerUiEditModeTests`) to guarantee that all configurations align with the expected design rules.
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: No

# Verification & Testing
1. **Verify Rig Uniqueness**:
   - Run a query or inspect the Hierarchy of `Boot.unity` to confirm exactly one `BlockiverseXRRig` root GameObject is present and active.
2. **Verify Texture Compression**:
   - Check `EditorUserBuildSettings.androidBuildSubtarget` in Unity's build settings to confirm it is set to `ASTC`.
3. **Run Test Suite**:
   - Open Unity Test Runner and run all EditMode tests. Ensure both `BlockiverseBootstrapEditModeTests` and `CompositionLayerUiEditModeTests` pass cleanly.
