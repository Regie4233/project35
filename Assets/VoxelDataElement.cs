using Unity.Entities;

// Setting capacity to 0 forces Unity to allocate this buffer outside the archetype chunk, 
// preventing memory fragmentation for large 16x16x16 (4096) grids.

public struct VoxelDataElement : IBufferElementData
{
    // Bits 0-7: Density (SDF for smooth terrain)
    // Bits 8-23: Material ID (for blocky structures)
    // Bits 24-31: State Flags (Solid, Air, Unsupported, etc.)
    public uint Value; 

    public byte GetDensity() => (byte)(Value & 0xFF);
    public ushort GetMaterialID() => (ushort)((Value >> 8) & 0xFFFF);
    public bool IsSolid() => ((Value >> 24) & 1) == 1;
}