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
    public int Octaves;
    public float Persistence;
    public float Lacunarity;
    public float TrenchScale;
    public float TrenchDepth;
    public float TrenchWidth;
    public float MaxSkyHeight;
    public float MaxBedrockDepth;
    public float SeaLevel;

    public NativeArray<VoxelDataElement> VoxelData;

    public void Execute(int index)
    {
        // Use padded dimensions for indexing
        int3 pSize = ChunkSize + 5;
        
        // Unflatten the 1D index back into 3D local coordinates
        int x = index % pSize.x;
        int y = (index / pSize.x) % pSize.y;
        int z = index / (pSize.x * pSize.y);

        // Offset the 3D local coordinates by -2 to center the padding around the 0-16 chunk bounds
        float3 worldPos = ChunkWorldPosition + new float3(x - 2, y - 2, z - 2);
        
        // A. Domain Warping for Coastal Islands
        float warpX = worldPos.x + noise.cnoise(new float2(worldPos.x + NoiseOffset.x, worldPos.z) * WarpScale) * WarpIntensity;
        float warpZ = worldPos.z + noise.cnoise(new float2(worldPos.x, worldPos.z + NoiseOffset.y) * WarpScale) * WarpIntensity;

        // B. Continentalness (from approx -1 to 1)
        float continentalness = noise.cnoise(new float2(warpX, warpZ) * ContinentScale);
        
        // Start with a density based on height vs SeaLevel
        float density = (SeaLevel - worldPos.y);
        
        // C. Shaping the Continents (Spline-like blending)
        if (continentalness < 0.0f)
        {
            // Ocean: deepen it based on how far out to sea we are
            density += continentalness * 20.0f;
            

        }
        else
        {
            // Land: Smooth Beaches and Stepped Cliffs
            // By starting the smoothstep at 0.05 instead of 0.0, we force a wider, perfectly flat beach 
            // before the inland plains start to rise.
            float inlandFactor = math.smoothstep(0.05f, 0.8f, continentalness);
            density += inlandFactor * 15.0f; // Gentle rise for inland plains
            
            // E. Meso Topography (Hills & Mountains, Only on Land)
            // General rolling hills for all inland areas (Blended in smoothly so no sudden cliffs)
            float hillNoise = noise.cnoise(new float2(worldPos.x + NoiseOffset.x, worldPos.z + NoiseOffset.y) * (MountainScale * 0.5f));
            float hillBlend = math.smoothstep(0.1f, 0.4f, continentalness);
            density += hillNoise * 5.0f * hillBlend;
            
            // Create a mountain mask so mountains only spawn in specific "mountain ranges"
            float mountainMask = noise.cnoise(new float2(worldPos.x + NoiseOffset.x, worldPos.z + NoiseOffset.y) * (ContinentScale * 2.0f));
            
            // Add sharp mountains only if we are significantly inland AND the mountain mask is high
            if (continentalness > 0.3f && mountainMask > 0.1f)
            {
                float2 pos = new float2(worldPos.x + NoiseOffset.x, worldPos.z + NoiseOffset.y) * MountainScale;
                
                float total = 0f;
                float frequency = 1f;
                float amplitude = 1f;
                float maxValue = 0f;

                for (int i = 0; i < Octaves; i++)
                {
                    // noise.cnoise returns -1 to 1.
                    // We use 1.0f - math.abs to create "ridges" (sharp peaks, wide valleys)
                    float n = 1.0f - math.abs(noise.cnoise(pos * frequency));
                    n *= n; // Square it for sharper peaks
                    total += n * amplitude;
                    maxValue += amplitude;
                    
                    amplitude *= Persistence;
                    frequency *= Lacunarity;
                }

                float fbmHeight = (total / maxValue) * MountainHeight;
                
                // Smoothly blend the mountains in based on the mask and how far inland we are
                float maskBlend = math.smoothstep(0.1f, 0.4f, mountainMask);
                float continentalBlend = math.smoothstep(0.3f, 0.6f, continentalness);
                
                density += fbmHeight * maskBlend * continentalBlend;
            }
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
        
        // Pack data
        uint packedData = finalDensityByte;
        
        // We no longer assign material IDs per voxel because MarchingCubesJob 
        // applies textures mathematically using the surface Normal and Height!
        
        if (isSolid)
        {
            packedData |= (1u << 24); // Flag bit 24 as Solid
        }

        VoxelData[index] = new VoxelDataElement { Value = packedData };
    }
}
