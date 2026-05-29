using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(PhysicsSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
public partial struct PhysicsSafetySystem : ISystem
{
    private EntityQuery _chunkQuery;
    private bool _hasReleased;

    public void OnCreate(ref SystemState state)
    {
        _chunkQuery = state.GetEntityQuery(typeof(ChunkReadyTag));
    }

    public void OnUpdate(ref SystemState state)
    {
        if ((state.World.Flags & (WorldFlags.Conversion | WorldFlags.Shadow)) != 0)
            return;

        // Instead of calculating a static grid size (which is broken by streaming),
        // we just wait until the generation pipeline is completely empty and we have at least some ready chunks!
        int generatingCount = state.EntityManager.CreateEntityQuery(typeof(ChunkNeedsNoiseTag)).CalculateEntityCount() + 
                              state.EntityManager.CreateEntityQuery(typeof(ChunkNeedsMeshingTag)).CalculateEntityCount();
        
        int readyCount = _chunkQuery.CalculateEntityCount();
        bool terrainReady = readyCount > 0 && generatingCount == 0;

        if (terrainReady && !_hasReleased)
        {
            Debug.Log($"[PhysicsSafetySystem] Terrain initial streaming complete! ({readyCount} chunks). Releasing physics objects.");
            _hasReleased = true;
        }

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (transform, velocity, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<PhysicsVelocity>>().WithEntityAccess())
        {
            if (SystemAPI.HasComponent<ChunkCoordinate>(entity)) continue;

            if (!SystemAPI.HasComponent<SafetyPosition>(entity))
            {
                ecb.AddComponent(entity, new SafetyPosition { Value = transform.ValueRO.Position });
                Debug.Log($"[PhysicsSafetySystem] Tracking start position for {entity}: {transform.ValueRO.Position}");
                continue;
            }

            var safety = SystemAPI.GetComponent<SafetyPosition>(entity);

            if (!terrainReady)
            {
                transform.ValueRW.Position = safety.Value;
                velocity.ValueRW.Linear = float3.zero;
                velocity.ValueRW.Angular = float3.zero;
            }
            else if (transform.ValueRO.Position.y < -50f && safety.Value.y > -40f)
            {
                Debug.LogWarning($"[PhysicsSafetySystem] Object {entity} fell through terrain! Resetting to {safety.Value}.");
                transform.ValueRW.Position = safety.Value;
                velocity.ValueRW.Linear = float3.zero;
                velocity.ValueRW.Angular = float3.zero;
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
