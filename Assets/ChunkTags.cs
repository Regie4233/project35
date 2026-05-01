using Unity.Entities;

public struct ChunkNeedsNoiseTag : IComponentData { }

public struct ChunkNeedsMeshingTag : IComponentData { }

public struct ChunkNeedsCollisionTag : IComponentData { }

public struct ChunkReadyTag : IComponentData { }

public struct ChunkCoordinate : IComponentData
{
    public Unity.Mathematics.int3 Value;
}
