using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


public struct NoiseGenerationJob : IJobParallelFor
{
    public int3 ChunkSize;
    public float3 ChunkWorldPosition;
    public float NoiseScale;
    public float IsoLevel;

    
    public NativeArray<VoxelDataElement> VoxelData;

    public void Execute(int index)
    {
        // Unflatten the 1D index back into 3D local coordinates
        int x = index % ChunkSize.x;
        int y = (index / ChunkSize.x) % ChunkSize.y;
        int z = index / (ChunkSize.x * ChunkSize.y);

        float3 worldPos = ChunkWorldPosition + new float3(x, y, z);
        
        // Sample 3D noise (using unity.mathematics noise.cnoise)
        float noiseValue = noise.cnoise(worldPos * NoiseScale);
        
        // Convert the noise float to a 0-255 density byte
        byte density = (byte)math.clamp((noiseValue + 1f) * 127.5f, 0, 255);
        
        // If density is higher than our threshold, it's solid terrain
        ushort materialId = density > (IsoLevel * 255)? (ushort)1 : (ushort)0;
        
        // Pack data into the unsigned integer
        uint packedData = density;
        packedData |= (uint)(materialId << 8);
        if (materialId > 0) 
        {
            packedData |= (1u << 24); // Flag bit 24 as Solid
        }

        VoxelData[index] = new VoxelDataElement { Value = packedData };
    }
}