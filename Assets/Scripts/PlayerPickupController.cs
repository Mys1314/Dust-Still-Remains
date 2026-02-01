using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class PlayerPickupController : MonoBehaviour
{
    [Header("Raycast (E pickup)")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float interactDistance = 2f;
    [SerializeField] private LayerMask interactMask = ~0;
    [SerializeField] private InputActionReference interactAction;

    [Header("Left Click Use")]
    [SerializeField] private InputActionReference useAction; // <Mouse>/leftButton
    [SerializeField] private float throwForce = 10f;

    [Header("Holding")]
    [SerializeField] private Transform holdPoint;
    [SerializeField] private float followSmooth = 20f;
    [SerializeField] private bool matchHoldRotation = true;

    [Header("Holding Collision Avoidance")]
    [Tooltip("If the held item overlaps colliders on this LayerMask, it will be pushed up/down (Y axis) until it no longer overlaps.")]
    [SerializeField] private LayerMask environmentMask;

    [Tooltip("Maximum absolute Y offset (meters) that can be applied to resolve overlaps.")]
    [SerializeField] private float maxResolveYOffset = 0.35f;

    [Tooltip("How many iterations to try resolving overlaps per frame.")]
    [SerializeField, Range(1, 12)] private int resolveIterations = 6;

    [Tooltip("Small extra distance to separate colliders after using ComputePenetration.")]
    [SerializeField] private float resolveSkin = 0.002f;

    [Header("UI Prompt")]
    [SerializeField] private GameObject promptRoot;   // enable/disable this GO
    [SerializeField] private TMP_Text promptText;     // TMP component
    [SerializeField] private string pickUpPrompt = "Press E to pick up";

    private bool promptVisible;

    private Pickupable lookedAt;
    private Pickupable held;

    private Rigidbody heldRb;
    private Collider[] heldColliders;
    private Collider[] playerColliders;

    private ItemActionInstance heldActionInstance;
    private bool isUsing;

    private float lastCamYaw;
    private float camYawDeltaThisFrame;

    // Offsets derived from the held item's HoldPose anchor.
    // These are applied so that the anchor exactly matches the holdPoint.
    private Quaternion holdRotationOffset = Quaternion.identity;
    private Vector3 holdPositionOffsetLocal = Vector3.zero;

    private void Awake()
    {
        if (!playerCamera) playerCamera = Camera.main;
        playerColliders = GetComponentsInChildren<Collider>();

        lastCamYaw = playerCamera.transform.eulerAngles.y;

        SetPrompt(false);
    }

    private void OnEnable()
    {
        if (interactAction != null)
        {
            interactAction.action.performed += OnInteract;
            interactAction.action.Enable();
        }

        if (useAction != null)
        {
            useAction.action.performed += OnUseStarted;
            useAction.action.canceled += OnUseCanceled;
            useAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (interactAction != null)
        {
            interactAction.action.performed -= OnInteract;
            interactAction.action.Disable();
        }

        if (useAction != null)
        {
            useAction.action.performed -= OnUseStarted;
            useAction.action.canceled -= OnUseCanceled;
            useAction.action.Disable();
        }
    }

    private void Update()
    {
        UpdateCameraYawDelta();

        if (held == null)
        {
            ScanForPickupable();
            UpdatePrompt();
        }
        else
        {
            lookedAt = null;
            SetPrompt(false);
            heldActionInstance?.Tick(BuildCtx());
        }
    }

    private void LateUpdate()
    {
        if (held != null)
            MoveHeldObject();
    }

    private void UpdateCameraYawDelta()
    {
        float currentYaw = playerCamera.transform.eulerAngles.y;
        camYawDeltaThisFrame = Mathf.DeltaAngle(lastCamYaw, currentYaw);
        lastCamYaw = currentYaw;
    }

    private void ScanForPickupable()
    {
        var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (Physics.Raycast(ray, out var hit, interactDistance, interactMask, QueryTriggerInteraction.Ignore))
            lookedAt = hit.collider.GetComponentInParent<Pickupable>();
        else
            lookedAt = null;
    }

    private void UpdatePrompt()
    {
        bool shouldShow = (lookedAt != null);

        if (shouldShow)
        {
            if (promptText != null)
                promptText.text = pickUpPrompt;

            SetPrompt(true);
        }
        else
        {
            SetPrompt(false);
        }
    }

    private void SetPrompt(bool show)
    {
        if (promptRoot == null) return;
        if (promptVisible == show) return;

        promptVisible = show;
        promptRoot.SetActive(show);
    }

    private void OnInteract(InputAction.CallbackContext ctx)
    {
        if (held == null)
        {
            if (lookedAt != null) Pickup(lookedAt);
        }
        else
        {
            Drop();
        }
    }

    private void OnUseStarted(InputAction.CallbackContext ctx)
    {
        if (held == null) return;

        // No ScriptableObject attached => default throw
        if (heldActionInstance == null)
        {
            Throw();
            return;
        }

        isUsing = true;
        heldActionInstance.OnUseStarted(BuildCtx());
    }

    private void OnUseCanceled(InputAction.CallbackContext ctx)
    {
        if (held == null) return;
        if (heldActionInstance == null) return;

        isUsing = false;
        heldActionInstance.OnUseCanceled(BuildCtx());
    }

    private void Pickup(Pickupable p)
    {
        held = p;
        heldRb = p.Rb;

        ComputeHoldOffsets();

        heldRb.isKinematic = true;
        heldRb.useGravity = false;

        heldColliders = held.GetComponentsInChildren<Collider>();
        foreach (var hc in heldColliders)
            foreach (var pc in playerColliders)
                if (hc && pc) Physics.IgnoreCollision(hc, pc, true);

        EquipHeldAction();
        SetPrompt(false);
    }

    private void EquipHeldAction()
    {
        isUsing = false;
        heldActionInstance = null;

        var provider = held.GetComponentInParent<ItemActionProvider>();
        if (provider != null && provider.leftClickAction != null)
        {
            heldActionInstance = provider.leftClickAction.CreateInstance();
            heldActionInstance.OnEquip(BuildCtx());
        }
    }

    private void MoveHeldObject()
    {
        float t = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);

        Quaternion baseRot = holdPoint.rotation;
        Vector3 basePos = holdPoint.position;

        // Apply offsets so the item's anchor matches the hold point.
        Quaternion targetRot = baseRot * holdRotationOffset;
        Vector3 targetPos = basePos + (targetRot * holdPositionOffsetLocal);

        // Allow action to add visual offsets (wiggle, sway) before we resolve collision.
        heldActionInstance?.ModifyHoldTarget(BuildCtx(), ref targetPos, ref targetRot);

        // Prevent held items from being placed inside environment geometry.
        ResolveEnvironmentOverlap(ref targetPos, targetRot);

        held.transform.position = Vector3.Lerp(held.transform.position, targetPos, t);

        if (matchHoldRotation)
            held.transform.rotation = Quaternion.Slerp(held.transform.rotation, targetRot, t);
    }

    private void ResolveEnvironmentOverlap(ref Vector3 targetPos, Quaternion targetRot)
    {
        if (environmentMask == 0) return;
        if (held == null) return;

        // Cache colliders if possible.
        if (heldColliders == null || heldColliders.Length == 0)
            heldColliders = held.GetComponentsInChildren<Collider>();

        if (heldColliders == null || heldColliders.Length == 0) return;

        float accumulatedY = 0f;

        for (int iter = 0; iter < resolveIterations; iter++)
        {
            bool anyPenetration = false;
            float requiredStepY = 0f;

            for (int i = 0; i < heldColliders.Length; i++)
            {
                var c = heldColliders[i];
                if (c == null) continue;
                if (!c.enabled) continue;
                if (c.isTrigger) continue;

                Pose colliderPose = GetColliderWorldPoseAtTarget(held.transform, c.transform, targetPos, targetRot);
                Bounds b = GetBroadphaseBoundsAtTarget(c, colliderPose.position, colliderPose.rotation);

                var envHits = Physics.OverlapBox(b.center, b.extents, colliderPose.rotation, environmentMask, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < envHits.Length; h++)
                {
                    var env = envHits[h];
                    if (env == null) continue;
                    if (!env.enabled) continue;
                    if (env.isTrigger) continue;

                    if (Physics.ComputePenetration(
                            c, colliderPose.position, colliderPose.rotation,
                            env, env.transform.position, env.transform.rotation,
                            out Vector3 direction, out float distance))
                    {
                        anyPenetration = true;

                        // Choose up/down based on relative position to the thing we hit.
                        float rel = colliderPose.position.y - env.bounds.center.y;
                        float sign = rel < 0f ? -1f : 1f;

                        float dy = sign * Mathf.Abs(direction.y) * (distance + resolveSkin);
                        if (Mathf.Abs(dy) < 1e-5f)
                            dy = sign * (distance + resolveSkin);

                        if (Mathf.Abs(dy) > Mathf.Abs(requiredStepY))
                            requiredStepY = dy;
                    }
                }
            }

            if (!anyPenetration)
                break;

            float nextAccum = accumulatedY + requiredStepY;
            if (Mathf.Abs(nextAccum) > maxResolveYOffset)
            {
                requiredStepY = Mathf.Sign(nextAccum) * maxResolveYOffset - accumulatedY;
                targetPos.y += requiredStepY;
                break;
            }

            accumulatedY = nextAccum;
            targetPos.y += requiredStepY;
        }
    }

    private static Pose GetColliderWorldPoseAtTarget(Transform heldRoot, Transform colliderTransform, Vector3 targetRootPos, Quaternion targetRootRot)
    {
        // Compute collider local pose relative to the held root, then apply to target root pose.
        Vector3 localPos = heldRoot.InverseTransformPoint(colliderTransform.position);
        Quaternion localRot = Quaternion.Inverse(heldRoot.rotation) * colliderTransform.rotation;

        return new Pose(
            targetRootPos + (targetRootRot * localPos),
            targetRootRot * localRot);
    }

    private static Bounds GetBroadphaseBoundsAtTarget(Collider c, Vector3 worldPos, Quaternion worldRot)
    {
        // Conservative bounds used to find nearby environment colliders.
        if (c is BoxCollider bc)
        {
            Vector3 scaledCenter = Vector3.Scale(bc.center, bc.transform.lossyScale);
            Vector3 size = Vector3.Scale(bc.size, bc.transform.lossyScale);
            var bounds = new Bounds(worldPos + (worldRot * scaledCenter), size);
            bounds.Expand(0.01f);
            return bounds;
        }

        if (c is SphereCollider sc)
        {
            Vector3 scaledCenter = Vector3.Scale(sc.center, sc.transform.lossyScale);
            float maxScale = Mathf.Max(Mathf.Abs(sc.transform.lossyScale.x), Mathf.Abs(sc.transform.lossyScale.y), Mathf.Abs(sc.transform.lossyScale.z));
            float r = sc.radius * maxScale;
            var bounds = new Bounds(worldPos + (worldRot * scaledCenter), Vector3.one * (2f * r));
            bounds.Expand(0.01f);
            return bounds;
        }

        if (c is CapsuleCollider cc)
        {
            Vector3 s = cc.transform.lossyScale;
            Vector3 scaledCenter = Vector3.Scale(cc.center, s);

            float radiusScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            float r = cc.radius * radiusScale;
            float h = Mathf.Abs(cc.height * s.y);

            float extentY = Mathf.Max(h * 0.5f, r);
            float extentXZ = r;

            var bounds = new Bounds(worldPos + (worldRot * scaledCenter), new Vector3(extentXZ * 2f, extentY * 2f, extentXZ * 2f));
            bounds.Expand(0.02f);
            return bounds;
        }

        var b = c.bounds;
        b.Expand(0.05f);
        return b;
    }

    private void ComputeHoldOffsets()
    {
        holdRotationOffset = Quaternion.identity;
        holdPositionOffsetLocal = Vector3.zero;

        if (held == null) return;

        var pose = held.GetComponentInParent<HoldPose>();
        if (pose == null || pose.holdAnchor == null)
            return; // No hold pose -> default behavior (match holdPoint exactly)

        // We want the anchor orientation to match the holdPoint orientation.
        // Setting targetRot = holdPoint.rotation * inverse(anchorLocalRotation)
        // makes the anchor's world rotation equal to holdPoint.rotation.
        holdRotationOffset = Quaternion.Inverse(pose.holdAnchor.localRotation);

        // For position, we want anchor world position to land on holdPoint.position.
        // In our formulation: targetPos = holdPoint.position + targetRot * holdPositionOffsetLocal
        // choose holdPositionOffsetLocal = -anchorLocalPos.
        holdPositionOffsetLocal = -pose.holdAnchor.localPosition;
    }

    private void Drop() => ReleaseHeld(applyThrow: false);
    private void Throw() => ReleaseHeld(applyThrow: true);

    private void ReleaseHeld(bool applyThrow)
    {
        if (held == null) return;

        isUsing = false;

        heldActionInstance?.OnUnequip(BuildCtx());
        heldActionInstance = null;

        foreach (var hc in heldColliders)
            foreach (var pc in playerColliders)
                if (hc && pc) Physics.IgnoreCollision(hc, pc, false);

        heldRb.isKinematic = false;
        heldRb.useGravity = true;

        heldRb.linearVelocity = Vector3.zero;
        heldRb.angularVelocity = Vector3.zero;

        if (applyThrow)
        {
            Vector3 dir = playerCamera.transform.forward;
            heldRb.AddForce(dir * throwForce, ForceMode.VelocityChange);
        }

        held = null;
        heldRb = null;
        heldColliders = null;

        holdRotationOffset = Quaternion.identity;
        holdPositionOffsetLocal = Vector3.zero;
    }

    private ItemActionContext BuildCtx()
    {
        return new ItemActionContext(
            playerCamera,
            holdPoint,
            held,
            Time.deltaTime,
            Time.time,
            camYawDeltaThisFrame,
            isUsing
        );
    }
}
