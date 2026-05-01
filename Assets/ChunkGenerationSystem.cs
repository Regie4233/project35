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

    protected override void OnCreate()
    {
        uninitializedGridQuery = GetEntityQuery(ComponentType.ReadOnly<VoxelWorldSettings>(), ComponentType.Exclude<ChunkCoordinate>());
        noiseQuery = GetEntityQuery(ComponentType.ReadOnly<VoxelWorldSettings>(), ComponentType.ReadOnly<ChunkNeedsNoiseTag>(), ComponentType.ReadWrite<VoxelDataElement>());
        meshingQuery = GetEntityQuery(ComponentType.ReadOnly<VoxelWorldSettings>(), ComponentType.ReadOnly<ChunkNeedsMeshingTag>(), ComponentType.ReadOnly<VoxelDataElement>());
        collisionQuery = GetEntityQuery(ComponentType.ReadOnly<VoxelWorldSettings>(), ComponentType.ReadOnly<ChunkNeedsCollisionTag>());

        edgeTable = new NativeArray<int>(MarchingCubesTables.edgeTable, Allocator.Persistent);
        triTable = new NativeArray<int>(MarchingCubesTables.triTable, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (edgeTable.IsCreated) edgeTable.Dispose();
        if (triTable.IsCreated) triTable.Dispose();
    }

    protected override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Allocator.TempJob);

        // 1. Initialize Grid
        if (!uninitializedGridQuery.IsEmptyIgnoreFilter)
        {
            var entities = uninitializedGridQuery.ToEntityArray(Allocator.Temp);
            var settings = uninitializedGridQuery.ToComponentDataArray<VoxelWorldSettings>(Allocator.Temp);

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
                            ecb.AddComponent(chunkEntity, new ChunkCoordinate { Value = new int3(x, y, z) });
                            ecb.AddComponent<ChunkNeedsNoiseTag>(chunkEntity);
                            ecb.AddComponentObject(chunkEntity, new VoxelMaterialComponent { Material = authoringMaterial.Material });
                            // Apply translation so chunks don't overlap
                            ecb.AddComponent(chunkEntity, new Unity.Transforms.LocalTransform {
                                Position = new float3(x * authoringSettings.ChunkSize.x, y * authoringSettings.ChunkSize.y, z * authoringSettings.ChunkSize.z),
                                Rotation = quaternion.identity,
                                Scale = 1f
                            });

                            var buffer = ecb.AddBuffer<VoxelDataElement>(chunkEntity);
                            buffer.ResizeUninitialized(authoringSettings.ChunkSize.x * authoringSettings.ChunkSize.y * authoringSettings.ChunkSize.z);
                        }
                    }
                }

                // Destroy the authoring entity so we don't process it again
                ecb.DestroyEntity(authoringEntity);
            }
            entities.Dispose();
            settings.Dispose();
        }

        // 2. Generate Noise
        if (!noiseQuery.IsEmptyIgnoreFilter)
        {
            var entities = noiseQuery.ToEntityArray(Allocator.Temp);
            var settings = noiseQuery.ToComponentDataArray<VoxelWorldSettings>(Allocator.Temp);
            var coordinates = noiseQuery.ToComponentDataArray<ChunkCoordinate>(Allocator.Temp);

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
                    VoxelData = voxelArray
                };

                noiseJob.Schedule(voxelArray.Length, 64).Complete(); // Wait for completion for simplicity

                ecb.RemoveComponent<ChunkNeedsNoiseTag>(entity);
                ecb.AddComponent<ChunkNeedsMeshingTag>(entity);
            }
            entities.Dispose();
            settings.Dispose();
            coordinates.Dispose();
        }

        // 3. Generate Mesh
        if (!meshingQuery.IsEmptyIgnoreFilter)
        {
            var entities = meshingQuery.ToEntityArray(Allocator.Temp);
            var settings = meshingQuery.ToComponentDataArray<VoxelWorldSettings>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var chunkSettings = settings[i];

                var voxelBuffer = EntityManager.GetBuffer<VoxelDataElement>(entity);

                var vertices = new NativeList<float3>(Allocator.TempJob);
                var indices = new NativeList<ushort>(Allocator.TempJob);
                var normals = new NativeList<float3>(Allocator.TempJob);
                var uvs = new NativeList<float2>(Allocator.TempJob);

                var meshingJob = new MarchingCubesJob
                {
                    VoxelData = voxelBuffer.AsNativeArray(),
                    ChunkSize = chunkSettings.ChunkSize,
                    IsoLevel = chunkSettings.IsoLevel,
                    Vertices = vertices,
                    Indices = indices,
                    Normals = normals,
                    UVs = uvs,
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

                    var materialComp = EntityManager.GetComponentObject<VoxelMaterialComponent>(entity);
                    ChunkRendererSetup.InitializeChunkRendering(EntityManager, entity, mesh, materialComp.Material);

                    // Simple physics
                    NativeArray<int3> triangleIndices = new NativeArray<int3>(indices.Length / 3, Allocator.Temp);
                    for (int t = 0; t < indices.Length; t += 3)
                    {
                        triangleIndices[t / 3] = new int3(indices[t], indices[t + 1], indices[t + 2]);
                    }
                    var meshCollider = Unity.Physics.MeshCollider.Create(vertices.AsArray(), triangleIndices, CollisionFilter.Default);
                    ecb.AddComponent(entity, new PhysicsCollider { Value = meshCollider });
                    triangleIndices.Dispose();
                }

                vertices.Dispose();
                indices.Dispose();
                normals.Dispose();
                uvs.Dispose();

                ecb.RemoveComponent<ChunkNeedsMeshingTag>(entity);
                ecb.AddComponent<ChunkReadyTag>(entity);
            }
            entities.Dispose();
            settings.Dispose();
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}
