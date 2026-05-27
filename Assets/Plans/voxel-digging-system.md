# Project Overview
- **Game Title**: Voxel Terrain Digging
- **High-Level Concept**: First-person interaction with a smooth voxel world (Marching Cubes).
- **Players**: Single player.
- **Inspiration**: 7 Days to Die.
- **Target Platform**: PC (Windows, Unity 6).
- **Render Pipeline**: Custom URP-based (PC_RPAsset).

# Game Mechanics
## Core Gameplay Loop
- Player moves around the terrain (using existing `FreeCamera`).
- Player aims at terrain using a central UI crosshair.
- Player holds Left Click to "dig" into the terrain.
- The terrain deforms gradually, creating craters and tunnels.

## Controls and Input Methods
- **Mouse Look**: Handled by existing `FreeCamera`.
- **Left Click (Hold)**: Dig/Carve terrain.
- **Escape**: Unlock cursor.

# UI
- **Canvas**: Screen-space overlay.
- **Crosshair**: A simple white circle or cross image at `(0, 0)` anchored to the center.

# Key Asset & Context
- **Scripts**:
    - `Assets/FPSCursorController.cs`: Handles cursor locking.
    - `Assets/VoxelColorSystem.cs`: Updated to handle iterative digging and range checks.
- **Voxel Data**:
    - `VoxelDataElement`: Bits 0-7 = Density, 8-23 = MaterialID.
    - `MaterialID 1`: Dirt/Grass.
    - `MaterialID 2`: Stone.

# Implementation Steps
## 1. Cursor Management
- Create `Assets/FPSCursorController.cs`.
- Implement logic to lock the cursor to the center of the screen on mouse click and unlock on Escape.
- This ensures the camera movement feels natural for a first-person digging game.

## 2. UI Setup
- Create a UI Canvas with a crosshair Image in the center of the screen.
- This provides the "cursor in the middle" requested.

## 3. Refine Voxel Digging System
- Modify `Assets/VoxelColorSystem.cs`:
    - Update the raycast to use a max distance (e.g., 5-10 units).
    - Implement gradual density subtraction using `SystemAPI.Time.DeltaTime` to simulate the "work" of digging.
    - Reduce `brushRadius` (e.g., to 1.5) to make digging feel more precise than the current large-scale carving.
    - Add logic to check `VoxelDataElement.GetMaterialID()` and log what material is being dug.
    - Ensure `ChunkNeedsMeshingTag` is added only when changes occur.

# Verification & Testing
- **Cursor Locking**: Verify that clicking the screen locks the mouse and moving it rotates the camera.
- **Digging Interaction**: Verify that holding Left Click on terrain creates a hole at the hit point.
- **Range Limit**: Verify that terrain cannot be dug from very far away.
- **Visual Feedback**: Verify that the crosshair remains centered and visible.
