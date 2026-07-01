# Project Overview
- Game Title: Blockiverse VR
- High-Level Concept: A voxel-based VR survival and creative game for Android/Quest.
- Players: Single player, Local/Network Multiplayer.
- Inspiration / Reference Games: Minecraft VR.
- Tone / Art Direction: Voxel-stylized.
- Target Platform: Android (Quest).
- Render Pipeline: URP.

# Game Mechanics
## Core Gameplay Loop
The game starts in a "Boot" scene where the player interacts with a 3D menu. A "mini-world" (a small voxel region) should be visible as a backdrop. Players can start new worlds, load existing ones, or adjust settings.
## Controls and Input Methods
VR controllers (Quest) with Ray Interactors for UI. Locomotion includes Teleport and Glide.

# UI
The Menu is a world-space canvas attached to the XR Rig's "Camera Offset". It uses "Composition Layers" (Meta XR feature) for high-quality rendering. The current layout has overlapping elements in the Comfort Settings panel.

# Key Asset & Context
- `Assets/Blockiverse/Scenes/Boot.unity`: The initial scene.
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`: Editor script responsible for setting up the project and scene.
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.XrRig.cs`: Handles XR Rig and Comfort Menu generation.
- `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`: Manages the voxel world (mini-world).
- `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`: Renders the voxel world chunks.

# Implementation Steps

## Step 1: Prevent Duplicate Root Objects in Bootstrapper
Modify the `BlockiverseProjectBootstrapper` logic to properly detect and reuse existing root objects (XR Rig and Canvases) instead of spawning new ones when "Bootstrap Unity Quest Project" is run multiple times.
- **Files**: `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Scenes.cs`
- **Action**: Update `EnsureRootGameObject` (or equivalent) to find objects in the specific scene rather than just relying on `GameObject.Find`.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

## Step 2: Cleanup Scene Duplicates
Remove the redundant `BlockiverseXRRig` and `Blockiverse Menu Canvas` objects from the `Boot` scene. Fix the "Broken text PPtr" error by re-saving the scene after cleanup.
- **Files**: `Assets/Blockiverse/Scenes/Boot.unity`
- **Action**: Open scene, delete 1 redundant rig and 5 redundant root canvases. Save scene.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

## Step 3: Fix Comfort Settings Menu Layout
Adjust the vertical spacing and positioning logic in the `EnsureXrRigComfortMenu` method to prevent overlapping labels and controls.
- **Files**: `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.XrRig.cs`
- **Action**: Update `anchoredPosition` values for `Title`, `Movement Label`, `Glide Toggle`, etc., to ensure a clean vertical flow.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

## Step 4: Fix Menu Mini-World Initialization
Ensure the `CreativeWorldManager` in the `Boot` scene is correctly configured and that its `Awake` initialization doesn't fail due to missing dependencies (like the Hotbar or Material).
- **Files**: `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Scenes.cs`
- **Action**: Check if `InitializeDefaultWorld` is being called and if `VoxelWorldRenderer` is successfully baking the spawn region. Ensure `hotbar` is correctly assigned in the Boot scene via the Bootstrapper.
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: No

# Verification & Testing
1. **Manual Check**: Run the "Bootstrap Unity Quest Project" menu item twice and verify no duplicate Rigs or Canvases are created.
2. **Visual Inspection**: Open the `Comfort Settings Menu` in the editor and verify that labels and toggles are no longer overlapping.
3. **Play Mode Test**: Enter Play Mode in the `Boot` scene.
   - Verify that the "mini-world" voxel chunks appear around the player.
   - Verify that the Main Menu is visible and functional.
4. **Automated Test**: Run `BootScenePlayModeTests` to ensure basic rig and HUD initialization still passes.
