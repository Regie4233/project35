using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct NoiseGenerationJob : IJobParallelFor
{
    public int3 ChunkSize;
    public float3 ChunkWorldPosition;
    public float NoiseScale; // Used to scale world coordinates to heightmap coordinates
    public float IsoLevel;
    
    // Diffusion Map Data
    [ReadOnly] public NativeArray<float> Heightmap;
    public int HeightmapResolution;
    public float HeightScale;

    public float MaxSkyHeight;
    public float MaxBedrockDepth;
    public float SeaLevel;
    public float ContinentScale;

    public NativeArray<VoxelDataElement> VoxelData;

    public void Execute(int index)
    {
        // Use padded dimensions for indexing
        int3 pSize = ChunkSize + 5;
        
        // Unflatten the 1D index back into 3D local coordinates
        int x = index % pSize.x;
        int y = (index / pSize.x) % pSize.y;
        int z = index / (pSize.x * pSize.y);

        // Offset the 3D local coordinates by -2 to center the padding
        float3 worldPos = ChunkWorldPosition + new float3(x - 2, y - 2, z - 2);
        
        // 1. Map world position to Heightmap UV (0 to 1)
        // We use NoiseScale to control how much of the world the heightmap covers
        float u = (worldPos.x * NoiseScale);
        float v = (worldPos.z * NoiseScale);

        // Map UV to pixel coordinates
        float px = (u * HeightmapResolution);
        float py = (v * HeightmapResolution);

        // Clamp to avoid out of bounds
        px = math.clamp(px, 0, HeightmapResolution - 1.001f);
        py = math.clamp(py, 0, HeightmapResolution - 1.001f);

        int x0 = (int)math.floor(px);
        int x1 = x0 + 1;
        int y0 = (int)math.floor(py);
        int y1 = y0 + 1;

        float tx = px - x0;
        float ty = py - y0;

        // 2. Bilinear Interpolation from the 1D NativeArray
        float h00 = Heightmap[y0 * HeightmapResolution + x0];
        float h10 = Heightmap[y0 * HeightmapResolution + x1];
        float h01 = Heightmap[y1 * HeightmapResolution + x0];
        float h11 = Heightmap[y1 * HeightmapResolution + x1];

        float h0 = math.lerp(h00, h10, tx);
        float h1 = math.lerp(h01, h11, tx);
        float heightmapValue = math.lerp(h0, h1, ty);

        // 3. Convert heightmap value (-1 to 1) to world height (This is the AI Continent Terrain)
        float continentTerrainHeight = SeaLevel + (heightmapValue * HeightScale);
        
        // Fix for flat plains: Add procedural rolling hills ONLY to the dark/low areas of the heightmap
        // This calculates a wide, 20-block tall hill pattern
        float plainHills = noise.snoise(new float2(worldPos.x * 0.005f, worldPos.z * 0.005f)) * 20.0f;
        
        // Calculate a blend factor: 1.0 in the darkest areas (heightmapValue near -1), 0.0 in the mountains (heightmapValue > 0)
        float plainBlend = math.clamp(1.0f - (heightmapValue + 1.0f), 0f, 1f); 
        
        // Add the hills to the plains
        continentTerrainHeight += (plainHills * plainBlend);
        
        // 4. Calculate Continent Mask (Ocean vs Land)
        // Instead of random noise, we use the actual AI heightmap to decide what is ocean!
        // The darkest areas of the map (valleys/lowlands) will naturally become oceans.
        
        // If heightmapValue is below -0.6, it becomes deep ocean. 
        // If it's above -0.4, it's solid land. We smoothstep between them for natural beaches.
        float blendFactor = math.smoothstep(-0.6f, -0.4f, heightmapValue);
        
        float oceanFloor = SeaLevel - 30f; // Deep ocean trench
        
        float targetTerrainHeight = math.lerp(oceanFloor, continentTerrainHeight, blendFactor);

        // Add procedural high-frequency detail noise (Fractional Brownian Motion - fBm)
        // This layers multiple octaves of noise for a natural, fractal-like rocky/bumpy texture
        float amplitude = 1.0f;
        float frequency = 0.015f; // Base frequency for rolling hills
        float detailNoise = 0f;
        
        for (int i = 0; i < 4; i++) // 4 Octaves
        {
            detailNoise += noise.snoise(new float2(worldPos.x * frequency, worldPos.z * frequency)) * amplitude;
            
            amplitude *= 0.4f; // Persistence: how much smaller the next bumps are
            frequency *= 2.0f; // Lacunarity: how much more frequent the next bumps are
        }
        
        // Apply a global scale to the noise (e.g. 5 blocks tall overall)
        // We also lower the terrain slightly based on the noise to create erosion-like valleys
        targetTerrainHeight += (detailNoise * 5.0f);

        // 4. Calculate Density for Marching Cubes
        // Positive density = solid, Negative = air
        float density = targetTerrainHeight - worldPos.y;

        // Hard Vertical Limits
        if (worldPos.y > MaxSkyHeight) density -= 1000.0f;
        if (worldPos.y < MaxBedrockDepth) density += 1000.0f;

        // Normalize density to roughly -1 to 1 for byte mapping (10 units = 1 normalized unit)
        // This ensures the transition is smooth across voxels, preventing blocky terrain
        float normalizedDensity = math.clamp(density / 10f, -1f, 1f);

        // Convert the noise float to a 0-255 density byte
        byte finalDensityByte = (byte)math.clamp((normalizedDensity + 1f) * 127.5f, 0, 255);
        
        // Solid check against IsoLevel
        bool isSolid = finalDensityByte > (IsoLevel * 255);
        
        uint packedData = finalDensityByte;
        if (isSolid) packedData |= (1u << 24);

        VoxelData[index] = new VoxelDataElement { Value = packedData };
    }
}

