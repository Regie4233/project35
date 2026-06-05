using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
public partial class ChunkGenerationSystem : SystemBase
{
    private NativeArray<int> edgeTable;
    private NativeArray<int> triTable;
    private NativeHashSet<BlobAssetReference<Unity.Physics.Collider>> activeColliders;
    private UnityEngine.Material fallbackMaterial;

    protected override void OnCreate()
    {
        edgeTable = new NativeArray<int>(MarchingCubesTables.edgeTable, Allocator.Persistent);
        triTable = new NativeArray<int>(MarchingCubesTables.triTable, Allocator.Persistent);
        activeColliders = new NativeHashSet<BlobAssetReference<Unity.Physics.Collider>>(256, Allocator.Persistent);
        RequireForUpdate<VoxelWorldSettings>();
    }

    protected override void OnDestroy()
    {
        if (edgeTable.IsCreated) edgeTable.Dispose();
        if (triTable.IsCreated) triTable.Dispose();
        if (fallbackMaterial != null) Object.DestroyImmediate(fallbackMaterial);

        if (activeColliders.IsCreated)
        {
            foreach (var collider in activeColliders)
            {
                if (collider.IsCreated) collider.Dispose();
            }
            activeColliders.Dispose();
        }
    }

    protected override void OnUpdate()
    {
        if (!SystemAPI.TryGetSingleton<TerrainHeightmapData>(out var heightmapData) || !heightmapData.IsReady)
        {
            return;
        }

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // 1. Dynamic Chunk Loading / Spawning
        float3 viewerPos = float3.zero;
        if (Camera.main != null) viewerPos = Camera.main.transform.position;

        foreach (var (settings, authoringEntity) in SystemAPI.Query<RefRO<VoxelWorldSettings>>().WithNone<ChunkCoordinate>().WithEntityAccess())
        {
            int3 viewerCoord = new int3(
                (int)math.floor(viewerPos.x / settings.ValueRO.ChunkSize.x),
                0, 
                (int)math.floor(viewerPos.z / settings.ValueRO.ChunkSize.z)
            );

            int renderDistance = settings.ValueRO.RenderDistance;
            int chunksPerFrame = settings.ValueRO.ChunksPerFrame;

            var loadedChunks = new NativeHashSet<int3>(1024, Allocator.Temp);
            int chunksSpawnedThisFrame = 0;

            // Despawn loop
            foreach (var (chunkCoord, chunkEntity) in SystemAPI.Query<RefRO<ChunkCoordinate>>().WithEntityAccess())
            {
                // We don't despawn the authoring entity because it has no ChunkCoordinate. 
                // Any entity with ChunkCoordinate is an active chunk.
                
                if (math.abs(chunkCoord.ValueRO.Value.x - viewerCoord.x) > renderDistance || 
                    math.abs(chunkCoord.ValueRO.Value.z - viewerCoord.z) > renderDistance)
                {
                    if (EntityManager.HasComponent<PhysicsCollider>(chunkEntity))
                    {
                        var collider = EntityManager.GetComponentData<PhysicsCollider>(chunkEntity);
                        if (collider.Value.IsCreated)
                        {
                            activeColliders.Remove(collider.Value);
                            collider.Value.Dispose();
                        }
                    }
                    
                    if (EntityManager.HasComponent<ChunkManagedData>(chunkEntity))
                    {
                        var managedData = EntityManager.GetComponentObject<ChunkManagedData>(chunkEntity);
                        if (managedData.Mesh != null) UnityEngine.Object.Destroy(managedData.Mesh);
                    }

                    ecb.DestroyEntity(chunkEntity);
                }
                else
                {
                    loadedChunks.Add(chunkCoord.ValueRO.Value);
                }
            }

            // Spawn missing chunks within render distance
            for (int x = -renderDistance; x <= renderDistance; x++)
            {
                for (int z = -renderDistance; z <= renderDistance; z++)
                {
                    for (int y = 0; y < settings.ValueRO.GridSize.y; y++)
                    {
                        int3 targetCoord = new int3(viewerCoord.x + x, y, viewerCoord.z + z);

                        if (!loadedChunks.Contains(targetCoord) && chunksSpawnedThisFrame < chunksPerFrame)
                        {
                            var chunkEntity = ecb.Instantiate(authoringEntity);
                            ecb.AddComponent(chunkEntity, new ChunkCoordinate { Value = targetCoord });
                            ecb.AddComponent<ChunkNeedsNoiseTag>(chunkEntity);
                            
                            float3 pos = new float3(targetCoord.x * settings.ValueRO.ChunkSize.x, targetCoord.y * settings.ValueRO.ChunkSize.y, targetCoord.z * settings.ValueRO.ChunkSize.z);
                            ecb.AddComponent(chunkEntity, LocalTransform.FromPosition(pos));
                            ecb.AddComponent<LocalToWorld>(chunkEntity);

                            var buffer = ecb.AddBuffer<VoxelDataElement>(chunkEntity);
                            int3 pSize = settings.ValueRO.ChunkSize + 5;
                            buffer.ResizeUninitialized(pSize.x * pSize.y * pSize.z);

                            chunksSpawnedThisFrame++;
                            loadedChunks.Add(targetCoord);
                        }
                    }
                }
            }
            
            loadedChunks.Dispose();
        }

        // 2. Noise Generation
        foreach (var (settings, coord, voxelBuffer, entity) in SystemAPI.Query<RefRO<VoxelWorldSettings>, RefRO<ChunkCoordinate>, DynamicBuffer<VoxelDataElement>>().WithAll<ChunkNeedsNoiseTag>().WithEntityAccess())
        {
            var noiseJob = new NoiseGenerationJob
            {
                ChunkSize = settings.ValueRO.ChunkSize,
                ChunkWorldPosition = new float3(coord.ValueRO.Value.x * settings.ValueRO.ChunkSize.x, coord.ValueRO.Value.y * settings.ValueRO.ChunkSize.y, coord.ValueRO.Value.z * settings.ValueRO.ChunkSize.z),
                NoiseScale = settings.ValueRO.NoiseScale,
                IsoLevel = settings.ValueRO.IsoLevel,
                MaxSkyHeight = settings.ValueRO.MaxSkyHeight,
                MaxBedrockDepth = settings.ValueRO.MaxBedrockDepth,
                SeaLevel = settings.ValueRO.SeaLevel,
                ContinentScale = settings.ValueRO.ContinentScale,
                Heightmap = heightmapData.Heightmap,
                HeightmapResolution = heightmapData.Resolution,
                HeightScale = heightmapData.HeightScale, // Read from the SentisHeightmapGenerator
                MapOffset = heightmapData.MapOffset,
                VoxelData = voxelBuffer.AsNativeArray()
            };

            int3 pSize = settings.ValueRO.ChunkSize + 5;
            noiseJob.Schedule(pSize.x * pSize.y * pSize.z, 64).Complete();

            ecb.RemoveComponent<ChunkNeedsNoiseTag>(entity);
            ecb.AddComponent<ChunkNeedsMeshingTag>(entity);
        }

        // 3. Meshing
        var vertices = new NativeList<float3>(Allocator.Temp);
        var indices = new NativeList<ushort>(Allocator.Temp);
        var normals = new NativeList<float3>(Allocator.Temp);
        var uvs = new NativeList<float2>(Allocator.Temp);
        var colors = new NativeList<float4>(Allocator.Temp);

        // We collect chunks that need setup to avoid structural changes while querying
        var chunksToInitialize = new System.Collections.Generic.List<(Entity entity, Mesh mesh, UnityEngine.Material mat, BlobAssetReference<Unity.Physics.Collider> collider)>();

        foreach (var (settings, coord, voxelBuffer, entity) in SystemAPI.Query<RefRO<VoxelWorldSettings>, RefRO<ChunkCoordinate>, DynamicBuffer<VoxelDataElement>>().WithAll<ChunkNeedsMeshingTag>().WithEntityAccess())
        {
            vertices.Clear();
            indices.Clear();
            normals.Clear();
            uvs.Clear();
            colors.Clear();

            var meshingJob = new MarchingCubesJob
            {
                VoxelData = voxelBuffer.AsNativeArray(),
                ChunkSize = settings.ValueRO.ChunkSize,
                ChunkWorldPosition = new float3(coord.ValueRO.Value.x * settings.ValueRO.ChunkSize.x, coord.ValueRO.Value.y * settings.ValueRO.ChunkSize.y, coord.ValueRO.Value.z * settings.ValueRO.ChunkSize.z),
                IsoLevel = settings.ValueRO.IsoLevel,
                SeaLevel = settings.ValueRO.SeaLevel,
                SnowLevel = settings.ValueRO.SnowLevel,
                Vertices = vertices,
                Indices = indices,
                Normals = normals,
                UVs = uvs,
                Colors = colors,
                EdgeTable = edgeTable,
                TriTable = triTable
            };

            meshingJob.Execute();

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
                        if (shader != null) fallbackMaterial = new UnityEngine.Material(shader);
                    }
                    mat = fallbackMaterial;
                }

