# Project Overview
- Game Title: Blockiverse VR
- High-Level Concept: A VR block-based sandbox and exploration game optimized for VR headsets.
- Players: Single-player VR
- Target Platform: Android (Meta Quest)
- Render Pipeline: URP (Universal Render Pipeline)

# Game Mechanics
## Core Gameplay Loop
Building, exploring, and interacting with blocks in a virtual reality space.
## Controls and Input Methods
VR controllers (Meta Quest / OpenXR), supporting movement, block interaction, and menus.

# UI
Not directly applicable to gameplay UI, but relates to Editor-only settings UI and toolbar customization to keep a clean, warning-free development environment in Unity 6.

# Key Asset & Context
- **Preference Storage**: Unity Editor's `EditorPrefs` key `"Meta.XR.SDK.StatusIcon.Enabled"`.
- **Warning Origin**: `Meta.XR.Editor.StatusMenu.StatusIcon:Enable()` in `Library/PackageCache/com.meta.xr.sdk.core@1ed001f50700/Editor/Utils/StatusMenu/StatusIcon.cs` attempts to inject itself into the Unity Editor Main Toolbar via an unsupported reflection method on Unity 6.

# Implementation Steps

## Step 1: Disable the Status Icon via Editor UI Preferences
- **Description**: Open the Preferences window and disable the Status Icon to prevent it from loading on the toolbar.
  1. Open the Unity Editor.
  2. Navigate to **Edit > Preferences > Meta XR** (on macOS: **Unity > Settings > Preferences > Meta XR**).
  3. Look for the **StatusIcon** / **Meta XR Tools** setting.
  4. Toggle/Uncheck the setting to disable it.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: No

## Step 2: Set EditorPrefs via Automation Script (Optional/Fallback)
- **Description**: Add a simple editor initialization script to programmatically turn off `Meta.XR.SDK.StatusIcon.Enabled` for all developers working on the repository, preventing the warning from appearing even on clean checkouts.
  1. Create a script `Assets/Blockiverse/Editor/MetaToolbarRemediator.cs`.
  2. Implement `[InitializeOnLoad]` to run on startup.
  3. Set `EditorPrefs.SetBool("Meta.XR.SDK.StatusIcon.Enabled", false);`.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

# Verification & Testing
1. Restart the Unity Editor or trigger a domain reload.
2. Confirm that the warning: `"We have detected that your project includes custom elements added to the Unity Editor's main toolbar using unsupported methods..."` is no longer logged in the Console.
3. Verify that the "Meta XR Tools" icon is removed from the toolbar (and the editor is clean), while the same tools remain accessible through standard menus like **Oculus** or **Window > Meta XR**.
