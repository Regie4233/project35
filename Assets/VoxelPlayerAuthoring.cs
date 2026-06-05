using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct VoxelPlayer : IComponentData
{
    public float Speed;
    public float JumpForce;
}

public class VoxelPlayerAuthoring : MonoBehaviour
{
    public float Speed = 8f;
    public float JumpForce = 10f;

    class Baker : Baker<VoxelPlayerAuthoring>
    {
        public override void Bake(VoxelPlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new VoxelPlayer
            {
                Speed = authoring.Speed,
                JumpForce = authoring.JumpForce
            });
        }
    }
}
