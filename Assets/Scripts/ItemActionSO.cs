using UnityEngine;

public abstract class ItemActionSO : ScriptableObject
{
    public abstract ItemActionInstance CreateInstance();
}

public abstract class ItemActionInstance
{
    public virtual void OnEquip(ItemActionContext ctx) { }
    public virtual void OnUnequip(ItemActionContext ctx) { }

    public virtual void OnUseStarted(ItemActionContext ctx) { }
    public virtual void OnUseCanceled(ItemActionContext ctx) { }

    public virtual void Tick(ItemActionContext ctx) { }

    /// <summary>
    /// Lets the action add visual offsets (wiggle, sway) to the held object's target.
    /// </summary>
    public virtual void ModifyHoldTarget(ItemActionContext ctx, ref Vector3 targetPos, ref Quaternion targetRot) { }
}

public readonly struct ItemActionContext
{
    public Camera Camera { get; }
    public Transform HoldPoint { get; }
    public Pickupable Held { get; }

    public float DeltaTime { get; }
    public float Time { get; }

    /// <summary>Camera yaw change this frame in degrees (positive/right, negative/left).</summary>
    public float CameraYawDelta { get; }

    public bool IsUsing { get; }

    public ItemActionContext(
        Camera camera,
        Transform holdPoint,
        Pickupable held,
        float deltaTime,
        float time,
        float cameraYawDelta,
        bool isUsing)
    {
        Camera = camera;
        HoldPoint = holdPoint;
        Held = held;
        DeltaTime = deltaTime;
        Time = time;
        CameraYawDelta = cameraYawDelta;
        IsUsing = isUsing;
    }
}
