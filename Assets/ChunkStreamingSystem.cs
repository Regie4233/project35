using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateBefore(typeof(ChunkGenerationSystem))]
public partial class ChunkStreamingSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (UnityEngine.Camera.main == null) return;
        
        Entity settingsEntity = Entity.Null;
        foreach (var (settingsComp, coord, entity) in SystemAPI.Query<RefRO<VoxelWorldSettings>, RefRO<ChunkCoordinate>>().WithEntityAccess())
        {
            if (coord.ValueRO.Value.x == -999)
            {
                settingsEntity = entity;
                break;
            }
        }
        if (settingsEntity == Entity.Null) return;
        
        float3 playerPos = UnityEngine.Camera.main.transform.position;
        var settings = SystemAPI.GetComponent<VoxelWorldSettings>(settingsEntity);
        
        int3 chunkSize = settings.ChunkSize;
        int renderDist = settings.RenderDistance;
        
        // Find player coordinate
        int playerX = (int)math.floor(playerPos.x / chunkSize.x);
        int playerZ = (int)math.floor(playerPos.z / chunkSize.z);
        
        // Calculate desired coordinates
        var desiredCoords = new NativeHashSet<int3>(renderDist * 2 * renderDist * 2 * settings.GridSize.y, Allocator.Temp);
        
        for (int x = playerX - renderDist; x <= playerX + renderDist; x++)
        {
            if (x < 0 || x >= settings.GridSize.x) continue; // Clamp to GridSize
            
            for (int z = playerZ - renderDist; z <= playerZ + renderDist; z++)
            {
                if (z < 0 || z >= settings.GridSize.z) continue; // Clamp to GridSize
                
                // Add all vertical chunks for this X/Z coordinate
                for (int y = 0; y < settings.GridSize.y; y++)
                {
                    desiredCoords.Add(new int3(x, y, z));
                }
            }
        }
        
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        
        // Unload chunks outside desired coords and track which ones are already active/generating
        foreach (var (coord, entity) in SystemAPI.Query<RefRO<ChunkCoordinate>>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).WithEntityAccess())
        {
            // Skip the authoring entity marker
            if (coord.ValueRO.Value.x == -999) continue;
            // Skip the pool chunks
            if (coord.ValueRO.Value.Equals(new int3(-1, -1, -1))) continue;
            
            if (!desiredCoords.Contains(coord.ValueRO.Value))
            {
                // Chunk is no longer desired, return to pool
                ecb.AddComponent<Disabled>(entity);
                ecb.SetComponent(entity, new ChunkCoordinate { Value = new int3(-1, -1, -1) });
                // We keep the mesh and collider attached, they will be overwritten when awakened
            }
            else
            {
                // Chunk is desired and already active (or generating), remove from set so we don't spawn it
                desiredCoords.Remove(coord.ValueRO.Value);
            }
        }
        
        // Reactivate chunks for missing coords from the pool
        var missingCoordsArray = desiredCoords.ToNativeArray(Allocator.Temp);
        int missingIndex = 0;
        
        if (missingIndex < missingCoordsArray.Length)
        {
            // Search the pool for available chunks (coord == -1)
            foreach (var (coord, entity) in SystemAPI.Query<RefRW<ChunkCoordinate>>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).WithEntityAccess())
            {
                if (!coord.ValueRO.Value.Equals(new int3(-1, -1, -1))) continue; // Only pull true pool chunks!
                
                if (missingIndex >= missingCoordsArray.Length) break;
                
                int3 newCoord = missingCoordsArray[missingIndex++];
                
                // Wake it up, but KEEP IT DISABLED so the old mesh doesn't pop-in!
                // ChunkGenerationSystem will remove the Disabled tag once the new mesh is perfectly ready.
                ecb.SetComponent(entity, new ChunkCoordinate { Value = newCoord });
                
                // Set its position
                float3 pos = new float3(newCoord.x * chunkSize.x, newCoord.y * chunkSize.y, newCoord.z * chunkSize.z);
                ecb.SetComponent(entity, LocalTransform.FromPosition(pos));
                
                // Tell the generation pipeline to recalculate it
                ecb.AddComponent<ChunkNeedsNoiseTag>(entity);
            }
            
            if (missingIndex < missingCoordsArray.Length)
            {
                UnityEngine.Debug.LogWarning("[ChunkStreamingSystem] Chunk pool exhausted! Need larger buffer size.");
            }
        }
        
        ecb.Playback(EntityManager);
        ecb.Dispose();
        desiredCoords.Dispose();
        missingCoordsArray.Dispose();
    }
}
