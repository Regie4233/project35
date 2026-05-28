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

        // B. Continentalness (from approx -1 to 1)
        float continentalness = noise.cnoise(new float2(warpX, warpZ) * ContinentScale);
        
        // Start with a density based on height vs SeaLevel
        float density = (SeaLevel - worldPos.y);
        
        // C. Shaping the Continents (Spline-like blending)
        if (continentalness < 0.0f)
        {
            // Ocean: deepen it based on how far out to sea we are
            density += continentalness * 20.0f;
            
            // D. Trench Fractures (Only in the deep ocean!)
            if (continentalness < -0.1f)
            {
                float trenchLine = math.abs(noise.cnoise(new float2(worldPos.x + NoiseOffset.x, worldPos.z + NoiseOffset.y) * TrenchScale));
                if (trenchLine < 0.05f)
                {
                    // Taper the trench depth based on how close to the center of the line it is
                    float trenchMultiplier = 1.0f - (trenchLine / 0.05f);
                    
                    // Taper the trench based on continentalness so it doesn't create a sudden cliff where it turns on!
                    float oceanDepthBlend = math.smoothstep(-0.1f, -0.3f, continentalness);
                    
                    density -= TrenchDepth * trenchMultiplier * oceanDepthBlend;
                }
            }
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
            float hillBlend = math.smoothstep(0.0f, 0.1f, continentalness);
            density += hillNoise * 5.0f * hillBlend;
            
            // Create a mountain mask so mountains only spawn in specific "mountain ranges"
            float mountainMask = noise.cnoise(new float2(worldPos.x + NoiseOffset.x, worldPos.z + NoiseOffset.y) * (ContinentScale * 2.0f));
            
            // Add sharp mountains only if we are significantly inland AND the mountain mask is high
            if (continentalness > 0.1f && mountainMask > 0.1f)
            {
                float peakValue = 1.0f - math.abs(noise.cnoise(new float2(worldPos.x + NoiseOffset.x, worldPos.z + NoiseOffset.y) * MountainScale));
                float rawMountainHeight = peakValue * peakValue * MountainHeight;
                
                // --- STEPPED CLIFFS (Terracing) ---
                // We divide the mountain into 6-unit high "steps" or "terraces"
                float terraceStepHeight = 6.0f;
                float steppedMountainHeight = math.round(rawMountainHeight / terraceStepHeight) * terraceStepHeight;
                
                // We blend the stepped height and raw height (0.7f means it's 70% stepped, 30% smooth)
                // This keeps the cliffs distinct but softens the sharp 90-degree corners so marching cubes renders them better
                float finalMountainHeight = math.lerp(rawMountainHeight, steppedMountainHeight, 0.7f);
                
                // Smoothly blend the mountains in based on the mask and how far inland we are
                float maskBlend = math.smoothstep(0.1f, 0.4f, mountainMask);
                float continentalBlend = math.smoothstep(0.1f, 0.3f, continentalness);
                
                density += finalMountainHeight * maskBlend * continentalBlend;
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
