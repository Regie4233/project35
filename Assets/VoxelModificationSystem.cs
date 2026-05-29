using Unity.Burst;
using Unity.Entities;


public partial struct VoxelModificationSystem : ISystem
{
   
    public void OnUpdate(ref SystemState state)
    {
        // Command buffers safely queue structural changes like spawning falling blocks
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Iterate through all chunk entities that have VoxelDataElements
        foreach (var item in SystemAPI.Query<DynamicBuffer<VoxelDataElement>>().WithEntityAccess())
        {
            var voxelBuffer = item.Item1;
            var entity = item.Item2;
            // CSG Subtraction: math.max(terrainSDF, -shapeSDF)
            // Example: digging a hole (shapeSDF = sphere)
            float shapeSDF = -1.0f; // a sphere of radius 1 at this exact voxel point
            
            var voxel = voxelBuffer[targetIndex];
            if (voxel.IsSolid())
            {
                // Smooth CSG Subtract
                voxel.SDF = math.max(voxel.SDF, -shapeSDF);
                
                // Write back to buffer
                voxelBuffer[targetIndex] = voxel; 

                // Trigger remesh
                ecb.AddComponent<ChunkNeedsRemeshTag>(entity);
            }
        }
    }
}

public struct ChunkNeedsRemeshTag : IComponentData { }