using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


public struct MarchingCubesJob : IJob
{
    [ReadOnly] public NativeArray<VoxelDataElement> VoxelData;
    
    public int3 ChunkSize;
    public float IsoLevel;

    // Dynamically sized collections to hold our generated mesh
    public NativeList<float3> Vertices;
    public NativeList<ushort> Indices;
    public NativeList<float3> Normals;
    public NativeList<float2> UVs;
    public NativeList<float4> Colors;

    [ReadOnly] public NativeArray<int> EdgeTable;
    [ReadOnly] public NativeArray<int> TriTable;

    public void Execute()
    {
        NativeArray<float3> cornerOffsets = new NativeArray<float3>(8, Allocator.Temp);
        cornerOffsets[0] = new float3(0, 0, 0);
        cornerOffsets[1] = new float3(1, 0, 0);
        cornerOffsets[2] = new float3(1, 1, 0);
        cornerOffsets[3] = new float3(0, 1, 0);
        cornerOffsets[4] = new float3(0, 0, 1);
        cornerOffsets[5] = new float3(1, 0, 1);
        cornerOffsets[6] = new float3(1, 1, 1);
        cornerOffsets[7] = new float3(0, 1, 1);

        float threshold = IsoLevel * 255f;

        NativeArray<float> cubeValues = new NativeArray<float>(8, Allocator.Temp);
        NativeArray<float3> edgeVertices = new NativeArray<float3>(12, Allocator.Temp);

        int3 pSize = ChunkSize + 1;

        // Loop up to ChunkSize (instead of ChunkSize - 1) because we have ChunkSize + 1 data
        for (int z = 0; z < ChunkSize.z; z++)
        {
            for (int y = 0; y < ChunkSize.y; y++)
            {
                for (int x = 0; x < ChunkSize.x; x++)
                {
                    int cubeIndex = 0;
                    
                    for (int i = 0; i < 8; i++)
                    {
                        int3 p = new int3(x, y, z) + (int3)cornerOffsets[i];
                        int flatIndex = p.x + (p.y * pSize.x) + (p.z * pSize.x * pSize.y);

                        // Treat density as continuous value.
                        cubeValues[i] = VoxelData[flatIndex].GetDensity();

                        if (cubeValues[i] < threshold)
                        {
                            cubeIndex |= 1 << i;
                        }
                    }

                    if (EdgeTable[cubeIndex] == 0) continue;

                    if ((EdgeTable[cubeIndex] & 1) != 0) edgeVertices[0] = VertexInterp(threshold, cornerOffsets[0], cornerOffsets[1], cubeValues[0], cubeValues[1]);
                    if ((EdgeTable[cubeIndex] & 2) != 0) edgeVertices[1] = VertexInterp(threshold, cornerOffsets[1], cornerOffsets[2], cubeValues[1], cubeValues[2]);
                    if ((EdgeTable[cubeIndex] & 4) != 0) edgeVertices[2] = VertexInterp(threshold, cornerOffsets[2], cornerOffsets[3], cubeValues[2], cubeValues[3]);
                    if ((EdgeTable[cubeIndex] & 8) != 0) edgeVertices[3] = VertexInterp(threshold, cornerOffsets[3], cornerOffsets[0], cubeValues[3], cubeValues[0]);
                    if ((EdgeTable[cubeIndex] & 16) != 0) edgeVertices[4] = VertexInterp(threshold, cornerOffsets[4], cornerOffsets[5], cubeValues[4], cubeValues[5]);
                    if ((EdgeTable[cubeIndex] & 32) != 0) edgeVertices[5] = VertexInterp(threshold, cornerOffsets[5], cornerOffsets[6], cubeValues[5], cubeValues[6]);
                    if ((EdgeTable[cubeIndex] & 64) != 0) edgeVertices[6] = VertexInterp(threshold, cornerOffsets[6], cornerOffsets[7], cubeValues[6], cubeValues[7]);
                    if ((EdgeTable[cubeIndex] & 128) != 0) edgeVertices[7] = VertexInterp(threshold, cornerOffsets[7], cornerOffsets[4], cubeValues[7], cubeValues[4]);
                    if ((EdgeTable[cubeIndex] & 256) != 0) edgeVertices[8] = VertexInterp(threshold, cornerOffsets[0], cornerOffsets[4], cubeValues[0], cubeValues[4]);
                    if ((EdgeTable[cubeIndex] & 512) != 0) edgeVertices[9] = VertexInterp(threshold, cornerOffsets[1], cornerOffsets[5], cubeValues[1], cubeValues[5]);
                    if ((EdgeTable[cubeIndex] & 1024) != 0) edgeVertices[10] = VertexInterp(threshold, cornerOffsets[2], cornerOffsets[6], cubeValues[2], cubeValues[6]);
                    if ((EdgeTable[cubeIndex] & 2048) != 0) edgeVertices[11] = VertexInterp(threshold, cornerOffsets[3], cornerOffsets[7], cubeValues[3], cubeValues[7]);

                    for (int i = 0; TriTable[cubeIndex * 16 + i] != -1; i += 3)
                    {
                        float3 v0_local = edgeVertices[TriTable[cubeIndex * 16 + i]];
                        float3 v1_local = edgeVertices[TriTable[cubeIndex * 16 + i + 1]];
                        float3 v2_local = edgeVertices[TriTable[cubeIndex * 16 + i + 2]];

                        float3 v0 = new float3(x, y, z) + v0_local;
                        float3 v1 = new float3(x, y, z) + v1_local;
                        float3 v2 = new float3(x, y, z) + v2_local;

                        // Use gradient normals for smooth lighting
                        AddVertex(v0, GetGradientNormal(v0));
                        AddVertex(v1, GetGradientNormal(v1));
                        AddVertex(v2, GetGradientNormal(v2));
                    }
                }
            }
        }
        cubeValues.Dispose();
        edgeVertices.Dispose();
        cornerOffsets.Dispose();
    }

