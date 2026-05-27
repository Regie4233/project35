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

        int expectedChunks = 0;
        int worldCount = 0;
        foreach (var settings in SystemAPI.Query<RefRO<VoxelWorldSettings>>().WithNone<ChunkCoordinate>())
        {
            expectedChunks += settings.ValueRO.GridSize.x * settings.ValueRO.GridSize.y * settings.ValueRO.GridSize.z;
            worldCount++;
        }
        foreach (var (settings, coord) in SystemAPI.Query<RefRO<VoxelWorldSettings>, RefRO<ChunkCoordinate>>())
        {
            if (coord.ValueRO.Value.x == -1)
            {
                expectedChunks += settings.ValueRO.GridSize.x * settings.ValueRO.GridSize.y * settings.ValueRO.GridSize.z;
                worldCount++;
            }
        }

        int readyCount = _chunkQuery.CalculateEntityCount();
        bool terrainReady = expectedChunks > 0 && readyCount >= expectedChunks;

        if (terrainReady && !_hasReleased)
        {
            Debug.Log($"[PhysicsSafetySystem] All Terrains ({worldCount} worlds) are Ready ({readyCount}/{expectedChunks} chunks). Releasing physics objects.");
            _hasReleased = true;
        }
        else if (!terrainReady && _hasReleased)
        {
            _hasReleased = false;
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
