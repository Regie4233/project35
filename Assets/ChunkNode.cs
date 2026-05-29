using Unity.Entities;
using Unity.Mathematics;

public struct ChunkNode : IComponentData
{
    public int3 NodePosition; // Virtual grid position for this LOD level
    public int LODLevel;      // 0 = finest detail, higher = coarser detail
    public float NodeSize;    // World space size of this chunk
    public bool IsActive;     // Whether it's the active node being rendered
    public Entity Parent;     // Reference to the parent chunk if merged
}