    private float3 GetGradientNormal(float3 pos)
    {
        int3 pSize = ChunkSize + 1;
        
        // Sample surrounding densities
        float dx = GetDensityInterpolated(pos + new float3(1, 0, 0)) - GetDensityInterpolated(pos - new float3(1, 0, 0));
        float dy = GetDensityInterpolated(pos + new float3(0, 1, 0)) - GetDensityInterpolated(pos - new float3(0, 1, 0));
        float dz = GetDensityInterpolated(pos + new float3(0, 0, 1)) - GetDensityInterpolated(pos - new float3(0, 0, 1));

        return math.normalize(new float3(dx, dy, dz));
    }

    private float GetDensityInterpolated(float3 pos)
    {
        int3 pSize = ChunkSize + 1;
        int3 p0 = (int3)math.floor(pos);
        int3 p1 = p0 + 1;

        // Clamp to avoid out of bounds sampling
        p0 = math.clamp(p0, 0, pSize - 1);
        p1 = math.clamp(p1, 0, pSize - 1);

        float3 f = pos - math.floor(pos);

        float d000 = VoxelData[p0.x + (p0.y * pSize.x) + (p0.z * pSize.x * pSize.y)].GetDensity();
        float d100 = VoxelData[p1.x + (p0.y * pSize.x) + (p0.z * pSize.x * pSize.y)].GetDensity();
        float d010 = VoxelData[p0.x + (p1.y * pSize.x) + (p0.z * pSize.x * pSize.y)].GetDensity();
        float d110 = VoxelData[p1.x + (p1.y * pSize.x) + (p0.z * pSize.x * pSize.y)].GetDensity();
        float d001 = VoxelData[p0.x + (p0.y * pSize.x) + (p1.z * pSize.x * pSize.y)].GetDensity();
        float d101 = VoxelData[p1.x + (p0.y * pSize.x) + (p1.z * pSize.x * pSize.y)].GetDensity();
        float d011 = VoxelData[p0.x + (p1.y * pSize.x) + (p1.z * pSize.x * pSize.y)].GetDensity();
        float d111 = VoxelData[p1.x + (p1.y * pSize.x) + (p1.z * pSize.x * pSize.y)].GetDensity();

        float d00 = math.lerp(d000, d100, f.x);
        float d10 = math.lerp(d010, d110, f.x);
        float d01 = math.lerp(d001, d101, f.x);
        float d11 = math.lerp(d011, d111, f.x);

        float d0 = math.lerp(d00, d10, f.y);
        float d1 = math.lerp(d01, d11, f.y);

        return math.lerp(d0, d1, f.z);
    }

    private float3 VertexInterp(float isoLevel, float3 p1, float3 p2, float valp1, float valp2)
    {
        if (math.abs(isoLevel - valp1) < 0.00001f) return p1;
        if (math.abs(isoLevel - valp2) < 0.00001f) return p2;
        if (math.abs(valp1 - valp2) < 0.00001f) return p1;

        float mu = (isoLevel - valp1) / (valp2 - valp1);
        return p1 + mu * (p2 - p1);
    }

    private ushort GetMaterialAt(int3 p)
    {
        int3 pSize = ChunkSize + 1;
        p = math.clamp(p, 0, pSize - 1);
        int flatIndex = p.x + (p.y * pSize.x) + (p.z * pSize.x * pSize.y);
        return VoxelData[flatIndex].GetMaterialID();
    }

    private void AddVertex(float3 pos, float3 normal)
    {
        Indices.Add((ushort)Vertices.Length);
        Vertices.Add(pos);
        Normals.Add(normal);
        // Simple UV projection
        UVs.Add(new float2(pos.x, pos.z));

        // Determine vertex color based on nearest voxel material ID
        int3 voxelPos = (int3)math.round(pos);
        ushort matId = GetMaterialAt(voxelPos);

        float4 color = new float4(1.0f, 1.0f, 1.0f, 1.0f);
        if (matId == 1)
        {
            color = new float4(0.27f, 0.62f, 0.18f, 1.0f); // Dirt/Grass: Green
        }
        else if (matId == 2)
        {
            color = new float4(0.5f, 0.5f, 0.5f, 1.0f); // Stone: Grey
        }
        else if (matId == 3)
        {
            color = new float4(0.9f, 0.1f, 0.1f, 1.0f); // Painted: Red
        }

        Colors.Add(color);
    }
}
