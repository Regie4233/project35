using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class ChunkRendererSetup
{
    public static void InitializeChunkRendering(EntityManager entityManager, Entity chunkEntity, Mesh generatedMesh, Material chunkMaterial)
    {
        var renderMeshArray = new RenderMeshArray(
            new Material[] { chunkMaterial },
            new Mesh[] { generatedMesh }
        );

        if (entityManager.HasComponent<MaterialMeshInfo>(chunkEntity))
        {
            // The chunk already has rendering components from the pool!
            // First, destroy the old mesh to prevent a massive memory leak
            var oldArray = entityManager.GetSharedComponentManaged<RenderMeshArray>(chunkEntity);
            var oldMesh = oldArray.GetMesh(MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            if (oldMesh != null)
            {
                UnityEngine.Object.Destroy(oldMesh);
            }
            
            // Assign the new mesh and material
            entityManager.SetSharedComponentManaged(chunkEntity, renderMeshArray);
            
            // Update the bounding box for culling
            var bounds = generatedMesh.bounds;
            entityManager.SetComponentData(chunkEntity, new RenderBounds { Value = new Unity.Mathematics.AABB { Center = bounds.center, Extents = bounds.extents } });
        }
        else
        {
            var renderMeshDescription = new RenderMeshDescription
            {
                FilterSettings = RenderFilterSettings.Default,
                LightProbeUsage = LightProbeUsage.Off
            };

            RenderMeshUtility.AddComponents(
                chunkEntity,
                entityManager,
                renderMeshDescription,
                renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
            );
        }
    }
}
