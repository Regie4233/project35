using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public class FlyCamera : MonoBehaviour
{
    public float speed = 50.0f;
    public float mouseSensitivity = 2.0f;
    private float yaw = 0.0f;
    private float pitch = 0.0f;

    void Start()
    {
        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
        }
        if (Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += mouseSensitivity * Input.GetAxis("Mouse X");
            pitch -= mouseSensitivity * Input.GetAxis("Mouse Y");
            pitch = Mathf.Clamp(pitch, -90f, 90f);
            transform.eulerAngles = new Vector3(pitch, yaw, 0.0f);
        }

        Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        
        float currentSpeed = speed;
        if (Input.GetKey(KeyCode.LeftShift)) currentSpeed *= 3f;

        transform.Translate(move * currentSpeed * Time.deltaTime);
        
        if (Input.GetKey(KeyCode.E)) transform.position += Vector3.up * currentSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.Q)) transform.position -= Vector3.up * currentSpeed * Time.deltaTime;
    }
}

public struct LODViewer : IComponentData
{
    public float3 Position;
}

public partial class LODViewerSyncSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (Camera.main != null)
        {
            float3 camPos = Camera.main.transform.position;
            // Update all entities that have LODViewer component, or create a singleton
            if (!SystemAPI.HasSingleton<LODViewer>())
            {
                var entity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(entity, new LODViewer { Position = camPos });
            }
            else
            {
                SystemAPI.SetSingleton(new LODViewer { Position = camPos });
            }
        }
    }
}
