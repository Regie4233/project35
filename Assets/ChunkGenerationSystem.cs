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
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // 1. Spawning the Chunk Pool
        foreach (var (settings, entity) in SystemAPI.Query<RefRW<VoxelWorldSettings>>().WithNone<ChunkCoordinate>().WithEntityAccess())
        {
            // Randomize the noise offset if not already explicitly set to something large, or just always randomize it so it's different each time.
            settings.ValueRW.NoiseOffset = new float2(UnityEngine.Random.Range(-100000f, 100000f), UnityEngine.Random.Range(-100000f, 100000f));
            
            // Mark the authoring entity so we don't spawn again
            ecb.AddComponent(entity, new ChunkCoordinate { Value = new int3(-999, -999, -999) });
            
            int renderDist = settings.ValueRO.RenderDistance;
            int chunksPerSide = (renderDist * 2) + 1;
            int poolSize = chunksPerSide * chunksPerSide * settings.ValueRO.GridSize.y;
            
            // Add a small buffer of extra chunks just to be safe during fast movement
            poolSize += settings.ValueRO.GridSize.y * 10;
            
            Debug.Log($"[ChunkGenerationSystem] Spawning chunk pool of {poolSize} entities...");
            
            for (int i = 0; i < poolSize; i++)
            {
                var chunkEntity = ecb.Instantiate(entity);
                ecb.AddComponent(chunkEntity, new ChunkCoordinate { Value = new int3(-1, -1, -1) });
                ecb.AddComponent(chunkEntity, LocalTransform.FromPosition(float3.zero));
                ecb.AddComponent<LocalToWorld>(chunkEntity);
                ecb.AddComponent<Disabled>(chunkEntity); // Start deactivated in the pool

                var buffer = ecb.AddBuffer<VoxelDataElement>(chunkEntity);
                int3 pSize = settings.ValueRO.ChunkSize + 5;
                buffer.ResizeUninitialized(pSize.x * pSize.y * pSize.z);
            }
        }

        // 2. Noise Generation
        int chunksProcessedThisFrame = 0;
        foreach (var (settings, coord, voxelBuffer, entity) in SystemAPI.Query<RefRO<VoxelWorldSettings>, RefRO<ChunkCoordinate>, DynamicBuffer<VoxelDataElement>>().WithAll<ChunkNeedsNoiseTag>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).WithEntityAccess())
        {
            if (chunksProcessedThisFrame >= 1) break; // Stagger generation to prevent lag spikes!
            chunksProcessedThisFrame++;

            var noiseJob = new NoiseGenerationJob
            {
                ChunkSize = settings.ValueRO.ChunkSize,
                ChunkWorldPosition = new float3(coord.ValueRO.Value.x * settings.ValueRO.ChunkSize.x, coord.ValueRO.Value.y * settings.ValueRO.ChunkSize.y, coord.ValueRO.Value.z * settings.ValueRO.ChunkSize.z),
                NoiseScale = settings.ValueRO.NoiseScale,
                IsoLevel = settings.ValueRO.IsoLevel,
                NoiseOffset = settings.ValueRO.NoiseOffset,
                ContinentScale = settings.ValueRO.ContinentScale,
                WarpScale = settings.ValueRO.WarpScale,
                WarpIntensity = settings.ValueRO.WarpIntensity,
                MountainScale = settings.ValueRO.MountainScale,
                MountainHeight = settings.ValueRO.MountainHeight,
                Octaves = settings.ValueRO.Octaves,
                Persistence = settings.ValueRO.Persistence,
                Lacunarity = settings.ValueRO.Lacunarity,
                TrenchScale = settings.ValueRO.TrenchScale,
                TrenchDepth = settings.ValueRO.TrenchDepth,
                TrenchWidth = settings.ValueRO.TrenchWidth,
                MaxSkyHeight = settings.ValueRO.MaxSkyHeight,
                MaxBedrockDepth = settings.ValueRO.MaxBedrockDepth,
                SeaLevel = settings.ValueRO.SeaLevel,
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

        int meshedThisFrame = 0;
        foreach (var (settings, coord, voxelBuffer, entity) in SystemAPI.Query<RefRO<VoxelWorldSettings>, RefRO<ChunkCoordinate>, DynamicBuffer<VoxelDataElement>>().WithAll<ChunkNeedsMeshingTag>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).WithEntityAccess())
        {
            if (meshedThisFrame >= 1) break; // Stagger meshing to prevent lag spikes!
            meshedThisFrame++;

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

            if (vertices.Length > 0)
            {
                var mesh = new Mesh();
                mesh.SetVertices(vertices.AsArray());
                mesh.SetIndices(indices.AsArray(), MeshTopology.Triangles, 0);
                mesh.SetNormals(normals.AsArray());
                mesh.SetUVs(0, uvs.AsArray());
                mesh.SetColors(colors.AsArray());
                mesh.RecalculateBounds();

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
            else
            {
                // The chunk is empty, but we MUST assign an empty mesh to overwrite the old mountain mesh from the pool!
                Mesh emptyMesh = new Mesh();
                chunksToInitialize.Add((entity, emptyMesh, mat, default));
            }

            ecb.RemoveComponent<ChunkNeedsMeshingTag>(entity);
            ecb.AddComponent<ChunkReadyTag>(entity);
            ecb.RemoveComponent<Disabled>(entity); // Finally, make the chunk visible!
        }

        // Apply structural changes outside the query loop
        foreach (var item in chunksToInitialize)
        {
            ChunkRendererSetup.InitializeChunkRendering(EntityManager, item.entity, item.mesh, item.mat);
            
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

