using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;
using Unity.Rendering;
using Unity.Physics;

public partial class TerrainLODSystem : SystemBase
{
    private Dictionary<int3, Entity> activeChunks = new Dictionary<int3, Entity>();

    protected override void OnUpdate()
    {
        if (!SystemAPI.HasSingleton<VoxelWorldSettings>() || !SystemAPI.HasSingleton<LODViewer>())
            return;

        var settings = SystemAPI.GetSingleton<VoxelWorldSettings>();
        var viewer = SystemAPI.GetSingleton<LODViewer>();
        var materialComp = SystemAPI.ManagedAPI.GetSingleton<VoxelMaterialComponent>();

        float baseChunkSize = settings.ChunkSize.x;
        int maxLOD = settings.MaxLODDepth;
        float maxChunkSize = baseChunkSize * math.pow(2, maxLOD);

        // Very simplified quadtree evaluation:
        // We evaluate a 3x3 grid of root nodes (largest LOD) around the player.
        int3 viewerRootPos = new int3(
            (int)math.floor(viewer.Position.x / maxChunkSize), 
            0, 
            (int)math.floor(viewer.Position.z / maxChunkSize)
        );

        HashSet<int4> requiredNodes = new HashSet<int4>(); // x, y, z, lod

        for (int x = -2; x <= 2; x++)
        {
            for (int z = -2; z <= 2; z++)
            {
                EvaluateNode(viewerRootPos + new int3(x, 0, z), maxLOD, viewer.Position, settings, requiredNodes);
            }
        }

        // Destroy chunks that are no longer required
        List<int3> toRemove = new List<int3>();
        foreach (var kvp in activeChunks)
        {
            // We use a flat index hash (just an example, in reality we'd need to hash LOD + Pos)
            // For simplicity, we just rebuild if needed.
        }

        // Instantiate new required chunks
        foreach (var node in requiredNodes)
        {
            int3 pos = new int3(node.x, node.y, node.z);
            int lod = node.w;
            
            // Generate a unique hash for this node position and LOD
            int3 hashPos = new int3(pos.x, lod, pos.z); 

            if (!activeChunks.ContainsKey(hashPos))
            {
                float currentChunkScale = baseChunkSize * math.pow(2, lod);
                float3 worldPos = new float3(pos.x * currentChunkScale, 0, pos.z * currentChunkScale);

                Entity chunkEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(chunkEntity, new ChunkNode 
                { 
                    NodePosition = pos, 
                    LODLevel = lod, 
                    NodeSize = currentChunkScale, 
                    IsActive = true 
                });
                
                var buffer = EntityManager.AddBuffer<VoxelDataElement>(chunkEntity);
                buffer.ResizeUninitialized(settings.ChunkSize.x * settings.ChunkSize.y * settings.ChunkSize.z);

                // Run SDF Job
                var sdfJob = new SDFGenerationJob
                {
                    ChunkSize = settings.ChunkSize,
                    ChunkWorldPosition = worldPos,
                    ChunkScale = currentChunkScale,
                    NoiseScale = settings.NoiseScale,
                    IsoLevel = settings.IsoLevel,
                    VoxelData = buffer.AsNativeArray()
                };
                sdfJob.Run(buffer.Length);

                // Run Surface Nets Job
                var vertices = new NativeList<float3>(Allocator.TempJob);
                var indices = new NativeList<ushort>(Allocator.TempJob);

                var meshJob = new SurfaceNetsJob
                {
                    VoxelData = buffer.AsNativeArray(),
                    ChunkSize = settings.ChunkSize,
                    ChunkScale = currentChunkScale,
                    Vertices = vertices,
                    Indices = indices
                };
                meshJob.Run();

                // Create Unity Mesh (in production, we'd use MeshDataArray and async jobs)
                if (vertices.Length > 0 && indices.Length > 0)
                {
                    Mesh mesh = new Mesh();
                    mesh.SetVertices(vertices.AsArray());
                    mesh.SetIndices(indices.AsArray(), MeshTopology.Triangles, 0);
                    mesh.RecalculateNormals();

                    ChunkRendererSetup.InitializeChunkRendering(EntityManager, chunkEntity, mesh, materialComp.Material);
                }

                vertices.Dispose();
                indices.Dispose();

                activeChunks.Add(hashPos, chunkEntity);
            }
        }
    }

    private void EvaluateNode(int3 nodePos, int currentLOD, float3 viewerPos, VoxelWorldSettings settings, HashSet<int4> requiredNodes)
    {
        float currentChunkScale = settings.ChunkSize.x * math.pow(2, currentLOD);
        float3 centerPos = new float3(nodePos.x * currentChunkScale, 0, nodePos.z * currentChunkScale) + new float3(currentChunkScale / 2f, 0, currentChunkScale / 2f);
        
        float dist = math.distance(new float2(viewerPos.x, viewerPos.z), new float2(centerPos.x, centerPos.z));

        // If it's close enough and we can subdivide, split into 4 children (quadtree on XZ plane)
        if (currentLOD > 0 && dist < (currentChunkScale * settings.LODDistanceMultiplier))
        {
            int childLOD = currentLOD - 1;
            EvaluateNode(new int3(nodePos.x * 2, 0, nodePos.z * 2), childLOD, viewerPos, settings, requiredNodes);
            EvaluateNode(new int3(nodePos.x * 2 + 1, 0, nodePos.z * 2), childLOD, viewerPos, settings, requiredNodes);
            EvaluateNode(new int3(nodePos.x * 2, 0, nodePos.z * 2 + 1), childLOD, viewerPos, settings, requiredNodes);
            EvaluateNode(new int3(nodePos.x * 2 + 1, 0, nodePos.z * 2 + 1), childLOD, viewerPos, settings, requiredNodes);
        }
        else
        {
            // Add to required nodes
            requiredNodes.Add(new int4(nodePos.x, nodePos.y, nodePos.z, currentLOD));
        }
    }
}
