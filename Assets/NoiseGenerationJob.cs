using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


public struct NoiseGenerationJob : IJobParallelFor
{
    public int3 ChunkSize;
    public float3 ChunkWorldPosition;
    public float NoiseScale; // Base scale (kept for legacy/base use if needed)
    public float IsoLevel;
    public float2 NoiseOffset;
    
    public float ContinentScale;
    public float WarpScale;
    public float WarpIntensity;
    public float MountainScale;
    public float MountainHeight;
    public float TrenchScale;
    public float TrenchDepth;
    public float MaxSkyHeight;
    public float MaxBedrockDepth;
    public float SeaLevel;

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
        
        // A. Domain Warping for Coastal Islands
        float warpX = worldPos.x + noise.cnoise(new float2(worldPos.x + NoiseOffset.x, worldPos.z) * WarpScale) * WarpIntensity;
        float warpZ = worldPos.z + noise.cnoise(new float2(worldPos.x, worldPos.z + NoiseOffset.y) * WarpScale) * WarpIntensity;

        // B. Continental Mask
        float landNoise = noise.cnoise(new float2(warpX, warpZ) * ContinentScale);
        
        // Start with a density based on height vs SeaLevel
        float density = (SeaLevel - worldPos.y);
        
        // Add the continental landmass height (scale it so continents are significantly higher than ocean floor)
        density += landNoise * 40.0f; 

        // C. Meso Topography (Ridge Noise for Tectonic Mountains)
        float peakValue = 1.0f - math.abs(noise.cnoise(new float2(worldPos.x + NoiseOffset.x, worldPos.z + NoiseOffset.y) * MountainScale));
        float mountainHeight = peakValue * peakValue * MountainHeight;
        
        // Only apply mountains on land areas
        if (landNoise > -0.2f)
        {
            density += mountainHeight;
        }

        // D. Trench Fractures (Inverted Valley Selection)
        float trenchLine = math.abs(noise.cnoise(new float2(worldPos.x + NoiseOffset.x, worldPos.z + NoiseOffset.y) * TrenchScale));
        if (trenchLine < 0.05f)
        {
            // Taper the trench depth based on how close to the center of the line it is
            float trenchMultiplier = 1.0f - (trenchLine / 0.05f);
            density -= TrenchDepth * trenchMultiplier;
        }

        // Hard Vertical Limits
        if (worldPos.y > MaxSkyHeight) density -= 1000.0f;
        if (worldPos.y < MaxBedrockDepth) density += 1000.0f;

        // Normalize density to roughly -1 to 1 for byte mapping (10 units = 1 normalized unit)
        float normalizedDensity = math.clamp(density / 10f, -1f, 1f);

        // Convert the noise float to a 0-255 density byte
        byte finalDensityByte = (byte)math.clamp((normalizedDensity + 1f) * 127.5f, 0, 255);
        
        // Solid check against IsoLevel
        bool isSolid = finalDensityByte > (IsoLevel * 255);
        
        // Material assignment
        ushort materialId = 0;
        if (isSolid)
        {
            if (worldPos.y < SeaLevel - 3)
            {
                materialId = 2; // Stone
            }
            else
            {
                materialId = 1; // Dirt/Grass
            }
        }
        
        // Pack data
        uint packedData = finalDensityByte;
        packedData |= (uint)(materialId << 8);
        if (isSolid)
        {
            packedData |= (1u << 24); // Flag bit 24 as Solid
        }

        VoxelData[index] = new VoxelDataElement { Value = packedData };
    }
}
