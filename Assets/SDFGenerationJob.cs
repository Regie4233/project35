using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct SDFGenerationJob : IJobParallelFor
{
    public int3 ChunkSize;
    public float3 ChunkWorldPosition;
    public float ChunkScale; // Physical size of this chunk in world space
    
    public float NoiseScale;
    public float IsoLevel; // Base terrain height

    public NativeArray<VoxelDataElement> VoxelData;

    public void Execute(int index)
    {
        int x = index % ChunkSize.x;
        int y = (index / ChunkSize.x) % ChunkSize.y;
        int z = index / (ChunkSize.x * ChunkSize.y);

        // Voxel spacing depends on chunk scale
        float3 voxelSpacing = new float3(ChunkScale) / new float3(ChunkSize);
        float3 localPos = new float3(x, y, z) * voxelSpacing;
        float3 worldPos = ChunkWorldPosition + localPos;
        
        // Base terrain SDF (flat plane)
        // Distance from current Y to the IsoLevel
        float baseSDF = worldPos.y - IsoLevel; 
        
        // Displace the surface using 3D noise
        float noiseValue = noise.cnoise(worldPos * NoiseScale);
        float finalSDF = baseSDF - (noiseValue * 20f); // Amplitude of noise

        ushort materialId = finalSDF <= 0f ? (ushort)1 : (ushort)0;

        VoxelData[index] = new VoxelDataElement 
        { 
            SDF = finalSDF,
            MaterialFlags = materialId
        };
    }
}
