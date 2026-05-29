using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
public partial class AbstractPhysicsSystem : SystemBase
{
    protected override void OnUpdate()
    {
        Entity settingsEntity = Entity.Null;
        foreach (var (settingsComp, coord, e) in SystemAPI.Query<RefRO<VoxelWorldSettings>, RefRO<ChunkCoordinate>>().WithEntityAccess())
        {
            if (coord.ValueRO.Value.x == -999)
            {
                settingsEntity = e;
                break;
            }
        }
        if (settingsEntity == Entity.Null) return;
        
        var settings = SystemAPI.GetComponent<VoxelWorldSettings>(settingsEntity);

        float3 playerPos = UnityEngine.Camera.main != null ? (float3)UnityEngine.Camera.main.transform.position : float3.zero;
        float renderDistance = settings.RenderDistance * settings.ChunkSize.x;

        // Create a dummy job struct just to hold the settings and use the density formula!
        var noiseSettings = new NoiseGenerationJob
        {
            ChunkSize = settings.ChunkSize,
            NoiseScale = settings.NoiseScale,
            IsoLevel = settings.IsoLevel,
            NoiseOffset = settings.NoiseOffset,
            ContinentScale = settings.ContinentScale,
            WarpScale = settings.WarpScale,
            WarpIntensity = settings.WarpIntensity,
            MountainScale = settings.MountainScale,
            MountainHeight = settings.MountainHeight,
            Octaves = settings.Octaves,
            Persistence = settings.Persistence,
            Lacunarity = settings.Lacunarity,
            TrenchScale = settings.TrenchScale,
            TrenchDepth = settings.TrenchDepth,
            TrenchWidth = settings.TrenchWidth,
            MaxSkyHeight = settings.MaxSkyHeight,
            MaxBedrockDepth = settings.MaxBedrockDepth,
            SeaLevel = settings.SeaLevel
        };

        foreach (var (transform, velocity, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<PhysicsVelocity>>().WithEntityAccess())
        {
            // Do not apply abstract physics to the chunks themselves
            if (SystemAPI.HasComponent<ChunkCoordinate>(entity)) continue;

            // Check if the entity is outside the active chunk rendering radius
            float dist = math.distance(transform.ValueRO.Position, playerPos);
            
            // Give a 1-chunk buffer before abstract physics takes over
            if (dist > renderDistance + (settings.ChunkSize.x * 1.5f))
            {
                // Abstract Physics Simulation:
                // We sample the exact mathematical density of the terrain at this position!
                float density = noiseSettings.EvaluateDensity(transform.ValueRO.Position);

                // If density > 0, the entity has clipped below the mathematical ground line!
                if (density > 0)
                {
                    // Because our noise density function is exactly (Height - Y),
                    // adding the density to the Y coordinate snaps it perfectly to the surface!
                    transform.ValueRW.Position.y += density;
                    
                    // Stop downward falling to simulate ground collision
                    if (velocity.ValueRW.Linear.y < 0)
                    {
                        velocity.ValueRW.Linear.y = 0;
                    }
                }
            }
        }
    }
}
