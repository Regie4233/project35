using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Physics;
using UnityEngine;


public struct AsyncCollisionBakeJob : IJobParallelFor
{
    public NativeArray<Mesh.MeshData> MeshData;
    public NativeArray<BlobAssetReference<Unity.Physics.Collider>> ColliderBlobReferences;

    public void Execute(int index)
    {
        // Build the BVH physics acceleration structure asynchronously
        ColliderBlobReferences[index] = Unity.Physics.MeshCollider.Create(
            MeshData[index], 
            CollisionFilter.Default, 
            Unity.Physics.Material.Default
        );
    }
}