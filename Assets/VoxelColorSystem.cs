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

        // 1. Get Mouse/Screen Ray
        UnityEngine.Ray cameraRay;
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            // Center of screen if camera lock is active
            cameraRay = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        }
        else
        {
            // Direct mouse position
            Vector2 mousePos = Mouse.current.position.ReadValue();
            cameraRay = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));
        }

        // 2. Query Physics World
        if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physicsWorldSingleton)) return;
        var collisionWorld = physicsWorldSingleton.CollisionWorld;

        RaycastInput rayInput = new RaycastInput
        {
            Start = cameraRay.origin,
            End = cameraRay.origin + (cameraRay.direction * rayLength),
            Filter = CollisionFilter.Default
        };

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

        // 4. Painting terrain on Left Click
        if (hitVoxelChunk && Mouse.current.leftButton.isPressed)
        {
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
                            int density = (int)(currentVal & 0xFF);

                            // Only paint solid voxel surface (not air)
                            if (density > (settings.IsoLevel * 255))
                            {
                                ushort currentMat = (ushort)((currentVal >> 8) & 0xFFFF);
                                
                                // Material ID 3 is Red / Custom Paint
                                if (currentMat != 3)
                                {
                                    uint newVal = (currentVal & 0xFF0000FF) | (3u << 8); // Pack Material ID = 3
                                    voxelData[index] = new VoxelDataElement { Value = newVal };
                                    modified = true;
                                }
                            }
                        }
                    }
                }
            }

            // Trigger Remeshing if voxels were painted
            if (modified)
            {
                // Request remesh by updating component tags on the chunk
                entityManager.AddComponent<ChunkNeedsMeshingTag>(hitChunkEntity);
                entityManager.RemoveComponent<ChunkReadyTag>(hitChunkEntity);
            }
        }
    }
}
