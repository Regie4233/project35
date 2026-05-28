# Project Overview
- **Game Title**: Voxel Terrain Digging
- **High-Level Concept**: A first-person voxel interaction prototype using Marching Cubes and Unity Entities.
- **Players**: Single player.
- **Target Platform**: PC (Windows, Unity 6).
- **Render Pipeline**: Custom URP-based (PC_RPAsset).

# Game Mechanics
## Core Gameplay Loop
- Terrain is generated from noise at runtime via ECS.
- A test sphere (`Spherex`) falls under gravity and should collide with the generated surface.
- The `Voxel World Settings` and `Spherex` are both authored in the `worldsub` SubScene.

## Controls and Input Methods
- **Gravity**: Provided by Unity Physics simulation.
- **Safety**: `PhysicsSafetySystem` holds the sphere at its spawn point until terrain chunks are "Ready" to prevent falling through the void during initialization.

# UI
- N/A (Standard Scene/Game view for physics test).

# Key Asset & Context
- **Scripts**:
    - `Assets/MarchingCubesJob.cs`: Generates the mesh and collider geometry.
    - `Assets/PhysicsSafetySystem.cs`: Manages the "initialization lock" for physics objects.
    - `Assets/ChunkGenerationSystem.cs`: Handles chunk spawning and collider assignment.

# Implementation Steps
## 1. Fix Winding and Normals (MarchingCubesJob.cs)
- Update the `MarchingCubesJob` to ensure triangle winding `(v0, v2, v1)` results in clockwise faces when viewed from the "Air" side (where `density < threshold`).
- Confirm `GetGradientNormal` returns a vector pointing into the "Air".
- This ensures the `PhysicsCollider` is solid from the top down.
- **Dependency**: None.

## 2. Refine Safety Logic (PhysicsSafetySystem.cs)
- Update the system to ignore "Conversion" or "Shadow" worlds. Currently, it sees 0 chunks in the conversion world and permanently locks the sphere's entity in that context.
- Add a `Debug.Log` when `terrainReady` is first detected in the `Default World` (Play Mode).
- **Dependency**: Step 1.

# Verification & Testing
- **Step 1**: Enter Play Mode.
- **Step 2**: Check Console for "[PhysicsSafetySystem] Terrain is Ready. Releasing physics objects."
- **Step 3**: Observe `Spherex` falling.
- **Step 4**: Confirm `Spherex` stops at the terrain surface.
