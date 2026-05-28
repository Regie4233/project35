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
