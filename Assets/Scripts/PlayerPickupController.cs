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

    [Tooltip("Higher = snappier follow. Typical 12-30.")]
    [SerializeField] private float followSmooth = 20f;

    [Tooltip("If true, held rotation follows the hold pose. If false, rotation is locked to pickup rotation.")]
    [SerializeField] private bool matchHoldRotation = false;

    [Header("Collision Avoidance (ONE place only)")]
    [Tooltip("Only the layers you want the held item to NOT overlap (usually Ground + Wall). Do NOT include Player or Pickup layers.")]
    [SerializeField] private LayerMask environmentMask = 0;

    [SerializeField, Range(1, 12)] private int penetrationIterations = 6;

    [Tooltip("Extra separation distance after ComputePenetration (meters).")]
    [SerializeField, Min(0f)] private float penetrationSkin = 0.002f;

    [Tooltip("Small extra spacing to reduce visible jitter (meters).")]
    [SerializeField, Min(0f)] private float contactOffset = 0.004f;

    [Tooltip("How fast the persistent collision offset updates.")]
    [SerializeField, Min(1f)] private float collisionOffsetFollow = 25f;

    [Tooltip("How fast the offset relaxes back to zero when no longer colliding.")]
    [SerializeField, Min(0.1f)] private float collisionOffsetRelax = 6f;

    [Tooltip("Max horizontal (XZ) collision offset magnitude.")]
    [SerializeField, Min(0f)] private float maxResolveHorizontal = 0.30f;

    [Tooltip("Max vertical (Y) collision offset magnitude.")]
    [SerializeField, Min(0f)] private float maxResolveVertical = 0.30f;

    [Header("Debug")]
    [SerializeField] private bool debugDrawResolveBoxes = false;

    [Header("UI Prompt")]
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private string pickUpPrompt = "Press E to pick up";

    [Header("Offset Relax (prevents mid-air sticking)")]
    [SerializeField, Min(0.1f)] private float verticalOffsetRelax = 25f;
    [SerializeField, Min(0.1f)] private float horizontalOffsetRelax = 10f;

    private float holdYOffset;      // persistent ground correction only
    private Vector3 holdHOffset;    // persistent wall correction only (XZ)


    private bool promptVisible;

    private Pickupable lookedAt;
    private Pickupable held;

    private Rigidbody heldRb;
    private Collider[] playerColliders;
    private Collider[] heldColliders;

    private ItemActionInstance heldActionInstance;
    private bool isUsing;

    private float lastCamYaw;
    private float camYawDeltaThisFrame;

    // HoldPose offsets
    private Quaternion holdRotationOffset = Quaternion.identity;
    private Vector3 holdPositionOffsetLocal = Vector3.zero;

    // Locked rotation when matchHoldRotation == false
    private Quaternion lockedRotation = Quaternion.identity;

    // Persistent collision correction
    private Vector3 holdCollisionOffset = Vector3.zero;
    private int framesWithoutCorrection = 0;

    // Cached RB settings to restore on drop
    private bool savedKinematic;
    private bool savedUseGravity;
    private RigidbodyInterpolation savedInterpolation;
    private CollisionDetectionMode savedCollisionMode;
    private RigidbodyConstraints savedConstraints;

    // Cache collider local poses relative to held root (more stable than recomputing each frame)
    private struct HeldColPose
    {
        public Collider col;
        public Vector3 localPos;
        public Quaternion localRot;
    }
    private HeldColPose[] heldColPoses;

    // NonAlloc overlap buffer
    private readonly Collider[] overlapBuffer = new Collider[64];

    // Exposed flags for item actions (sounds/feedback).
    // True if collision avoidance pushed the held item this frame.
    public bool IsPushedByWall { get; private set; }
    public bool IsPushedByGround { get; private set; }

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

            // action logic (input driven)
            heldActionInstance?.Tick(BuildCtx(Time.deltaTime));
        }
    }

    private void FixedUpdate()
    {
        if (held != null)
            StepHeld(Time.fixedDeltaTime);
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

        if (heldActionInstance == null)
        {
            Throw();
            return;
        }

        isUsing = true;
        heldActionInstance.OnUseStarted(BuildCtx(Time.deltaTime));
    }

    private void OnUseCanceled(InputAction.CallbackContext ctx)
    {
        if (held == null) return;
        if (heldActionInstance == null) return;

        isUsing = false;
        heldActionInstance.OnUseCanceled(BuildCtx(Time.deltaTime));
    }

    private void Pickup(Pickupable p)
    {
        held = p;
        heldRb = p.Rb;
        holdYOffset = 0f;
        holdHOffset = Vector3.zero;


        if (heldRb == null)
        {
            Debug.LogError("Pickupable has no Rigidbody on the root. Add a Rigidbody to the held object's root.");
            held = null;
            return;
        }

        ComputeHoldOffsets();

        lockedRotation = heldRb.rotation;

        // cache rb state
        savedKinematic = heldRb.isKinematic;
        savedUseGravity = heldRb.useGravity;
        savedInterpolation = heldRb.interpolation;
        savedCollisionMode = heldRb.collisionDetectionMode;
        savedConstraints = heldRb.constraints;

        // while held
        heldRb.isKinematic = true;
        heldRb.useGravity = false;
        heldRb.interpolation = RigidbodyInterpolation.Interpolate;
        heldRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        heldRb.constraints = RigidbodyConstraints.FreezeRotation;

        // Cache colliders + local poses
        heldColliders = held.GetComponentsInChildren<Collider>();
        heldColPoses = new HeldColPose[heldColliders.Length];
        for (int i = 0; i < heldColliders.Length; i++)
        {
            var c = heldColliders[i];
            heldColPoses[i] = new HeldColPose
            {
                col = c,
                localPos = held.transform.InverseTransformPoint(c.transform.position),
                localRot = Quaternion.Inverse(held.transform.rotation) * c.transform.rotation
            };
        }

        // Ignore collisions with player
        foreach (var hc in heldColliders)
            foreach (var pc in playerColliders)
                if (hc && pc) Physics.IgnoreCollision(hc, pc, true);

        holdCollisionOffset = Vector3.zero;
        framesWithoutCorrection = 0;

        EquipHeldAction();
        SetPrompt(false);

        // snap once
        GetHoldTargetPose(Time.fixedDeltaTime, out Vector3 snapPos, out Quaternion snapRot);
        heldRb.position = snapPos;
        heldRb.rotation = matchHoldRotation ? snapRot : lockedRotation;
    }

    private void EquipHeldAction()
    {
        isUsing = false;
        heldActionInstance = null;

        var provider = held.GetComponentInParent<ItemActionProvider>();
        if (provider != null && provider.leftClickAction != null)
        {
            heldActionInstance = provider.leftClickAction.CreateInstance();
            heldActionInstance.OnEquip(BuildCtx(Time.deltaTime));
        }
    }

    private void StepHeld(float dt)
    {
        if (heldRb == null) return;

        // Reset per-step flags.
        IsPushedByWall = false;
        IsPushedByGround = false;

        GetHoldTargetPose(dt, out Vector3 targetPos, out Quaternion targetRot);

        // Apply persistent offsets
        Vector3 desiredPos = targetPos + holdHOffset + new Vector3(0f, holdYOffset, 0f);

        float t = 1f - Mathf.Exp(-followSmooth * dt);
        Vector3 prevPos = heldRb.position;
        Vector3 candidatePos = Vector3.Lerp(prevPos, desiredPos, t);

        Quaternion finalRot = matchHoldRotation ? targetRot : lockedRotation;

        // Solve penetrations
        Vector3 preSolve = candidatePos;
        bool corrected = ResolvePenetrations(ref candidatePos, finalRot);

        // Record what kind of push happened (used by action SOs for sound).
        if (corrected)
        {
            Vector3 delta = candidatePos - preSolve;
            Vector3 deltaH = new Vector3(delta.x, 0f, delta.z);

            // Any upward correction implies ground push.
            if (delta.y > 0.0001f)
                IsPushedByGround = true;

            // Any horizontal correction implies wall push.
            if (deltaH.sqrMagnitude > 1e-8f)
                IsPushedByWall = true;
        }

        // Move (kinematic)
        heldRb.MovePosition(candidatePos);
        heldRb.MoveRotation(finalRot);

        // How much did we get pushed this step?
        Vector3 delta2 = candidatePos - preSolve;

        // Update offsets by COMPONENT so wall contacts don't "pin" the vertical offset.
        float followT = 1f - Mathf.Exp(-collisionOffsetFollow * dt);
        float relaxVT = 1f - Mathf.Exp(-verticalOffsetRelax * dt);
        float relaxHT = 1f - Mathf.Exp(-horizontalOffsetRelax * dt);

        // --- Vertical (ground) offset ---
        // Only keep Y offset if we actually corrected upward this frame.
        if (corrected && delta2.y > 0.0001f)
        {
            holdYOffset = Mathf.Lerp(holdYOffset, holdYOffset + delta2.y, followT);
        }
        else
        {
            holdYOffset = Mathf.Lerp(holdYOffset, 0f, relaxVT);
        }

        // --- Horizontal (wall) offset ---
        Vector3 deltaH2 = new Vector3(delta2.x, 0f, delta2.z);
        if (corrected && deltaH2.sqrMagnitude > 1e-8f)
        {
            holdHOffset = Vector3.Lerp(holdHOffset, holdHOffset + deltaH2, followT);
        }
        else
        {
            holdHOffset = Vector3.Lerp(holdHOffset, Vector3.zero, relaxHT);
        }

        // Clamp offsets
        if (holdHOffset.magnitude > maxResolveHorizontal && holdHOffset.sqrMagnitude > 1e-8f)
            holdHOffset = holdHOffset.normalized * maxResolveHorizontal;

        holdYOffset = Mathf.Clamp(holdYOffset, -maxResolveVertical, maxResolveVertical);
    }

    private void GetHoldTargetPose(float dt, out Vector3 targetPos, out Quaternion targetRot)
    {
        Quaternion baseRot = holdPoint.rotation;
        Vector3 basePos = holdPoint.position;

        targetRot = baseRot * holdRotationOffset;
        targetPos = basePos + (targetRot * holdPositionOffsetLocal);

        // IMPORTANT:
        // The action should ONLY do visual offsets (wiggle), NOT collision solving.
        heldActionInstance?.ModifyHoldTarget(BuildCtx(dt), ref targetPos, ref targetRot);

        if (!matchHoldRotation)
            targetRot = lockedRotation;
    }

    private bool ResolvePenetrations(ref Vector3 rootPos, Quaternion rootRot)
    {
        if (environmentMask == 0) return false;
        if (heldColPoses == null || heldColPoses.Length == 0) return false;

        bool correctedAny = false;

        for (int iter = 0; iter < penetrationIterations; iter++)
        {
            bool anyThisIter = false;
            Vector3 bestMove = Vector3.zero;

            for (int i = 0; i < heldColPoses.Length; i++)
            {
                Collider c = heldColPoses[i].col;
                if (c == null || !c.enabled || c.isTrigger) continue;

                Vector3 colWorldPos = rootPos + (rootRot * heldColPoses[i].localPos);
                Quaternion colWorldRot = rootRot * heldColPoses[i].localRot;

                OrientedBox obb = GetColliderOrientedBoxAt(c, colWorldPos, colWorldRot);

                if (debugDrawResolveBoxes)
                    DrawOrientedBox(obb, Color.yellow, 0f);

                int hitCount = Physics.OverlapBoxNonAlloc(
                    obb.center, obb.halfExtents, overlapBuffer, obb.rotation,
                    environmentMask, QueryTriggerInteraction.Ignore);

                for (int h = 0; h < hitCount; h++)
                {
                    var env = overlapBuffer[h];
                    if (env == null || !env.enabled || env.isTrigger) continue;
                    if (env.transform.IsChildOf(held.transform)) continue;

                    // ignore player colliders even if mask is wrong
                    if (playerColliders != null)
                    {
                        bool isPlayer = false;
                        for (int pc = 0; pc < playerColliders.Length; pc++)
                        {
                            if (env == playerColliders[pc]) { isPlayer = true; break; }
                        }
                        if (isPlayer) continue;
                    }

                    if (!Physics.ComputePenetration(
                        c, colWorldPos, colWorldRot,
                        env, env.transform.position, env.transform.rotation,
                        out Vector3 dir, out float dist))
                        continue;

                    anyThisIter = true;
                    Vector3 move = dir * (dist + penetrationSkin + contactOffset);

                    if (move.sqrMagnitude > bestMove.sqrMagnitude)
                        bestMove = move;
                }
            }

            if (!anyThisIter)
                break;

            correctedAny = true;
            rootPos += bestMove;
        }

        return correctedAny;
    }

    private readonly struct OrientedBox
    {
        public readonly Vector3 center;
        public readonly Vector3 halfExtents;
        public readonly Quaternion rotation;

        public OrientedBox(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            this.center = center;
            this.halfExtents = halfExtents;
            this.rotation = rotation;
        }
    }

    private static OrientedBox GetColliderOrientedBoxAt(Collider c, Vector3 worldPos, Quaternion worldRot)
    {
        Vector3 scale = c.transform.lossyScale;
        Vector3 absScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        if (c is BoxCollider bc)
        {
            Vector3 scaledCenter = Vector3.Scale(bc.center, scale);
            Vector3 scaledSize = Vector3.Scale(bc.size, absScale);
            Vector3 half = scaledSize * 0.5f;
            return new OrientedBox(worldPos + (worldRot * scaledCenter), half, worldRot);
        }

        if (c is SphereCollider sc)
        {
            float max = Mathf.Max(absScale.x, absScale.y, absScale.z);
            float r = sc.radius * max;
            Vector3 scaledCenter = Vector3.Scale(sc.center, scale);
            return new OrientedBox(worldPos + (worldRot * scaledCenter), Vector3.one * r, worldRot);
        }

        if (c is CapsuleCollider cc)
        {
            float radiusScale;
            float heightScale;

            switch (cc.direction)
            {
                case 0: radiusScale = Mathf.Max(absScale.y, absScale.z); heightScale = absScale.x; break; // X
                case 2: radiusScale = Mathf.Max(absScale.x, absScale.y); heightScale = absScale.z; break; // Z
                default: radiusScale = Mathf.Max(absScale.x, absScale.z); heightScale = absScale.y; break; // Y
            }

            float r = cc.radius * radiusScale;
            float h = Mathf.Max(cc.height * heightScale, 2f * r);

            Vector3 half;
            switch (cc.direction)
            {
                case 0: half = new Vector3(h * 0.5f, r, r); break;
                case 2: half = new Vector3(r, r, h * 0.5f); break;
                default: half = new Vector3(r, h * 0.5f, r); break;
            }

            Vector3 scaledCenter = Vector3.Scale(cc.center, scale);
            return new OrientedBox(worldPos + (worldRot * scaledCenter), half, worldRot);
        }

        // IMPORTANT FIX: MeshCollider must use sharedMesh.bounds (local OBB), NOT c.bounds (world AABB).
        if (c is MeshCollider mc && mc.sharedMesh != null)
        {
            Bounds mb = mc.sharedMesh.bounds; // local space
            Vector3 scaledCenter = Vector3.Scale(mb.center, scale);
            Vector3 scaledExtents = Vector3.Scale(mb.extents, absScale);
            return new OrientedBox(worldPos + (worldRot * scaledCenter), scaledExtents, worldRot);
        }

        // Fallback (rare). Uses world AABB (less stable).
        Bounds b = c.bounds;
        return new OrientedBox(b.center, b.extents, Quaternion.identity);
    }

    private static void DrawOrientedBox(OrientedBox b, Color color, float duration)
    {
        Vector3 ex = b.rotation * new Vector3(b.halfExtents.x, 0f, 0f);
        Vector3 ey = b.rotation * new Vector3(0f, b.halfExtents.y, 0f);
        Vector3 ez = b.rotation * new Vector3(0f, 0f, b.halfExtents.z);

        Vector3 c = b.center;

        Vector3 p000 = c - ex - ey - ez;
        Vector3 p001 = c - ex - ey + ez;
        Vector3 p010 = c - ex + ey - ez;
        Vector3 p011 = c - ex + ey + ez;
        Vector3 p100 = c + ex - ey - ez;
        Vector3 p101 = c + ex - ey + ez;
        Vector3 p110 = c + ex + ey - ez;
        Vector3 p111 = c + ex + ey + ez;

        Debug.DrawLine(p000, p001, color, duration);
        Debug.DrawLine(p001, p011, color, duration);
        Debug.DrawLine(p011, p010, color, duration);
        Debug.DrawLine(p010, p000, color, duration);

        Debug.DrawLine(p100, p101, color, duration);
        Debug.DrawLine(p101, p111, color, duration);
        Debug.DrawLine(p111, p110, color, duration);
        Debug.DrawLine(p110, p100, color, duration);

        Debug.DrawLine(p000, p100, color, duration);
        Debug.DrawLine(p001, p101, color, duration);
        Debug.DrawLine(p010, p110, color, duration);
        Debug.DrawLine(p011, p111, color, duration);
    }

    private void ComputeHoldOffsets()
    {
        holdRotationOffset = Quaternion.identity;
        holdPositionOffsetLocal = Vector3.zero;

        if (held == null) return;

        var pose = held.GetComponentInParent<HoldPose>();
        if (pose == null || pose.holdAnchor == null)
            return;

        holdRotationOffset = Quaternion.Inverse(pose.holdAnchor.localRotation);
        holdPositionOffsetLocal = -pose.holdAnchor.localPosition;
    }

    private void Drop() => ReleaseHeld(applyThrow: false);
    private void Throw() => ReleaseHeld(applyThrow: true);

    private void ReleaseHeld(bool applyThrow)
    {
        if (held == null) return;

        isUsing = false;
        holdYOffset = 0f;
        holdHOffset = Vector3.zero;


        heldActionInstance?.OnUnequip(BuildCtx(Time.deltaTime));
        heldActionInstance = null;

        if (heldColliders != null)
        {
            foreach (var hc in heldColliders)
                foreach (var pc in playerColliders)
                    if (hc && pc) Physics.IgnoreCollision(hc, pc, false);
        }

        // Restore RB state
        if (heldRb != null)
        {
            heldRb.isKinematic = savedKinematic;
            heldRb.useGravity = savedUseGravity;
            heldRb.interpolation = savedInterpolation;
            heldRb.collisionDetectionMode = savedCollisionMode;
            heldRb.constraints = savedConstraints;

            heldRb.linearVelocity = Vector3.zero;
            heldRb.angularVelocity = Vector3.zero;

            if (applyThrow)
            {
                Vector3 dir = playerCamera != null ? playerCamera.transform.forward : transform.forward;
                heldRb.AddForce(dir * throwForce, ForceMode.VelocityChange);
            }
        }

        held = null;
        heldRb = null;
        heldColliders = null;
        heldColPoses = null;

        holdRotationOffset = Quaternion.identity;
        holdPositionOffsetLocal = Vector3.zero;
        holdCollisionOffset = Vector3.zero;
        framesWithoutCorrection = 0;
    }

    private ItemActionContext BuildCtx(float dt)
    {
        return new ItemActionContext(
            playerCamera,
            holdPoint,
            held,
            dt,
            Time.time,
            camYawDeltaThisFrame,
            isUsing
        );
    }
}
