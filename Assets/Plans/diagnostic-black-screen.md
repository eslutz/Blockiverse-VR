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
We are troubleshooting a critical issue: after the splash screen, the game loads to a black screen. Music is audible, but no world, rays, or hands are visible.

### Potential Root Causes:
1. **Menu Routing or Initialization Exception**: `BlockiverseMenuController` or `BlockiverseWorldSessionController` throws an exception during `Awake()` or `Start()` (e.g., in `ResolveRuntimeReferences()`), preventing the UI routing state from running. This leaves all menus (including the Title Menu) at their default Edit Mode state: `Canvas Enabled = False`, resulting in a completely black screen.
2. **Comfort Fade Overlay Stuck**: The `BlockiverseComfortTransition` or another screen-space fade canvas is stuck at `Alpha = 1.0` (pure black) and is not fading out correctly.
3. **Startup Loading Overlay Stuck**: The `BlockiverseStartupOverlay` (the title loading overlay) fails to auto-hide or get disabled correctly, blocking the entire view.
4. **Camera or Renderer Configuration Error**: The `Main Camera` has an invalid culling mask, or the Universal Render Pipeline asset/renderer is failing to draw geometry/UI.

# Implementation Steps

### Step 1: Create and Run Automated Play-Mode Diagnostic Tool
- **Description**: Create an editor script `Assets/Editor/BlockiversePlayModeDiagnostics.cs` that automates play mode diagnostics:
  1. Enters Play Mode programmatically.
  2. Subscribes to `Application.logMessageReceived` to capture any runtime warnings/errors or NullReferenceExceptions during startup.
  3. Waits for 60 updates/frames (approx. 1-2 seconds) to let all Awake, Start, and Coroutine initializations complete.
  4. Inspects and logs:
     - All active and inactive cameras, their enabled states, clear flags, and culling masks.
     - All canvas elements, their `enabled` properties, and associated `CanvasGroup` alpha values.
     - Specifically checks `Startup Loading Overlay` and any screen-space `Comfort Fade Overlay` to see if their alphas are blocking the view.
     - Position of `BlockiverseXRRig` and status of the VR controllers/tracking.
  5. Writes the diagnostic findings to `Assets/play_mode_diagnostics_report.txt`.
  6. Automatically exits Play Mode.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: No

### Step 2: Analyze Diagnostic Report
- **Description**: Execute the diagnostics script, inspect `Assets/play_mode_diagnostics_report.txt`, and pinpoint the exact source of the black screen (such as a specific NullReferenceException or a stuck overlay).
- **Assigned role**: explorer
- **Dependencies**: Step 1
- **Parallelizable**: No

### Step 3: Implement Fixes
- **Description**: Based on Step 2's analysis:
  - If a script exception is found: Apply precise code corrections to eliminate the exception.
  - If an overlay is stuck: Update the state machine or timer script (e.g. `BlockiverseComfortTransition` or `BlockiverseStartupOverlay`) to ensure it fades out properly under all circumstances.
  - If a scene configuration error is found: Programmatically fix the canvas, camera, or renderer settings in `Boot.unity` or the URP asset.
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: No

### Step 4: Re-Run Diagnostics and Validate
- **Description**: Run the diagnostics script again to confirm that the errors are eliminated, the camera is rendering, and the `Title Menu` (or relevant initial canvas) has successfully set its `Canvas.enabled = true`.
- **Assigned role**: developer
- **Dependencies**: Step 3
- **Parallelizable**: No

### Step 5: Clean Up Diagnostics
- **Description**: Remove `Assets/Editor/BlockiversePlayModeDiagnostics.cs` and `Assets/play_mode_diagnostics_report.txt` once validation passes to ensure no debugging files are committed.
- **Assigned role**: developer
- **Dependencies**: Step 4
- **Parallelizable**: No

# Verification & Testing
1. **Console Logs Check**: Verify that there are zero Errors, Exceptions, or critical Warnings in the console upon starting Play Mode.
2. **Active Canvas Verification**: Verify that the first active canvas (`Title Menu`) has `Canvas.enabled == true` and its `CanvasGroup.alpha == 1.0f`.
3. **No Stuck Black Fades**: Verify that any fade overlays (such as the comfort fade or loading overlay) have `Canvas.enabled == false` or `CanvasGroup.alpha == 0.0f`.
4. **Camera View Clear**: Ensure the camera rendering pipeline is outputting the correct clear background or skybox and not a solid black screen.
