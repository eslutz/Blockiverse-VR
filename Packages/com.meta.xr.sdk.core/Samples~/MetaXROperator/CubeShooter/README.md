# Vaporwave Shooting Gallery

A VR shooting gallery built in Unity for Meta Quest. Grab the laser gun, shoot floating cubes, and earn points based on cube type.

## Scene Overview

The CubeShooter scene contains:

- **LaserGun** — Grabbable pistol that fires triangle projectiles. Grip button to grab, index trigger to fire.
- **Target Cubes** — 16 cubes spawn at random positions around the player with four visual styles and point values:
  - NeonBorder (1 pt) — glowing colored outline
  - NeonGrid (1 pt) — colored grid pattern
  - Vaporwave (2 pts) — gradient shader
  - JazzCup (3 pts) — 90s wavy pattern
- **ScoreManager** — Tracks and displays the current score.
- **TargetSpawner** — Maintains 16 active targets, respawning a new one each time one is destroyed.

**CubeShooter-Broken** is a copy of CubeShooter with intentional bugs (uses broken "alt" prefabs and scripts), useful for testing an agent's ability to debug and fix issues.

## Validating with Meta XR Operator

Meta XR Operator is an OpenXR API layer that lets AI agents interact with running VR apps via MCP tools.

With this project, an agent can take an actively running Unity VR application (i.e. in play mode) and:

1. **Orient itself** — capture screenshots and query the scene hierarchy to understand the layout
2. **Grab the gun** — position the controller near the gun and press grip
3. **Aim and shoot** — compute aim direction from the muzzle to a target, set the controller's aim pose, and pull the trigger
4. **Verify results** — confirm targets are destroyed, the score increments correctly per cube type, and new targets respawn

This enables automated end-to-end testing of the full gameplay loop without a human in the headset.

Paired with tools like [Unity MCP](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.0/manual/unity-mcp-overview.html) and [Horizon Debug Bridge](https://developers.meta.com/horizon/documentation/unity/ts-mqdh-mcp/), agents can also develop and iterate on features, not just validate them.

### Setup

Prerequisites: Meta XR Core SDK package and Meta XR Simulator installed in your Unity project.

Follow instructions for setup in the **Meta Project Setup Tool** or **Meta XR Settings window**. Check the status of Meta XR Operator in the Unity toolbar under **Meta**.

Next, import the Meta XR Operator skills for use with your AI agent. Skills can be found in the Meta XR Core SDK package: `Meta XR Core SDK/Editor/MetaXROperator/Skills`.

### Try It

Launch the CubeShooter scene in Play Mode, then prompt your agent with something like:

> Validate the currently running Unity app. Grab the gun, shoot the targets, and verify the score goes up.

With Unity MCP also configured, try the CubeShooter-Broken scene and prompt your agent to find and fix the bugs. For a more realistic test, try hiding the non-alt versions of the broken prefabs and scripts so the agent cannot see compare against the original working versions.
