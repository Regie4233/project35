using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ChunkGenerationSystem : SystemBase
{
    private EntityQuery uninitializedGridQuery;
    private EntityQuery noiseQuery;
    private EntityQuery meshingQuery;
    private EntityQuery collisionQuery;

    private NativeArray<int> edgeTable;
    private NativeArray<int> triTable;
    private UnityEngine.Material fallbackMaterial;

    protected override void OnCreate()
    {
        uninitializedGridQuery = GetEntityQuery(ComponentType.ReadOnly<VoxelWorldSettings>(), ComponentType.Exclude<ChunkCoordinate>());
        noiseQuery = GetEntityQuery(ComponentType.ReadOnly<VoxelWorldSettings>(), ComponentType.ReadOnly<ChunkNeedsNoiseTag>(), ComponentType.ReadWrite<VoxelDataElement>(), ComponentType.ReadOnly<ChunkCoordinate>());
        meshingQuery = GetEntityQuery(ComponentType.ReadOnly<VoxelWorldSettings>(), ComponentType.ReadOnly<ChunkNeedsMeshingTag>(), ComponentType.ReadOnly<VoxelDataElement>());
        collisionQuery = GetEntityQuery(ComponentType.ReadOnly<VoxelWorldSettings>(), ComponentType.ReadOnly<ChunkNeedsCollisionTag>());

        edgeTable = new NativeArray<int>(MarchingCubesTables.edgeTable, Allocator.Persistent);
        triTable = new NativeArray<int>(MarchingCubesTables.triTable, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (edgeTable.IsCreated) edgeTable.Dispose();
        if (triTable.IsCreated) triTable.Dispose();

        if (fallbackMaterial != null)
        {
            UnityEngine.Object.DestroyImmediate(fallbackMaterial);
        }
    }

    protected override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Allocator.TempJob);

        try
        {
            // 1. Initialize Grid
            if (!uninitializedGridQuery.IsEmptyIgnoreFilter)
            {
                using var entities = uninitializedGridQuery.ToEntityArray(Allocator.Temp);
                using var settings = uninitializedGridQuery.ToComponentDataArray<VoxelWorldSettings>(Allocator.Temp);

                for (int i = 0; i < entities.Length; i++)
                {
                    var authoringEntity = entities[i];
                    var authoringSettings = settings[i];
                    var authoringMaterial = EntityManager.GetComponentObject<VoxelMaterialComponent>(authoringEntity);

                    for (int x = 0; x < authoringSettings.GridSize.x; x++)
                    {
                        for (int y = 0; y < authoringSettings.GridSize.y; y++)
                        {
                            for (int z = 0; z < authoringSettings.GridSize.z; z++)
                            {
                                var chunkEntity = ecb.Instantiate(authoringEntity);
                                
                                // Generate a random offset for this world generation
                                if (x == 0 && y == 0 && z == 0 && authoringSettings.NoiseOffset.Equals(float2.zero))
                                {
                                    authoringSettings.NoiseOffset = new float2(UnityEngine.Random.Range(-10000f, 10000f), UnityEngine.Random.Range(-10000f, 10000f));
                                }
                                
                                ecb.SetComponent(chunkEntity, authoringSettings);
                                ecb.AddComponent(chunkEntity, new ChunkCoordinate { Value = new int3(x, y, z) });
                                ecb.AddComponent<ChunkNeedsNoiseTag>(chunkEntity);
                                // Apply translation so chunks don't overlap
                                ecb.AddComponent(chunkEntity, new Unity.Transforms.LocalTransform
                                {
                                    Position = new float3(x * authoringSettings.ChunkSize.x, y * authoringSettings.ChunkSize.y, z * authoringSettings.ChunkSize.z),
                                    Rotation = quaternion.identity,
                                    Scale = 1f
                                });
                                ecb.AddComponent<Unity.Transforms.LocalToWorld>(chunkEntity);

                                var buffer = ecb.AddBuffer<VoxelDataElement>(chunkEntity);
                            int3 pSize = authoringSettings.ChunkSize + 1;
                            buffer.ResizeUninitialized(pSize.x * pSize.y * pSize.z);
                        }
                    }
                }

                // Destroy the authoring entity so we don't process it again
                ecb.DestroyEntity(authoringEntity);
            }
        }

        // 2. Generate Noise
        if (!noiseQuery.IsEmptyIgnoreFilter)
        {
            using var entities = noiseQuery.ToEntityArray(Allocator.Temp);
            using var settings = noiseQuery.ToComponentDataArray<VoxelWorldSettings>(Allocator.Temp);
            using var coordinates = noiseQuery.ToComponentDataArray<ChunkCoordinate>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var chunkSettings = settings[i];
                var coord = coordinates[i].Value;

                var voxelBuffer = EntityManager.GetBuffer<VoxelDataElement>(entity);
                var voxelArray = voxelBuffer.AsNativeArray();

                var noiseJob = new NoiseGenerationJob
                {
                    ChunkSize = chunkSettings.ChunkSize,
                    ChunkWorldPosition = new float3(coord.x * chunkSettings.ChunkSize.x, coord.y * chunkSettings.ChunkSize.y, coord.z * chunkSettings.ChunkSize.z),
                    NoiseScale = chunkSettings.NoiseScale,
                    IsoLevel = chunkSettings.IsoLevel,
                    NoiseOffset = chunkSettings.NoiseOffset,
                    VoxelData = voxelArray
                };

                int3 pSize = chunkSettings.ChunkSize + 1;
                noiseJob.Schedule(pSize.x * pSize.y * pSize.z, 64).Complete(); // Wait for completion for simplicity

                    ecb.RemoveComponent<ChunkNeedsNoiseTag>(entity);
                    ecb.AddComponent<ChunkNeedsMeshingTag>(entity);
                }
            }

            // 3. Generate Mesh
            if (!meshingQuery.IsEmptyIgnoreFilter)
            {
                using var entities = meshingQuery.ToEntityArray(Allocator.Temp);
                using var settings = meshingQuery.ToComponentDataArray<VoxelWorldSettings>(Allocator.Temp);

                using var vertices = new NativeList<float3>(Allocator.TempJob);
                using var indices = new NativeList<ushort>(Allocator.TempJob);
                using var normals = new NativeList<float3>(Allocator.TempJob);
                using var uvs = new NativeList<float2>(Allocator.TempJob);
                using var colors = new NativeList<float4>(Allocator.TempJob);

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var chunkSettings = settings[i];

                    var voxelBuffer = EntityManager.GetBuffer<VoxelDataElement>(entity);

                    vertices.Clear();
                    indices.Clear();
                    normals.Clear();
                    uvs.Clear();
                    colors.Clear();

                    var meshingJob = new MarchingCubesJob
                    {
                        VoxelData = voxelBuffer.AsNativeArray(),
                        ChunkSize = chunkSettings.ChunkSize,
                        IsoLevel = chunkSettings.IsoLevel,
                        Vertices = vertices,
                        Indices = indices,
                        Normals = normals,
                        UVs = uvs,
                        Colors = colors,
                        EdgeTable = edgeTable,
                        TriTable = triTable
                    };

                    meshingJob.Schedule().Complete(); // Wait for completion

                    if (vertices.Length > 0)
                    {
                        var mesh = new Mesh();
                        mesh.SetVertices(vertices.AsArray());
                        mesh.SetIndices(indices.AsArray(), MeshTopology.Triangles, 0);
                        mesh.SetNormals(normals.AsArray());
                        mesh.SetUVs(0, uvs.AsArray());
                        mesh.SetColors(colors.AsArray());
                        mesh.RecalculateBounds();

                        var materialComp = EntityManager.GetComponentObject<VoxelMaterialComponent>(entity);
                        UnityEngine.Material mat = materialComp.Material;
                        if (mat == null)
                        {
                            if (fallbackMaterial == null)
                            {
                                Shader shader = Shader.Find("Custom/VoxelVertexColor");
                                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                                if (shader != null)
                                {
                                    fallbackMaterial = new UnityEngine.Material(shader);
                                }
                            }
                            mat = fallbackMaterial;
                        }
                        ChunkRendererSetup.InitializeChunkRendering(EntityManager, entity, mesh, mat);

                        // Simple physics
                        var triangleIndices = new NativeArray<int3>(indices.Length / 3, Allocator.Temp);
                        for (int t = 0; t < indices.Length; t += 3)
                        {
                            triangleIndices[t / 3] = new int3(indices[t], indices[t + 1], indices[t + 2]);
                        }
                        var meshCollider = Unity.Physics.MeshCollider.Create(vertices.AsArray(), triangleIndices, CollisionFilter.Default);
                        
                        // IMPORTANT: Dispose of old collider if it exists to prevent leaks
                        if (EntityManager.HasComponent<PhysicsCollider>(entity))
                        {
                            var oldCollider = EntityManager.GetComponentData<PhysicsCollider>(entity);
                            oldCollider.Value.Dispose();
                        }
                        
                        ecb.AddComponent(entity, new PhysicsCollider { Value = meshCollider });
                        // We must NOT dispose it here if we want it to stay alive on the entity, 
                        // but Unity.Physics.MeshCollider.Create produces a persistent BlobAssetReference.
                        // However, if this system runs multiple times and recreates colliders, it leaks.
                        triangleIndices.Dispose();
                    }

                    ecb.RemoveComponent<ChunkNeedsMeshingTag>(entity);
                    ecb.AddComponent<ChunkReadyTag>(entity);
                }
            }

            ecb.Playback(EntityManager);
        }
        finally
        {
            ecb.Dispose();
        }
    }
}
