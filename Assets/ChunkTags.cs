using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

public struct ChunkNeedsNoiseTag : IComponentData { }

public struct ChunkNeedsMeshingTag : IComponentData { }

public struct ChunkNeedsCollisionTag : IComponentData { }

public struct ChunkReadyTag : IComponentData { }

public struct ChunkNoiseJobActiveTag : IComponentData { }
public struct ChunkMeshingJobActiveTag : IComponentData { }

public struct ChunkNoiseJobInfo : IComponentData
{
    public JobHandle Handle;
    public NativeArray<VoxelDataElement> VoxelData;
}

public struct ChunkMeshingJobInfo : IComponentData
{
    public JobHandle Handle;
    public NativeArray<VoxelDataElement> VoxelDataCopy;
    public NativeList<float3> Vertices;
    public NativeList<ushort> Indices;
    public NativeList<float3> Normals;
    public NativeList<float2> UVs;
    public NativeList<float4> Colors;
}

public struct SafetyPosition : IComponentData
{
    public Unity.Mathematics.float3 Value;
}

public struct ChunkCoordinate : IComponentData
{
    public Unity.Mathematics.int3 Value;
}

