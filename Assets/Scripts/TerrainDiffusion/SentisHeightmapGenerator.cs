using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using System.IO;
using System.Collections;

public struct TerrainHeightmapData : IComponentData
{
    public NativeArray<float> Heightmap;
    public int Resolution;
    public float HeightScale;
    public float2 MapOffset;
    public bool IsReady;
}

public class SentisHeightmapGenerator : MonoBehaviour
{
    [Header("Pre-generated Map Loader")]
    [Tooltip("The name of the .raw file inside the StreamingAssets folder")]
    public string RawFileName = "world_map.raw";
    
    [Header("Output Scale")]
    [Tooltip("Divisor for the heightmap values, to match the ChunkGenerationSystem scale")]
    public float HeightScale = 64f;

    [Tooltip("Offset to pan around the map. Values from 0 to 1 represent the entire map.")]
    public Vector2 MapOffset = Vector2.zero;

    private NativeArray<float> generatedHeightmap;

    void Start()
    {
        StartCoroutine(LoadTerrainRoutine());
    }

    IEnumerator LoadTerrainRoutine()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, RawFileName);
        int calculatedResolution = 0;
        
        if (File.Exists(filePath))
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            
            // Dynamically calculate the resolution based on the file size to prevent striping/aliasing
            int numFloats = fileData.Length / 4;
            calculatedResolution = (int)math.sqrt(numFloats);
            
            if (calculatedResolution * calculatedResolution != numFloats)
            {
                Debug.LogError($"[StaticHeightmapLoader] The .raw file at {filePath} does not contain a perfect square of floats. Are you sure it's a valid heightmap?");
                yield break;
            }

            Debug.Log($"[StaticHeightmapLoader] Detected map resolution: {calculatedResolution}x{calculatedResolution} from file size.");
            
            generatedHeightmap = new NativeArray<float>(numFloats, Allocator.Persistent);
            
            // First pass: find min and max elevation
            float minElev = float.MaxValue;
            float maxElev = float.MinValue;
            for (int i = 0; i < generatedHeightmap.Length; i++)
            {
                float elevation = System.BitConverter.ToSingle(fileData, i * 4);
                if (elevation < minElev) minElev = elevation;
                if (elevation > maxElev) maxElev = elevation;
            }
            
            Debug.Log($"[StaticHeightmapLoader] Raw elevation ranges from {minElev}m to {maxElev}m. Using absolute elevation values...");

            // We load the flat float array directly
            for (int i = 0; i < generatedHeightmap.Length; i++)
            {
                float elevation = System.BitConverter.ToSingle(fileData, i * 4);
                generatedHeightmap[i] = elevation;
            }
            Debug.Log($"[StaticHeightmapLoader] Loaded {calculatedResolution}x{calculatedResolution} map successfully.");
        }
        else
        {
            Debug.LogWarning($"[StaticHeightmapLoader] Map file not found at {filePath}. Running MOCK pipeline to generate smooth hills. Have you run the Python script yet?");
            calculatedResolution = 256;
            generatedHeightmap = new NativeArray<float>(calculatedResolution * calculatedResolution, Allocator.Persistent);
            FillMockData(calculatedResolution);
        }

        RegisterToECS(calculatedResolution);
        yield return null;
    }

    private void FillMockData(int resolution)
    {
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float u = x / (float)resolution;
                float v = y / (float)resolution;
                // Generate a simple hill shape with absolute real-world elevation scaling
                float height = math.sin(u * math.PI) * math.sin(v * math.PI) * 1000f; // up to 1000m hills
                generatedHeightmap[y * resolution + x] = height;
            }
        }
    }

    private void RegisterToECS(int resolution)
    {
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var entity = entityManager.CreateEntity();
        
        entityManager.AddComponentData(entity, new TerrainHeightmapData
        {
            Heightmap = generatedHeightmap,
            Resolution = resolution,
            HeightScale = HeightScale,
            MapOffset = MapOffset,
            IsReady = true
        });
        
        Debug.Log($"[StaticHeightmapLoader] Registered heightmap {resolution}x{resolution} to ECS.");
    }

    void OnDestroy()
    {
        if (generatedHeightmap.IsCreated)
        {
            generatedHeightmap.Dispose();
        }
    }
}
