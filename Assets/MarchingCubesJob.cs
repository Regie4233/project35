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

        for (int z = 0; z < ChunkSize.z - 1; z++)
        {
            for (int y = 0; y < ChunkSize.y - 1; y++)
            {
                for (int x = 0; x < ChunkSize.x - 1; x++)
                {
                    int cubeIndex = 0;
                    
                    for (int i = 0; i < 8; i++)
                    {
                        int3 p = new int3(x, y, z) + (int3)cornerOffsets[i];
                        int flatIndex = p.x + (p.y * ChunkSize.x) + (p.z * ChunkSize.x * ChunkSize.y);

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
                        float3 v0 = new float3(x,y,z) + edgeVertices[TriTable[cubeIndex * 16 + i]];
                        float3 v1 = new float3(x,y,z) + edgeVertices[TriTable[cubeIndex * 16 + i + 1]];
                        float3 v2 = new float3(x,y,z) + edgeVertices[TriTable[cubeIndex * 16 + i + 2]];

                        // Calculate normal for this triangle
                        float3 normal = math.normalize(math.cross(v1 - v0, v2 - v0));

                        AddVertex(v0, normal);
                        AddVertex(v1, normal);
                        AddVertex(v2, normal);
                    }
                }
            }
        }
        cubeValues.Dispose();
        edgeVertices.Dispose();
        cornerOffsets.Dispose();
    }

    private float3 VertexInterp(float isoLevel, float3 p1, float3 p2, float valp1, float valp2)
    {
        if (math.abs(isoLevel - valp1) < 0.00001f) return p1;
        if (math.abs(isoLevel - valp2) < 0.00001f) return p2;
        if (math.abs(valp1 - valp2) < 0.00001f) return p1;

        float mu = (isoLevel - valp1) / (valp2 - valp1);
        return p1 + mu * (p2 - p1);
    }

    private void AddVertex(float3 pos, float3 normal)
    {
        Indices.Add((ushort)Vertices.Length);
        Vertices.Add(pos);
        Normals.Add(normal);
        // Simple UV projection
        UVs.Add(new float2(pos.x, pos.z));
    }
}