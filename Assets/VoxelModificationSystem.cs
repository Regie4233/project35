using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Mathematics;
using UnityEngine;
using Unity.Transforms;

[DisableAutoCreation]
public partial struct VoxelModificationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        /*
        // This system is disabled in favor of VoxelColorSystem.
        // Use the modern Input System API
        if (UnityEngine.InputSystem.Mouse.current == null || !UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame) return;

        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Raycast from Camera
        if (Camera.main == null) return;
        
        float2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));
        RaycastInput input = new RaycastInput
        {
            Start = ray.origin,
            End = ray.origin + ray.direction * 100f,
            Filter = CollisionFilter.Default
        };

        if (physicsWorld.CollisionWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
        {
            Entity hitEntity = hit.Entity;

            if (SystemAPI.HasComponent<VoxelWorldSettings>(hitEntity) && SystemAPI.HasBuffer<VoxelDataElement>(hitEntity))
            {
                var settings = SystemAPI.GetComponent<VoxelWorldSettings>(hitEntity);
                var transform = SystemAPI.GetComponent<LocalTransform>(hitEntity);
                var voxelBuffer = SystemAPI.GetBuffer<VoxelDataElement>(hitEntity);

                // Convert world hit point to local voxel space
                float3 localHitPoint = math.transform(math.inverse(transform.ToMatrix()), hit.Position);
                
                // Digging Radius
                int radius = 2;
                int3 pSize = settings.ChunkSize + 1;

                bool modified = false;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            float3 offset = new float3(dx, dy, dz);
                            if (math.length(offset) > radius) continue;

                            int3 voxelPos = (int3)math.round(localHitPoint + offset);

                            // Check bounds (staying within the padded grid)
                            if (voxelPos.x >= 0 && voxelPos.x < pSize.x &&
                                voxelPos.y >= 0 && voxelPos.y < pSize.y &&
                                voxelPos.z >= 0 && voxelPos.z < pSize.z)
                            {
                                int index = voxelPos.x + (voxelPos.y * pSize.x) + (voxelPos.z * pSize.x * pSize.y);
                                var voxel = voxelBuffer[index];
                                
                                // Decrease density (255 is solid, 0 is air)
                                int newDensity = math.max(0, (int)voxel.GetDensity() - 100);
                                
                                // Pack it back
                                uint packedData = (uint)newDensity;
                                packedData |= (uint)(voxel.GetMaterialID() << 8);
                                if (newDensity > (settings.IsoLevel * 255))
                                {
                                    packedData |= (1u << 24);
                                }

                                voxelBuffer[index] = new VoxelDataElement { Value = packedData };
                                modified = true;
                            }
                        }
                    }
                }

                if (modified)
                {
                    ecb.AddComponent<ChunkNeedsMeshingTag>(hitEntity);
                    ecb.RemoveComponent<ChunkReadyTag>(hitEntity);
                }
            }
        }
        */
    }
}
