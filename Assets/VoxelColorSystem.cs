using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class VoxelColorSystem : SystemBase
{
    private const float brushRadius = 2.0f;
    private const float rayLength = 100f;
    private const float interactionRange = 25.0f;
    private const float digRate = 800.0f;

    protected override void OnCreate()
    {
    }

    protected override void OnUpdate()
    {
        if (Camera.main == null || Mouse.current == null) return;

        if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physicsWorldSingleton))
        {
            return;
        }

        var physicsWorld = physicsWorldSingleton.CollisionWorld;

        // 1. Get Camera Center Ray
        UnityEngine.Ray cameraRay = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        RaycastInput rayInput = new RaycastInput
        {
            Start = cameraRay.origin,
            End = cameraRay.origin + (cameraRay.direction * rayLength),
            Filter = CollisionFilter.Default
        };

        bool hitVoxelChunk = false;
        float3 hitPosition = float3.zero;
        Entity hitChunkEntity = Entity.Null;
        float hitDistance = float.MaxValue;

        if (physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit))
        {
            if (EntityManager.HasComponent<ChunkCoordinate>(hit.Entity) &&
                EntityManager.HasBuffer<VoxelDataElement>(hit.Entity))
            {
                hitVoxelChunk = true;
                hitPosition = hit.Position;
                hitChunkEntity = hit.Entity;
                hitDistance = hit.Fraction * rayLength;
            }
        }

        bool withinRange = hitVoxelChunk && (hitDistance <= interactionRange);

        // 3. Update Cursor Target Indicator
        foreach (var transform in SystemAPI.Query<RefRW<LocalTransform>>().WithAll<CursorTargetTag>())
        {
            if (withinRange)
            {
                transform.ValueRW.Position = hitPosition;
                transform.ValueRW.Scale = 0.4f; 
            }
            else
            {
                transform.ValueRW.Scale = 0.0f; 
            }
        }

        // 4. Digging
        if (withinRange && Mouse.current.leftButton.isPressed)
        {
            var settings = EntityManager.GetComponentData<VoxelWorldSettings>(hitChunkEntity);
            var chunkTransform = EntityManager.GetComponentData<LocalTransform>(hitChunkEntity);
            var buffer = EntityManager.GetBuffer<VoxelDataElement>(hitChunkEntity);

            float3 localHitPos = math.mul(math.inverse(chunkTransform.Rotation), hitPosition - chunkTransform.Position) / chunkTransform.Scale;

            var voxelData = buffer.AsNativeArray();
            bool modified = false;

            int3 pSize = settings.ChunkSize + 1;

            int3 min = (int3)math.floor(localHitPos - brushRadius);
            int3 max = (int3)math.ceil(localHitPos + brushRadius);

            min = math.clamp(min, 0, pSize - 1);
            max = math.clamp(max, 0, pSize - 1);

            float deltaTime = SystemAPI.Time.DeltaTime;
            int subtractAmount = (int)(digRate * deltaTime);
            if (subtractAmount <= 0) subtractAmount = 1;

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

                            if (currentDensity > 0)
                            {
                                int newDensity = math.max(0, currentDensity - subtractAmount);

                                if (newDensity != currentDensity)
                                {
                                    bool isSolid = (newDensity > (settings.IsoLevel * 255));
                                    uint newVal = (currentVal & 0xFEFFFF00) | (uint)newDensity;
                                    if (isSolid) newVal |= (1u << 24);
                                    
                                    voxelData[index] = new VoxelDataElement { Value = newVal };
                                    modified = true;
                                }
                            }
                        }
                    }
                }
            }

            if (modified)
            {
                EntityManager.AddComponent<ChunkNeedsMeshingTag>(hitChunkEntity);
                EntityManager.RemoveComponent<ChunkReadyTag>(hitChunkEntity);
                
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    Debug.Log($"[VoxelColorSystem] Digging Chunk {hitChunkEntity.Index} at distance {hitDistance:F1}m...");
                }
            }
        }
    }
}



