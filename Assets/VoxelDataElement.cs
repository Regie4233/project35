using Unity.Entities;

// Setting capacity to 0 forces Unity to allocate this buffer outside the archetype chunk, 
// preventing memory fragmentation for large 16x16x16 (4096) grids.

[InternalBufferCapacity(0)]
public struct VoxelDataElement : IBufferElementData
{
    // Exact float representation of the Signed Distance Field (SDF)
    // Negative values are inside the terrain (solid), positive are outside (air)
    public float SDF;
    
    // Material and State flags
    public uint MaterialFlags;

    public ushort GetMaterialID() => (ushort)(MaterialFlags & 0xFFFF);
    public bool IsSolid() => SDF <= 0f;
}