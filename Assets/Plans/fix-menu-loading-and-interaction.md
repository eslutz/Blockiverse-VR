# Project Overview
- **Game Title**: Blockiverse VR
- **High-Level Concept**: Voxel survival and creative experience for Meta Quest.
- **Players**: Single player (local) and LAN multiplayer.
- **Render Pipeline**: Universal Render Pipeline (URP).
- **Target Platform**: Android (Meta Quest).

# Game Mechanics
## Core Gameplay Loop
The player starts in a "mini-world" (Title World) where they can manage saves and settings. They then transition into generated survival or creative worlds.
## Controls and Input Methods
- **XR Interaction**: Tracked-device rays are used for all UI interactions.
- **Composition Layers**: UI is rendered via hardware composition layers for superior clarity, with a proxy interaction surface handling the physical raycasts in the Unity scene.

# UI
The menu system uses a "Routed Composition" architecture:
- **Display**: High-quality compositor Quad layer.
- **Interaction Proxy**: A Unity GameObject with a `MeshCollider` and `XRSimpleInteractable` on the `Interaction` layer (10).
- **Routing**: `InteractableUIMirror` maps scene-space ray hits on the proxy to UI events on the compositor canvas.

# Key Asset & Context
- `Assets/Blockiverse/Scenes/Boot.unity`: The main entry scene containing the menu and rig.
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Scenes.cs`: Logic for scene setup and object instantiation.
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.CompositionLayers.cs`: Logic for configuring the composition layer and interaction routing.
- `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`: Handles automatic loading of the menu world.

# Implementation Steps

## Step 1: Fix Boot Scene Duplication and Cleanup
The current `Boot` scene has multiple duplicated Rigs and Canvases. The bootstrapper's cleanup logic only removes the first instance of a root object, leaving duplicates behind.
- **Modify `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Scenes.cs`**:
    - Update `EnsureBootSceneRig` to find and destroy ALL root GameObjects with the name `BlockiverseProject.XrRigRootName`.
    - Update `EnsureBootSceneCreativeWorld` to find and destroy ALL root GameObjects with the name `BlockiverseProject.CreativeWorldRootName`.
    - Update `EnsureEventSystem` to find and destroy ALL root GameObjects with the provided `eventSystemName`.
    - Update `EnsureBootSceneNetworkStack` to find and destroy ALL root GameObjects with the name `NetworkManagerRootName`.
- **Assigned Role**: Developer
- **Dependencies**: None

## Step 2: Fix Interaction Routing Configuration
The `XRSimpleInteractable` requires its `colliders` list to be explicitly populated to ensure reliable raycasting.
- **Modify `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.CompositionLayers.cs`**:
    - In `EnsureMenuCompositionSurface`, after creating the `XRSimpleInteractable` and `MeshCollider`, add the collider to the interactable's `colliders` list:
      ```csharp
      simpleInteractable.colliders.Clear();
      simpleInteractable.colliders.Add(meshCollider);
      ```
- **Assigned Role**: Developer
- **Dependencies**: None

## Step 3: Fix Menu World Loading
The `CreativeWorldManager` is currently configured to NOT initialize the world on awake in the `Boot` scene.
- **Modify `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Scenes.cs`**:
    - In `EnsureBootSceneCreativeWorld`, verify that `manager.InitializeDefaultWorldOnAwake = true;` is being set and correctly saved to the scene.
- **Assigned Role**: Developer
- **Dependencies**: Step 1

## Step 4: Execute Bootstrapper and Verify
Run the updated bootstrapper to apply all fixes and clean up the project state.
- **Action**: Run the `Blockiverse/Bootstrap Unity Quest Project` menu item.
- **Verification**: 
    - Inspect the `Boot` scene hierarchy to ensure only one Rig, one World, and one Event System exist.
    - Check the `Creative World` object to ensure `Initialize Default World On Awake` is checked.
    - Check the `Blockiverse Menu Composition Surface` to ensure the `XRSimpleInteractable` has 1 collider assigned.
- **Assigned Role**: Developer
- **Dependencies**: Steps 1, 2, 3

# Verification & Testing
- **Scene Hierarchy Check**: Open `Boot.unity` and verify no duplicate root objects exist.
- **Component Audit**: Use the Inspector to verify `XRSimpleInteractable` and `CreativeWorldManager` settings.
- **Interaction Test (Editor)**: Enter Play Mode in the Editor and verify that the "mini-world" generates and the menu ray interactions are responsive (hover highlights, button clicks).
- **PlayMode Test**: Run existing `BootScenePlayModeTests.cs` to ensure basic scene integrity is maintained.
