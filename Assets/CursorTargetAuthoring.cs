using Unity.Entities;
using UnityEngine;

public class CursorTargetAuthoring : MonoBehaviour
{
    class Baker : Baker<CursorTargetAuthoring>
    {
        public override void Bake(CursorTargetAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<CursorTargetTag>(entity);
        }
    }
}

public struct CursorTargetTag : IComponentData { }
