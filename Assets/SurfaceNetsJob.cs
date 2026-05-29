using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct SurfaceNetsJob : IJob
{
    [ReadOnly] public NativeArray<VoxelDataElement> VoxelData;
    public int3 ChunkSize;
    public float ChunkScale;

    public NativeList<float3> Vertices;
    public NativeList<ushort> Indices;

    private int GetIndex(int x, int y, int z) => x + y * ChunkSize.x + z * ChunkSize.x * ChunkSize.y;
    private int GetCellIndex(int x, int y, int z, int3 cellSize) => x + y * cellSize.x + z * cellSize.x * cellSize.y;

    public void Execute()
    {
        int3 cellSize = ChunkSize - new int3(1, 1, 1);
        NativeArray<int> cellVertexIndex = new NativeArray<int>(cellSize.x * cellSize.y * cellSize.z, Allocator.Temp);
        for (int i = 0; i < cellVertexIndex.Length; i++) cellVertexIndex[i] = -1;

        float3 voxelSpacing = new float3(ChunkScale) / new float3(ChunkSize);

        // Cube corner offsets
        int3[] corners = new int3[8]
        {
            new int3(0, 0, 0), new int3(1, 0, 0), new int3(1, 1, 0), new int3(0, 1, 0),
            new int3(0, 0, 1), new int3(1, 0, 1), new int3(1, 1, 1), new int3(0, 1, 1)
        };

        // Edge connections between corners
        int2[] edges = new int2[12]
        {
            new int2(0,1), new int2(1,2), new int2(2,3), new int2(3,0),
            new int2(4,5), new int2(5,6), new int2(6,7), new int2(7,4),
            new int2(0,4), new int2(1,5), new int2(2,6), new int2(3,7)
        };

        // Pass 1: Find boundary cells and generate vertices
        for (int z = 0; z < cellSize.z; z++)
        {
            for (int y = 0; y < cellSize.y; y++)
            {
                for (int x = 0; x < cellSize.x; x++)
                {
                    int mask = 0;
                    float[] sdfs = new float[8];
                    for (int i = 0; i < 8; i++)
                    {
                        int3 c = new int3(x, y, z) + corners[i];
                        float sdf = VoxelData[GetIndex(c.x, c.y, c.z)].SDF;
                        sdfs[i] = sdf;
                        if (sdf < 0f) mask |= (1 << i);
                    }

                    // If cell is completely inside (255) or completely outside (0), skip
                    if (mask == 0 || mask == 255) continue;

                    // Calculate vertex position via edge intersections
                    float3 vertexPos = float3.zero;
                    int intersections = 0;

                    for (int e = 0; e < 12; e++)
                    {
                        int c0 = edges[e].x;
                        int c1 = edges[e].y;
                        bool sign0 = (mask & (1 << c0)) != 0;
                        bool sign1 = (mask & (1 << c1)) != 0;

                        if (sign0 != sign1)
                        {
                            // Interpolate
                            float t = sdfs[c0] / (sdfs[c0] - sdfs[c1]);
                            float3 p0 = new float3(corners[c0]);
                            float3 p1 = new float3(corners[c1]);
                            vertexPos += math.lerp(p0, p1, t);
                            intersections++;
                        }
                    }

                    vertexPos /= intersections;
                    
                    // Local grid position to chunk physical space
                    float3 localPos = (new float3(x, y, z) + vertexPos) * voxelSpacing;
                    
                    // Apply skirt (drop edges) if on the chunk boundary
                    if (x == 0 || x == cellSize.x - 1 || z == 0 || z == cellSize.z - 1)
                    {
                        localPos.y -= voxelSpacing.y * 2f; // simple skirt
                    }

                    int vIndex = Vertices.Length;
                    Vertices.Add(localPos);
                    cellVertexIndex[GetCellIndex(x, y, z, cellSize)] = vIndex;
                }
            }
        }

        // Pass 2: Connect vertices to form quads along intersecting voxel edges
        for (int z = 1; z < cellSize.z; z++)
        for (int y = 1; y < cellSize.y; y++)
        for (int x = 1; x < cellSize.x; x++)
        {
            // Check X axis edge
            bool solid1 = VoxelData[GetIndex(x, y, z)].SDF < 0f;
            bool solid2 = VoxelData[GetIndex(x, y-1, z)].SDF < 0f;
            bool solid3 = VoxelData[GetIndex(x, y-1, z-1)].SDF < 0f;
            bool solid4 = VoxelData[GetIndex(x, y, z-1)].SDF < 0f;
            
            // Simplified dual meshing face generation:
            // For each internal grid vertex (which corresponds to 8 surrounding cells),
            // we check the 3 edges (X, Y, Z) radiating from it.
            // If an edge crosses the surface (SDF changes sign), we emit a quad connecting the 4 cells that share this edge.
            
            // Edge in X direction
            bool s0 = VoxelData[GetIndex(x, y, z)].SDF < 0f;
            bool sx = VoxelData[GetIndex(x-1, y, z)].SDF < 0f;
            if (s0 != sx) {
                int v0 = cellVertexIndex[GetCellIndex(x-1, y, z, cellSize)];
                int v1 = cellVertexIndex[GetCellIndex(x-1, y-1, z, cellSize)];
                int v2 = cellVertexIndex[GetCellIndex(x-1, y-1, z-1, cellSize)];
                int v3 = cellVertexIndex[GetCellIndex(x-1, y, z-1, cellSize)];
                if(v0!=-1 && v1!=-1 && v2!=-1 && v3!=-1) EmitQuad(v0, v1, v2, v3, s0);
            }

            // Edge in Y direction
            bool sy = VoxelData[GetIndex(x, y-1, z)].SDF < 0f;
            if (s0 != sy) {
                int v0 = cellVertexIndex[GetCellIndex(x, y-1, z, cellSize)];
                int v1 = cellVertexIndex[GetCellIndex(x, y-1, z-1, cellSize)];
                int v2 = cellVertexIndex[GetCellIndex(x-1, y-1, z-1, cellSize)];
                int v3 = cellVertexIndex[GetCellIndex(x-1, y-1, z, cellSize)];
                if(v0!=-1 && v1!=-1 && v2!=-1 && v3!=-1) EmitQuad(v0, v1, v2, v3, s0);
            }

            // Edge in Z direction
            bool sz = VoxelData[GetIndex(x, y, z-1)].SDF < 0f;
            if (s0 != sz) {
                int v0 = cellVertexIndex[GetCellIndex(x, y, z-1, cellSize)];
                int v1 = cellVertexIndex[GetCellIndex(x-1, y, z-1, cellSize)];
                int v2 = cellVertexIndex[GetCellIndex(x-1, y-1, z-1, cellSize)];
                int v3 = cellVertexIndex[GetCellIndex(x, y-1, z-1, cellSize)];
                if(v0!=-1 && v1!=-1 && v2!=-1 && v3!=-1) EmitQuad(v0, v1, v2, v3, s0);
            }
        }
        
        cellVertexIndex.Dispose();
    }

    private void EmitQuad(int v0, int v1, int v2, int v3, bool flip)
    {
        if (flip)
        {
            Indices.Add((ushort)v0); Indices.Add((ushort)v1); Indices.Add((ushort)v2);
            Indices.Add((ushort)v0); Indices.Add((ushort)v2); Indices.Add((ushort)v3);
        }
        else
        {
            Indices.Add((ushort)v0); Indices.Add((ushort)v2); Indices.Add((ushort)v1);
            Indices.Add((ushort)v0); Indices.Add((ushort)v3); Indices.Add((ushort)v2);
        }
    }
}
