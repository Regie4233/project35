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
            // Example: Player destroys the block at local index 512
            int targetIndex = 512; 
            
            var voxel = voxelBuffer[targetIndex];
            if (voxel.IsSolid())
            {
                // Convert to Air
                voxel.Value = 0; 
                
                // Note: DynamicBuffer elements are value types. We must write the copy back to the buffer.
                voxelBuffer[targetIndex] = voxel; 

                // Trigger a re-mesh tag component so the meshing system knows to rebuild
                ecb.AddComponent<ChunkNeedsRemeshTag>(entity);
            }
        }
    }
}

public struct ChunkNeedsRemeshTag : IComponentData { }