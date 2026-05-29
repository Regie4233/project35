using Unity.Entities;
using UnityEngine;

public struct PlayerTag : IComponentData { }

public class PlayerTrackerAuthoring : MonoBehaviour
{
    class Baker : Baker<PlayerTrackerAuthoring>
    {
        public override void Bake(PlayerTrackerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerTag>(entity);
        }
    }
}
