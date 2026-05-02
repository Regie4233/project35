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
        // Use padded dimensions for indexing
        int3 pSize = ChunkSize + 1;
        
        // Unflatten the 1D index back into 3D local coordinates
        int x = index % pSize.x;
        int y = (index / pSize.x) % pSize.y;
        int z = index / (pSize.x * pSize.y);

        float3 worldPos = ChunkWorldPosition + new float3(x, y, z);
        
        // 2D Heightmap Noise (Base terrain shape)
        float2 pos2D = new float2(worldPos.x, worldPos.z) * NoiseScale * 0.5f;
        float heightNoise = noise.cnoise(pos2D);
        // Normalize roughly to 0..1 and scale up by chunk height max (e.g. 16 or 32)
        // Adjust baseline so it looks okay.
        float baseHeight = ChunkSize.y * 0.5f + (heightNoise * ChunkSize.y * 0.4f);

        // 3D Noise (Caves, overhangs, detail)
        float detailNoise = noise.cnoise(worldPos * NoiseScale * 2f);

        // Calculate raw density
        // Higher y = lower density (above ground)
        // Lower y = higher density (underground)
        // detailNoise modifies the density threshold
        float densityFloat = (baseHeight - worldPos.y) + (detailNoise * 5f);

        // Normalize density to roughly -1 to 1 for byte mapping
        float normalizedDensity = math.clamp(densityFloat / 10f, -1f, 1f);

        // Convert the noise float to a 0-255 density byte.
        // We will consider 128 as the surface iso-level.
        byte density = (byte)math.clamp((normalizedDensity + 1f) * 127.5f, 0, 255);
        
        // If density is higher than our threshold (isoLevel scaled to 0-255), it's solid terrain
        bool isSolid = density > (IsoLevel * 255);
        
        // Simple multiple materials
        // e.g. Deep underground = stone (2), near surface = dirt/grass (1), air = (0)
        ushort materialId = 0;
        if (isSolid)
        {
            if (worldPos.y < baseHeight - 3)
            {
                materialId = 2; // Stone
            }
            else
            {
                materialId = 1; // Dirt/Grass
            }
        }
        
        // Pack data into the unsigned integer
        uint packedData = density;
        packedData |= (uint)(materialId << 8);
        if (isSolid)
        {
            packedData |= (1u << 24); // Flag bit 24 as Solid
        }

        VoxelData[index] = new VoxelDataElement { Value = packedData };
    }
}
