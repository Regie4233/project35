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
    private NativeList<BlobAssetReference<Unity.Physics.Collider>> collidersToDispose;
    private System.Collections.Generic.List<(Entity entity, Mesh mesh, UnityEngine.Material mat, BlobAssetReference<Unity.Physics.Collider> collider)> chunksToInitialize = new System.Collections.Generic.List<(Entity, Mesh, UnityEngine.Material, BlobAssetReference<Unity.Physics.Collider>)>();
    private UnityEngine.Material fallbackMaterial;

    protected override void OnCreate()
    {
        edgeTable = new NativeArray<int>(MarchingCubesTables.edgeTable, Allocator.Persistent);
        triTable = new NativeArray<int>(MarchingCubesTables.triTable, Allocator.Persistent);
        activeColliders = new NativeHashSet<BlobAssetReference<Unity.Physics.Collider>>(1024, Allocator.Persistent);
        collidersToDispose = new NativeList<BlobAssetReference<Unity.Physics.Collider>>(1024, Allocator.Persistent);
        RequireForUpdate<VoxelWorldSettings>();
    }

    protected override void OnDestroy()
    {
        if (edgeTable.IsCreated) edgeTable.Dispose();
        if (triTable.IsCreated) triTable.Dispose();
        if (fallbackMaterial != null) Object.DestroyImmediate(fallbackMaterial);

        foreach (var collider in activeColliders)
        {
            if (collider.IsCreated) collider.Dispose();
        }
        activeColliders.Dispose();
        
        foreach (var collider in collidersToDispose)
        {
            if (collider.IsCreated) collider.Dispose();
        }
        collidersToDispose.Dispose();
    }

    protected override void OnUpdate()
    {
        foreach (var collider in collidersToDispose)
        {
            if (collider.IsCreated) collider.Dispose();
        }
        collidersToDispose.Clear();

        if (!SystemAPI.TryGetSingleton<TerrainHeightmapData>(out var heightmapData) || !heightmapData.IsReady)
        {
            return;
        }

        var ecb1 = new EntityCommandBuffer(Allocator.Temp);

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

            foreach (var (chunkCoord, chunkEntity) in SystemAPI.Query<RefRO<ChunkCoordinate>>().WithEntityAccess())
            {
                if (math.abs(chunkCoord.ValueRO.Value.x - viewerCoord.x) > renderDistance || 
                    math.abs(chunkCoord.ValueRO.Value.z - viewerCoord.z) > renderDistance)
                {
                    if (EntityManager.HasComponent<PhysicsCollider>(chunkEntity))
                    {
                        var collider = EntityManager.GetComponentData<PhysicsCollider>(chunkEntity);
                        if (collider.Value.IsCreated)
                        {
                            activeColliders.Remove(collider.Value);
                            collidersToDispose.Add(collider.Value);
                        }
                    }
                    
                    if (EntityManager.HasComponent<ChunkManagedData>(chunkEntity))
                    {
                        var managedData = EntityManager.GetComponentObject<ChunkManagedData>(chunkEntity);
                        if (managedData.Mesh != null) UnityEngine.Object.Destroy(managedData.Mesh);
                    }

                    if (EntityManager.HasComponent<ChunkNoiseJobInfo>(chunkEntity))
                    {
                        var info = EntityManager.GetComponentData<ChunkNoiseJobInfo>(chunkEntity);
                        info.Handle.Complete();
                        if (info.VoxelData.IsCreated) info.VoxelData.Dispose();
                    }

                    if (EntityManager.HasComponent<ChunkMeshingJobInfo>(chunkEntity))
                    {
                        var info = EntityManager.GetComponentData<ChunkMeshingJobInfo>(chunkEntity);
                        info.Handle.Complete();
                        if (info.VoxelDataCopy.IsCreated) info.VoxelDataCopy.Dispose();
                        if (info.Vertices.IsCreated) info.Vertices.Dispose();
                        if (info.Indices.IsCreated) info.Indices.Dispose();
                        if (info.Normals.IsCreated) info.Normals.Dispose();
                        if (info.UVs.IsCreated) info.UVs.Dispose();
                        if (info.Colors.IsCreated) info.Colors.Dispose();
                    }

                    ecb1.DestroyEntity(chunkEntity);
                }
                else
                {
                    loadedChunks.Add(chunkCoord.ValueRO.Value);
                }
            }

            for (int x = -renderDistance; x <= renderDistance; x++)
            {
                for (int z = -renderDistance; z <= renderDistance; z++)
                {
                    for (int y = 0; y < settings.ValueRO.GridSize.y; y++)
                    {
                        int3 targetCoord = new int3(viewerCoord.x + x, y, viewerCoord.z + z);

                        if (!loadedChunks.Contains(targetCoord) && chunksSpawnedThisFrame < chunksPerFrame)
                        {
                            var chunkEntity = ecb1.Instantiate(authoringEntity);
                            ecb1.AddComponent(chunkEntity, new ChunkCoordinate { Value = targetCoord });
                            ecb1.AddComponent<ChunkNeedsNoiseTag>(chunkEntity);
                            
                            float3 pos = new float3(targetCoord.x * settings.ValueRO.ChunkSize.x, targetCoord.y * settings.ValueRO.ChunkSize.y, targetCoord.z * settings.ValueRO.ChunkSize.z);
                            ecb1.AddComponent(chunkEntity, LocalTransform.FromPosition(pos));
                            ecb1.AddComponent<LocalToWorld>(chunkEntity);

                            var buffer = ecb1.AddBuffer<VoxelDataElement>(chunkEntity);
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
        
        ecb1.Playback(EntityManager);
        ecb1.Dispose();

        var ecb2 = new EntityCommandBuffer(Allocator.Temp);

        // 2a. Start Noise Generation
        foreach (var (settings, coord, entity) in SystemAPI.Query<RefRO<VoxelWorldSettings>, RefRO<ChunkCoordinate>>().WithAll<ChunkNeedsNoiseTag>().WithEntityAccess())
        {
            int3 pSize = settings.ValueRO.ChunkSize + 5;
            var tempVoxelData = new NativeArray<VoxelDataElement>(pSize.x * pSize.y * pSize.z, Allocator.TempJob);

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
                HeightScale = heightmapData.HeightScale,
                MapOffset = heightmapData.MapOffset,
                VoxelData = tempVoxelData
            };

            var handle = noiseJob.Schedule(pSize.x * pSize.y * pSize.z, 64);

            ecb2.AddComponent(entity, new ChunkNoiseJobInfo { Handle = handle, VoxelData = tempVoxelData });
            ecb2.RemoveComponent<ChunkNeedsNoiseTag>(entity);
            ecb2.AddComponent<ChunkNoiseJobActiveTag>(entity);
        }

        // 2b. Complete Noise Generation
        foreach (var (info, voxelBuffer, entity) in SystemAPI.Query<RefRO<ChunkNoiseJobInfo>, DynamicBuffer<VoxelDataElement>>().WithAll<ChunkNoiseJobActiveTag>().WithEntityAccess())
        {
            if (info.ValueRO.Handle.IsCompleted)
            {
                info.ValueRO.Handle.Complete();
                voxelBuffer.CopyFrom(info.ValueRO.VoxelData);
                info.ValueRO.VoxelData.Dispose();

                ecb2.RemoveComponent<ChunkNoiseJobInfo>(entity);
                ecb2.RemoveComponent<ChunkNoiseJobActiveTag>(entity);
                ecb2.AddComponent<ChunkNeedsMeshingTag>(entity);
            }
        }
        
        ecb2.Playback(EntityManager);
        ecb2.Dispose();

        var ecb3 = new EntityCommandBuffer(Allocator.Temp);

        // 3a. Start Meshing Jobs
        foreach (var (settings, coord, voxelBuffer, entity) in SystemAPI.Query<RefRO<VoxelWorldSettings>, RefRO<ChunkCoordinate>, DynamicBuffer<VoxelDataElement>>().WithAll<ChunkNeedsMeshingTag>().WithEntityAccess())
        {
            var vertices = new NativeList<float3>(Allocator.TempJob);
            var indices = new NativeList<ushort>(Allocator.TempJob);
            var normals = new NativeList<float3>(Allocator.TempJob);
            var uvs = new NativeList<float2>(Allocator.TempJob);
            var colors = new NativeList<float4>(Allocator.TempJob);

            var voxelDataCopy = new NativeArray<VoxelDataElement>(voxelBuffer.Length, Allocator.TempJob);
            voxelDataCopy.CopyFrom(voxelBuffer.AsNativeArray());

            var meshingJob = new MarchingCubesJob
            {
                VoxelData = voxelDataCopy,
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

            var handle = meshingJob.Schedule();

            ecb3.AddComponent(entity, new ChunkMeshingJobInfo 
            { 
                Handle = handle,
                VoxelDataCopy = voxelDataCopy,
                Vertices = vertices,
                Indices = indices,
                Normals = normals,
                UVs = uvs,
                Colors = colors
            });
            
            ecb3.RemoveComponent<ChunkNeedsMeshingTag>(entity);
            ecb3.AddComponent<ChunkMeshingJobActiveTag>(entity);
        }

        // 3b. Complete Meshing Jobs
        foreach (var (info, entity) in SystemAPI.Query<RefRO<ChunkMeshingJobInfo>>().WithAll<ChunkMeshingJobActiveTag>().WithEntityAccess())
        {
            if (info.ValueRO.Handle.IsCompleted)
            {
                info.ValueRO.Handle.Complete();

                if (info.ValueRO.Vertices.Length > 0)
                {
                    var mesh = new Mesh();
                    mesh.SetVertices(info.ValueRO.Vertices.AsArray());
                    mesh.SetIndices(info.ValueRO.Indices.AsArray(), MeshTopology.Triangles, 0);
                    mesh.SetNormals(info.ValueRO.Normals.AsArray());
                    mesh.SetUVs(0, info.ValueRO.UVs.AsArray());
                    mesh.SetColors(info.ValueRO.Colors.AsArray());
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

                    int numTriangles = info.ValueRO.Indices.Length / 3;
                    var triangleIndices = new NativeArray<int3>(numTriangles, Allocator.Temp);
                    for (int t = 0; t < info.ValueRO.Indices.Length; t += 3) 
                    {
                        triangleIndices[t / 3] = new int3(info.ValueRO.Indices[t], info.ValueRO.Indices[t + 1], info.ValueRO.Indices[t + 2]);
                    }
                    
                    var meshCollider = Unity.Physics.MeshCollider.Create(
                        info.ValueRO.Vertices.AsArray(), 
                        triangleIndices, 
                        CollisionFilter.Default, 
                        Unity.Physics.Material.Default
                    );
                    triangleIndices.Dispose();

                    activeColliders.Add(meshCollider);
                    chunksToInitialize.Add((entity, mesh, mat, meshCollider));
                }

                info.ValueRO.VoxelDataCopy.Dispose();
                info.ValueRO.Vertices.Dispose();
                info.ValueRO.Indices.Dispose();
                info.ValueRO.Normals.Dispose();
                info.ValueRO.UVs.Dispose();
                info.ValueRO.Colors.Dispose();

                ecb3.RemoveComponent<ChunkMeshingJobInfo>(entity);
                ecb3.RemoveComponent<ChunkMeshingJobActiveTag>(entity);
                ecb3.AddComponent<ChunkReadyTag>(entity);
            }
        }

        ecb3.Playback(EntityManager);
        ecb3.Dispose();

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
                    collidersToDispose.Add(oldCollider.Value); // DEFERRED DISPOSAL
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
        
        chunksToInitialize.Clear();
    }
}

