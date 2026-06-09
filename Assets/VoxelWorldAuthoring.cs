using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// 1. The Component Data (Holds the unmanaged data in the ECS world)
public struct VoxelWorldSettings : IComponentData
{
    public int3 ChunkSize;
    public int3 GridSize;
    public float NoiseScale;
    public float IsoLevel;
    public float2 NoiseOffset;
    
    public int RenderDistance;
    public int ChunksPerFrame;
    
    public float MountainScale;
    
    public float MaxSkyHeight;
    public float MaxBedrockDepth;
    public float SeaLevel;
    public float SnowLevel;
}

// 2. A managed component to safely hold the Unity Material reference
public class VoxelMaterialComponent : IComponentData
{
    public Material Material;
}

// 3. The Authoring MonoBehaviour (Attached to a GameObject in the Editor)
// Dummy comment to force bake
public class VoxelWorldAuthoring : MonoBehaviour
{
   
    public Vector3Int ChunkSize = new Vector3Int(16, 16, 16);
    public Vector3Int GridSize = new Vector3Int(5, 4, 5); // Used for Y-height bounding, X/Z are dynamic
    
    [Header("Dynamic Chunk Loading")]
    [Tooltip("Radius of chunks to load around the player")]
    public int RenderDistance = 8;
    [Tooltip("Max chunks to spawn per frame to prevent lag")]
    public int ChunksPerFrame = 4;
    
    [Header("Base Settings")]
    public float NoiseScale = 0.05f;
    public float IsoLevel = 0.5f;
    
    [Header("Mountain Scaling")]
    [Tooltip("Multiplier for land height from the AI model (does not affect ocean depth)")]
    public float MountainScale = 0.03f;
    
    [Header("World Limits")]
    public float SeaLevel = 25f; // Positioned comfortably within the 64-unit vertical space
    public float SnowLevel = 50f;
    public float MaxSkyHeight = 60f;
    public float MaxBedrockDepth = 2f;
   
    public Material ChunkMaterial;

    // 4. The Baker (Runs automatically to convert the MonoBehaviour into ECS components)
    class Baker : Baker<VoxelWorldAuthoring>
    {
        public override void Bake(VoxelWorldAuthoring authoring)
        {
            // Get the primary entity associated with this GameObject
            var entity = GetEntity(TransformUsageFlags.None);

            // Add our Burst-compatible unmanaged settings
            AddComponent(entity, new VoxelWorldSettings
            {
                ChunkSize = new int3(authoring.ChunkSize.x, authoring.ChunkSize.y, authoring.ChunkSize.z),
                GridSize = new int3(authoring.GridSize.x, authoring.GridSize.y, authoring.GridSize.z),
                NoiseScale = authoring.NoiseScale,
                IsoLevel = authoring.IsoLevel,
                RenderDistance = authoring.RenderDistance,
                ChunksPerFrame = authoring.ChunksPerFrame,
                MountainScale = authoring.MountainScale,
                MaxSkyHeight = authoring.MaxSkyHeight,
                MaxBedrockDepth = authoring.MaxBedrockDepth,
                SeaLevel = authoring.SeaLevel,
                SnowLevel = authoring.SnowLevel
            });

            // Add the managed material component so our chunk generation system can access it
            AddComponentObject(entity, new VoxelMaterialComponent
            {
                Material = authoring.ChunkMaterial
            });
        }
    }
}