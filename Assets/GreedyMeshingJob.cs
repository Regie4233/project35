using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


public struct GreedyMeshingJob : IJob
{
    
    public NativeArray<VoxelDataElement> VoxelData;
    
    public int3 ChunkSize;

    public NativeList<float3> Vertices;
    public NativeList<ushort> Indices;

    public void Execute()
    {
        // Sweep over each of the 3 axes (X, Y, Z)
        for (int d = 0; d < 3; d++)
        {
            int u = (d + 1) % 3;
            int v = (d + 2) % 3;

            int3 x = int3.zero;
            int3 q = int3.zero;
            q[d] = 1;

            // Generate a boolean mask for the current 2D slice
            NativeArray<bool> mask = new NativeArray<bool>(ChunkSize[u] * ChunkSize[v], Allocator.Temp);

            // Iterate through the slices of the chunk along the current axis
            for (x[d] = -1; x[d] < ChunkSize[d];)
            {
                int n = 0;
                for (x[v] = 0; x[v] < ChunkSize[v]; ++x[v])
                {
                    for (x[u] = 0; x[u] < ChunkSize[u]; ++x[u])
                    {
                        // Check the block at x and x + q to see if a face is exposed
                        // If exposed, set mask[n] to true
                        n++;
                    }
                }
                
                x[d]++;
                n = 0;
                
                // Sweep over the mask to generate quads
                for (int j = 0; j < ChunkSize[v]; j++)
                {
                    for (int i = 0; i < ChunkSize[u];)
                    {
                        if (mask[n])
                        {
                            // Calculate max contiguous width (w) and height (h)
                            // Emit a single quad of size w * h to the Vertices/Indices NativeLists
                            // Set evaluated mask positions to false to prevent duplicate quads
                        }
                        else
                        {
                            i++;
                            n++;
                        }
                    }
                }
            }
            mask.Dispose();
        }
    }
}