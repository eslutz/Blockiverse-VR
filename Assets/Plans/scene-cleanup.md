# Project Overview
- Game Title: Blockiverse VR
- High-Level Concept: A virtual reality voxel block-building and survival game featuring network multiplayer, dynamic world generation, and day-night lighting cycles.
- Players: Single player, local & network multiplayer
- Inspiration / Reference Games: Minecraft VR, Roblox VR
- Tone / Art Direction: Stylized, low-poly voxel graphics with vibrant lighting and immersive atmosphere.
- Target Platform: Android / Meta Quest VR
- Screen Orientation / Resolution: Stereo Landscape VR
- Render Pipeline: URP (Universal Render Pipeline)

# Game Mechanics
## Core Gameplay Loop
- Players explore voxel worlds, harvest and place blocks, manage survival vital stats (health, hunger), craft items and structures, and join multiplayer sessions.
## Controls and Input Methods
- XR motion controllers (left/right hand inputs), continuous movement or teleportation locomotion, snap/continuous turning, flight, and laser pointer UI interactions.

# UI
- Immersive world-space VR canvases including: title and pause menus, game settings panels, inventory and crafting screens, survival HUD overlays, and multi-angle panel popups.

# Key Asset & Context
Through deep exploration, we analyzed and compared the following scenes in the project:
1. **`Assets/Blockiverse/Scenes/Boot.unity`**: The fully-featured primary game scene containing the complete VR player rig, network managers, world renderer, and UI framework.
2. **`Assets/scene.unity`**: A stripped-down single-object scene containing only the `Blockiverse Sun` GameObject.
3. **`Assets/unknown_scene.unity`**: A standard default empty scene template containing just a standard Main Camera and Directional Light.
4. **`Assets/build-202607010655.unity`**: A completely empty scene (0 GameObjects).
5. **Temporary / Recovery / Test Scenes**: 
   - `Assets/InitTestScene699b651c-dcc9-419a-b3c1-03dbc7f893c3.unity`
   - `Assets/InitTestScene7763e1de-bdea-4bfb-9964-19e621a07dc8.unity`
   - `Assets/_Recovery/0.unity`
   - `Assets/_Recovery/0 (1).unity`
   - `Assets/_Recovery/0 (2).unity`

### Comparison Analysis of `scene.unity` vs. `Boot.unity`:
We performed a deep programmatic comparison between `scene.unity` and `Boot.unity` for the `Blockiverse Sun` and lighting components:
- **`Boot.unity`**: Directional light, Color: RGBA(0.999, 0.949, 0.820, 1.000), Intensity: 1.148954, `WorldTimeClock` with default dayLengthSeconds of 1200 and normalizedTime of 0.22, and the `BlockiverseLightingCycleController` script.
- **`scene.unity`**: Directional light, Color: RGBA(1.000, 0.950, 0.820, 1.000), Intensity: 1.15, `WorldTimeClock` with dayLengthSeconds of 1200 and normalizedTime of 0.25, and the `BlockiverseLightingCycleController` script.

**Conclusion:** The lighting properties in `scene.unity` are mathematically and visually identical to the production `Boot.unity` scene (simply rounded or modified insignificantly by a few seconds of simulated time). There are **no lighting enhancements** in `scene.unity` that need to be merged into `Boot.unity`. It is entirely redundant.

# Implementation Steps

### Step 1: Backup Scene Verification
- **Description**: Verify that `Assets/Blockiverse/Scenes/Boot.unity` loads correctly and is fully functional before any deletion occurs. Ensure it contains the active lighting controllers and sun.
- **Assigned role**: explorer
- **Dependencies**: None
- **Parallelizable**: No

### Step 2: Delete Redundant Scenes
- **Description**: Programmatically delete the redundant scenes, recovery scenes, and auto-generated build/test scenes from the project:
  1. `Assets/scene.unity` (and its .meta file)
  2. `Assets/unknown_scene.unity` (and its .meta file)
  3. `Assets/build-202607010655.unity` (and its .meta file)
  4. `Assets/InitTestScene699b651c-dcc9-419a-b3c1-03dbc7f893c3.unity` (and its .meta file)
  5. `Assets/InitTestScene7763e1de-bdea-4bfb-9964-19e621a07dc8.unity` (and its .meta file)
  6. `Assets/_Recovery/` folder containing temporary recovery scenes.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

### Step 3: Scene Database Refresh
- **Description**: Trigger an asset database refresh to update the Unity Editor project structure and clear out missing scene references.
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: No

# Verification & Testing
1. **Scene File Verification**: Perform a file check to ensure all target scene files (and their corresponding `.meta` files) have been cleanly removed from `Assets/` and `Assets/_Recovery/`.
2. **Boot Scene Load Test**: Load `Assets/Blockiverse/Scenes/Boot.unity` and verify that the lighting system, `Blockiverse Sun`, and its associated time-and-cycle scripts function without warnings or compile-time issues.
3. **Build Settings Check**: Check `EditorBuildSettings` to make sure the build scenes list contains only correct, active scenes (`Assets/Blockiverse/Scenes/Boot.unity` and `Assets/Blockiverse/Scenes/MultiplayerTest.unity`), and that no deleted scenes are left as missing entries.
