using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial class VoxelPlayerSystem : SystemBase
{
    private Vector2 pitchYaw;
    private bool isInitialized = false;

    protected override void OnUpdate()
    {
        if (Camera.main == null || Keyboard.current == null || Mouse.current == null) return;

        if (!isInitialized)
        {
            Cursor.lockState = CursorLockMode.Locked;
            pitchYaw = new Vector2(Camera.main.transform.eulerAngles.x, Camera.main.transform.eulerAngles.y);
            isInitialized = true;
        }

        // Release cursor on Escape
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
        }

        // Only process look/movement if locked
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            pitchYaw.x -= mouseDelta.y * 0.1f; // Pitch
            pitchYaw.y += mouseDelta.x * 0.1f; // Yaw
            pitchYaw.x = Mathf.Clamp(pitchYaw.x, -89f, 89f);
            
            Camera.main.transform.rotation = Quaternion.Euler(pitchYaw.x, pitchYaw.y, 0);
        }

        float2 moveInput = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) moveInput.y += 1;
        if (Keyboard.current.sKey.isPressed) moveInput.y -= 1;
        if (Keyboard.current.aKey.isPressed) moveInput.x -= 1;
        if (Keyboard.current.dKey.isPressed) moveInput.x += 1;

        bool jumpPressed = Keyboard.current.spaceKey.wasPressedThisFrame;

        float3 camForward = Camera.main.transform.forward;
        camForward.y = 0;
        camForward = math.normalizesafe(camForward);
        
        float3 camRight = Camera.main.transform.right;
        camRight.y = 0;
        camRight = math.normalizesafe(camRight);

        foreach (var (player, velocity, transform) in SystemAPI.Query<RefRO<VoxelPlayer>, RefRW<PhysicsVelocity>, RefRW<LocalTransform>>())
        {
            float3 moveDir = camForward * moveInput.y + camRight * moveInput.x;
            if (math.lengthsq(moveDir) > 0) moveDir = math.normalize(moveDir);

            // Apply X/Z velocity while keeping Y (gravity)
            velocity.ValueRW.Linear.x = moveDir.x * player.ValueRO.Speed;
            velocity.ValueRW.Linear.z = moveDir.z * player.ValueRO.Speed;

            // Simple jump check
            if (jumpPressed && math.abs(velocity.ValueRO.Linear.y) < 0.2f)
            {
                velocity.ValueRW.Linear.y = player.ValueRO.JumpForce;
            }

            // Sync Camera position to player's head
            Camera.main.transform.position = transform.ValueRO.Position + new float3(0, 0.8f, 0);
        }
    }
}
