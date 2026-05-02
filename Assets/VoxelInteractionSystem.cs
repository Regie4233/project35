using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using RaycastHit = Unity.Physics.RaycastHit;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class VoxelInteractionSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // 1. Check for Input (T key = Dig, Right Mouse = Build)
        if (Mouse.current == null || Keyboard.current == null) return;

        bool isTPressed = Keyboard.current.tKey.isPressed;
        bool isRightPressed = Mouse.current.rightButton.isPressed;

        if (!isTPressed && !isRightPressed) return;

        bool isDigging = isTPressed;
        int modificationAmount = isDigging ? -25 : 25; // Speed of modification
        float brushRadius = 2.5f;
        
        Vector2 screenPoint;
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            // If the cursor is locked (FPS style), raycast from the center of the screen
            screenPoint = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }
        else
        {
            // Otherwise, use the actual mouse position
            screenPoint = Mouse.current.position.ReadValue();
        }

        // 2. Get the Physics World (requires Unity Physics package)
        if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physicsWorldSingleton)) return;
        var collisionWorld = physicsWorldSingleton.CollisionWorld;

        // 3. Screen to World Raycast
        if (Camera.main == null) return;
        UnityEngine.Ray unityRay = Camera.main.ScreenPointToRay(screenPoint);
        
        float rayLength = 1000f;
        RaycastInput rayInput = new RaycastInput
        {
            Start = unityRay.origin,
            End = unityRay.origin + unityRay.direction * rayLength,
            Filter = CollisionFilter.Default
        };

        // Visual Debug: See where you are clicking in the Scene View
        Debug.DrawRay(rayInput.Start, rayInput.End - rayInput.Start, Color.red);

        if (collisionWorld.CastRay(rayInput, out RaycastHit hit))
        {
            Entity hitEntity = hit.Entity;

            // 4. Verify it's a Voxel Chunk
            if (EntityManager.HasComponent<ChunkCoordinate>(hitEntity) && 
                EntityManager.HasBuffer<VoxelDataElement>(hitEntity) &&
                EntityManager.HasComponent<LocalToWorld>(hitEntity))
            {
                var settings = EntityManager.GetComponentData<VoxelWorldSettings>(hitEntity);
                var buffer = EntityManager.GetBuffer<VoxelDataElement>(hitEntity);
                var ltw = EntityManager.GetComponentData<LocalToWorld>(hitEntity);
                
                // Use the entity's actual transform to find the local hit point
                // This accounts for the world position, rotation, and scale of the chunk.
                float3 localHitPos = math.transform(math.inverse(ltw.Value), hit.Position);

                var voxelData = buffer.AsNativeArray();
                bool modified = false;

                int3 pSize = settings.ChunkSize + 1;

                // 5. Determine affected voxel range (Optimization)
                int3 min = (int3)math.floor(localHitPos - brushRadius);
                int3 max = (int3)math.ceil(localHitPos + brushRadius);

                min = math.clamp(min, 0, pSize - 1);
                max = math.clamp(max, 0, pSize - 1);

                // 6. Iterate through local voxels
                for (int z = min.z; z <= max.z; z++)
                {
                    for (int y = min.y; y <= max.y; y++)
                    {
                        for (int x = min.x; x <= max.x; x++)
                        {
                            float3 voxelLocalPos = new float3(x, y, z);
                            float dist = math.distance(voxelLocalPos, localHitPos);

                            if (dist <= brushRadius)
                            {
                                int index = x + (y * pSize.x) + (z * pSize.x * pSize.y);
                                uint currentVal = voxelData[index].Value;
                                
                                // Extract density (bits 0-7)
                                int density = (int)(currentVal & 0xFF);
                                
                                // Apply modification with a simple spherical falloff
                                float falloff = 1.0f - (dist / brushRadius);
                                int change = (int)(modificationAmount * falloff);
                                
                                int newDensity = math.clamp(density + change, 0, 255);

                                if (newDensity != density)
                                {
                                    // Pack it back (keeping Material ID and flags intact)
                                    uint newVal = (currentVal & 0xFFFFFF00) | (uint)newDensity;
                                    voxelData[index] = new VoxelDataElement { Value = newVal };
                                    modified = true;
                                }
                            }
                        }
                    }
                }

                // 7. Trigger the Remeshing process
                if (modified)
                {
                    EntityManager.AddComponent<ChunkNeedsMeshingTag>(hitEntity);
                    EntityManager.RemoveComponent<ChunkReadyTag>(hitEntity);
                }
            }
        }
    }
}
