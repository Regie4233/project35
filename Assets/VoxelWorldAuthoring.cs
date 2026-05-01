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
}

// 2. A managed component to safely hold the Unity Material reference
public class VoxelMaterialComponent : IComponentData
{
    public Material Material;
}

// 3. The Authoring MonoBehaviour (Attached to a GameObject in the Editor)
public class VoxelWorldAuthoring : MonoBehaviour
{
   
    public Vector3Int ChunkSize = new Vector3Int(16, 16, 16);
    public Vector3Int GridSize = new Vector3Int(3, 1, 3);
    public float NoiseScale = 0.05f;
    public float IsoLevel = 0.5f;
    
   
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
                IsoLevel = authoring.IsoLevel
            });

            // Add the managed material component so our chunk generation system can access it
            AddComponentObject(entity, new VoxelMaterialComponent
            {
                Material = authoring.ChunkMaterial
            });
        }
    }
}