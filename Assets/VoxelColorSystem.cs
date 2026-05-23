using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct VoxelColorSystem : ISystem
{
    private const float brushRadius = 3.0f;
    private const float rayLength = 200f;

    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        if (Camera.main == null || Mouse.current == null) return;

        // 1. Get Camera Center Ray
        UnityEngine.Ray cameraRay = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        // 2. Query Physics World
        if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physicsWorldSingleton)) return;
        var collisionWorld = physicsWorldSingleton.CollisionWorld;

        RaycastInput rayInput = new RaycastInput
        {
            Start = cameraRay.origin,
            End = cameraRay.origin + (cameraRay.direction * rayLength),
            Filter = CollisionFilter.Default
        };

        // Draw a red line in the Scene view for 2 seconds when you click
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Debug.DrawRay(cameraRay.origin, cameraRay.direction * rayLength, Color.red, 2.0f);
        }

        bool hitVoxelChunk = false;
        float3 hitPosition = float3.zero;
        Entity hitChunkEntity = Entity.Null;

        if (collisionWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit))
        {
            var entityManager = state.EntityManager;
            if (entityManager.HasComponent<ChunkCoordinate>(hit.Entity) &&
                entityManager.HasBuffer<VoxelDataElement>(hit.Entity))
            {
                hitVoxelChunk = true;
                hitPosition = hit.Position;
                hitChunkEntity = hit.Entity;
            }
            
            // Log if we click and hit something that IS NOT a chunk
            if (Mouse.current.leftButton.wasPressedThisFrame && !hitVoxelChunk)
            {
                Debug.Log($"[VoxelColorSystem] Ray hit Entity {hit.Entity.Index}, but it lacks ChunkCoordinate or VoxelDataElement.");
            }
        }
        else if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Log if we click and hit absolutely nothing
            Debug.Log("[VoxelColorSystem] Raycast did not hit any physics colliders.");
        }

        // 3. Update Cursor Target Indicator position/scale
        foreach (var transform in SystemAPI.Query<RefRW<LocalTransform>>().WithAll<CursorTargetTag>())
        {
            if (hitVoxelChunk)
            {
                transform.ValueRW.Position = hitPosition;
                transform.ValueRW.Scale = 0.35f; // Normal scale when visible
            }
            else
            {
                transform.ValueRW.Scale = 0.0f; // Scale to 0 to hide it
            }
        }

        // 4. Carving/Destroying terrain on Left Click
        if (hitVoxelChunk && Mouse.current.leftButton.isPressed)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Debug.Log($"[VoxelColorSystem] Successfully hit Terrain Chunk {hitChunkEntity.Index} at {hitPosition}. Carving...");
            }

            var entityManager = state.EntityManager;
            var settings = entityManager.GetComponentData<VoxelWorldSettings>(hitChunkEntity);
            var transform = entityManager.GetComponentData<LocalTransform>(hitChunkEntity);
            var buffer = entityManager.GetBuffer<VoxelDataElement>(hitChunkEntity);

            // Translate hit position to chunk's local space
            float3 localHitPos = math.mul(math.inverse(transform.Rotation), hitPosition - transform.Position) / transform.Scale;

            var voxelData = buffer.AsNativeArray();
            bool modified = false;

            int3 pSize = settings.ChunkSize + 1;

            // Determine affected voxel bounds
            int3 min = (int3)math.floor(localHitPos - brushRadius);
            int3 max = (int3)math.ceil(localHitPos + brushRadius);

            min = math.clamp(min, 0, pSize - 1);
            max = math.clamp(max, 0, pSize - 1);

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
                            int currentDensity = (int)(currentVal & 0xFF);

                            // Subtract a large chunk of density
                            int subtractAmount = 255; 
                            
                            int newDensity = math.max(0, currentDensity - subtractAmount);

                            // If the density actually changed, update the voxel data
                            if (newDensity != currentDensity)
                            {
                                // Keep upper 24 bits (materials/flags), insert new density into lower 8 bits
                                uint newVal = (currentVal & 0xFFFFFF00) | (uint)newDensity;
                                voxelData[index] = new VoxelDataElement { Value = newVal };
                                modified = true;
                            }
                        }
                    }
                }
            }

            // Trigger Remeshing if voxels were carved
            if (modified)
            {
                // Request remesh by updating component tags on the chunk
                entityManager.AddComponent<ChunkNeedsMeshingTag>(hitChunkEntity);
                entityManager.RemoveComponent<ChunkReadyTag>(hitChunkEntity);
            }
        }
    }
}
