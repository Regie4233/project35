using UnityEngine;
using Unity.Sentis;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections;

public struct TerrainHeightmapData : IComponentData
{
    public NativeArray<float> Heightmap;
    public int Resolution;
    public bool IsReady;
}

public class SentisHeightmapGenerator : MonoBehaviour
{
    [Header("Sentis ONNX Model")]
    [Tooltip("Drop your pre-trained terrain heightmap .onnx model here")]
    public ModelAsset TerrainModelAsset;
    
    [Header("Heightmap Settings")]
    [Tooltip("The resolution of the generated heightmap (e.g. 512)")]
    public int HeightmapResolution = 512;
    
    [Header("Output Scale")]
    [Tooltip("Multiplier for the normalized heightmap values")]
    public float HeightScale = 64f;

    private NativeArray<float> generatedHeightmap;
    private bool isFinished = false;

    // Sentis runtime resources
    private Model runtimeModel;
    private IWorker worker;

    void Start()
    {
        StartCoroutine(GenerateTerrainRoutine());
    }

    IEnumerator GenerateTerrainRoutine()
    {
        generatedHeightmap = new NativeArray<float>(HeightmapResolution * HeightmapResolution, Allocator.Persistent);

        if (TerrainModelAsset != null)
        {
            Debug.Log("[SentisHeightmapGenerator] Loading ONNX model...");
            
            // 1. Load the Model
            runtimeModel = ModelLoader.Load(TerrainModelAsset);
            
            // 2. Create the Worker (Use GPU compute)
            worker = WorkerFactory.CreateWorker(BackendType.GPUCompute, runtimeModel);

            // 3. Prepare Inputs
            // For a diffusion/generator model, this might be a random latent noise tensor.
            // Example: [batch_size, channels, height, width]
            using var inputTensor = new TensorFloat(new TensorShape(1, 4, 64, 64)); 
            
            // Fill with random noise (standard normal distribution for diffusion)
            var inputSpan = inputTensor.ToReadOnlyArray();
            float[] noiseData = new float[inputSpan.Length];
            for (int i = 0; i < noiseData.Length; i++)
            {
                // Box-Muller transform for normal distribution
                float u1 = UnityEngine.Random.value;
                float u2 = UnityEngine.Random.value;
                noiseData[i] = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(u1, 0.0001f))) * Mathf.Cos(2f * Mathf.PI * u2);
            }
            using var inputTensorWithData = new TensorFloat(inputTensor.shape, noiseData);

            Debug.Log("[SentisHeightmapGenerator] Executing Model Inference...");
            
            // 4. Execute the Model
            worker.Execute(inputTensorWithData);
            
            // 5. Retrieve Output
            // Assuming the model outputs a single-channel heightmap [1, 1, 512, 512]
            var outputTensor = worker.PeekOutput() as TensorFloat;
            
            // 6. Copy to NativeArray for ECS to consume
            var outputSpan = outputTensor.ToReadOnlyArray();
            for(int i = 0; i < outputSpan.Length && i < generatedHeightmap.Length; i++)
            {
                generatedHeightmap[i] = outputSpan[i];
            }
            
            Debug.Log("[SentisHeightmapGenerator] Inference complete.");
        }
        else
        {
            Debug.LogWarning("[SentisHeightmapGenerator] No ONNX Model assigned! Running MOCK pipeline to generate smooth hills.");
            
            // MOCK PIPELINE: Generate a smooth, realistic-looking procedural heightmap 
            // so the rest of the systems can still function and be tested.
            for (int y = 0; y < HeightmapResolution; y++)
            {
                for (int x = 0; x < HeightmapResolution; x++)
                {
                    float nx = (float)x / HeightmapResolution * 3f;
                    float ny = (float)y / HeightmapResolution * 3f;
                    
                    // Simple smooth combination of sine waves and distance fields for a "island/hill" look
                    float e = 0.5f * Mathf.PerlinNoise(nx, ny) 
                            + 0.25f * Mathf.PerlinNoise(nx * 2, ny * 2) 
                            + 0.125f * Mathf.PerlinNoise(nx * 4, ny * 4);
                            
                    // Normalize and scale to roughly -1 to 1 range
                    float val = (e * 2f - 1f);
                    generatedHeightmap[y * HeightmapResolution + x] = val;
                }
            }
            yield return null; // simulate a frame of work
        }

        isFinished = true;

        // Register the data into ECS
        RegisterToECS();
    }

    private void RegisterToECS()
    {
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var entity = entityManager.CreateEntity();
        
        entityManager.AddComponentData(entity, new TerrainHeightmapData
        {
            Heightmap = generatedHeightmap,
            Resolution = HeightmapResolution,
            IsReady = true
        });
        
        Debug.Log($"[SentisHeightmapGenerator] Registered heightmap {HeightmapResolution}x{HeightmapResolution} to ECS.");
    }

    void OnDestroy()
    {
        worker?.Dispose();
        if (generatedHeightmap.IsCreated)
        {
            generatedHeightmap.Dispose();
        }
    }
}