                int numTriangles = indices.Length / 3;
                var triangleIndices = new NativeArray<int3>(numTriangles * 2, Allocator.Temp);
                for (int t = 0; t < indices.Length; t += 3) 
                {
                    triangleIndices[t / 3] = new int3(indices[t], indices[t + 1], indices[t + 2]);
                    triangleIndices[numTriangles + (t / 3)] = new int3(indices[t], indices[t + 2], indices[t + 1]);
                }
                
                var meshCollider = Unity.Physics.MeshCollider.Create(
                    vertices.AsArray(), 
                    triangleIndices, 
                    CollisionFilter.Default, 
                    Unity.Physics.Material.Default
                );
                triangleIndices.Dispose();

                activeColliders.Add(meshCollider);
                chunksToInitialize.Add((entity, mesh, mat, meshCollider));
                if (math.distance(math.transform(EntityManager.GetComponentData<LocalToWorld>(entity).Value, mesh.bounds.center), new float3(80, 8, 80)) < 25f) {
                    Debug.Log($"[ChunkGenerationSystem] Chunk at {EntityManager.GetComponentData<LocalTransform>(entity).Position} generated {vertices.Length} vertices and {indices.Length/3} triangles.");
                }
            }

            ecb.RemoveComponent<ChunkNeedsMeshingTag>(entity);
            ecb.AddComponent<ChunkReadyTag>(entity);
        }

        // Apply structural changes outside the query loop
        foreach (var item in chunksToInitialize)
        {
            ChunkRendererSetup.InitializeChunkRendering(EntityManager, item.entity, item.mesh, item.mat);
            
            if (!EntityManager.HasComponent<ChunkManagedData>(item.entity))
            {
                EntityManager.AddComponentObject(item.entity, new ChunkManagedData { Mesh = item.mesh });
            }
            else
            {
                var managed = EntityManager.GetComponentObject<ChunkManagedData>(item.entity);
                managed.Mesh = item.mesh;
            }
            
            if (EntityManager.HasComponent<PhysicsCollider>(item.entity))
            {
                var oldCollider = EntityManager.GetComponentData<PhysicsCollider>(item.entity);
                if (oldCollider.Value.IsCreated)
                {
                    activeColliders.Remove(oldCollider.Value);
                    oldCollider.Value.Dispose();
                }
                EntityManager.SetComponentData(item.entity, new PhysicsCollider { Value = item.collider });
            }
            else
            {
                EntityManager.AddComponentData(item.entity, new PhysicsCollider { Value = item.collider });
            }

            if (!EntityManager.HasComponent<PhysicsWorldIndex>(item.entity))
            {
                EntityManager.AddSharedComponent(item.entity, new PhysicsWorldIndex { Value = 0 });
            }
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
        
        vertices.Dispose();
        indices.Dispose();
        normals.Dispose();
        uvs.Dispose();
        colors.Dispose();
    }
}

