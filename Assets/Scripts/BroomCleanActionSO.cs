using UnityEngine;

[CreateAssetMenu(menuName = "Items/Actions/Broom Clean", fileName = "BroomCleanAction")]
public class BroomCleanActionSO : ItemActionSO
{
    [Header("Cleaning")]
    public float cleanRange = 2.0f;
    public LayerMask cleanMask = ~0;
    [Range(0.01f, 1f)] public float cleanAmountPerWiggle = 0.2f;

    [Tooltip("If the broom collider is currently touching objects on this mask, they will be cleaned.")]
    public LayerMask cleanableTouchMask;

    [Tooltip("Clean amount applied per touch check.")]
    public float touchCleanAmount = 0.3f;

    [Header("Touch Cleaning Rate")]
    [Tooltip("Seconds of continuous touch required before each touch-clean is applied (first clean is delayed too).")]
    [Min(0.05f)]
    public float touchCleanIntervalSeconds = 0.5f;

    [Header("Brush Sound (while touching)")]
    [Tooltip("Prefab to spawn while the broom is touching something. Typically a GameObject that has an AudioSource + PlaySoundOnce.")]
    public GameObject brushSoundPrefab;

    [Tooltip("LayerMask used to detect 'touching' for brush sounds. Use Everything by default.")]
    public LayerMask brushSoundTouchMask = ~0;

    [Tooltip("Seconds between brush sound spawns while touching.")]
    [Min(0.05f)]
    public float brushSoundIntervalSeconds = 1f;

    [Header("Wiggle Detection (mouse left/right while holding Use)")]
    [Tooltip("Ignore tiny camera yaw changes smaller than this (degrees per frame).")]
    public float minYawDeltaDegrees = 0.6f;

    [Tooltip("How many direction flips (L->R or R->L) required to trigger a clean.")]
    public int flipsNeeded = 4;

    [Tooltip("If you take longer than this between flips, the wiggle count resets.")]
    public float wiggleWindowSeconds = 0.8f;

    [Tooltip("Cooldown after a clean trigger (prevents spam).")]
    public float cleanCooldownSeconds = 0.1f;

    [Header("Held Visual Wiggle")]
    public float wiggleSpeed = 12f;
    public float wiggleRollDegrees = 18f;
    public float wiggleSideMeters = 0.05f;

    [Header("Held Collision Avoidance (Simple)")]
    [Tooltip("Step size used when pushing out of overlaps (meters per iteration).")]
    [Min(0.0005f)]
    public float resolveStep = 0.01f;

    [Tooltip("Broad LayerMask for environmental collision queries. Only layers in Wall/Ground masks will actually be resolved.")]
    public LayerMask environmentMask = ~0;

    [Tooltip("Layers treated as walls: penetration will be resolved by pushing along held local Z.")]
    public LayerMask wallLayers;

    [Tooltip("Layers treated as ground: penetration will be resolved by pushing along world Y.")]
    public LayerMask groundLayers;

    [Tooltip("Maximum absolute Y offset (meters) that can be applied to resolve overlaps with ground.")]
    public float maxResolveYOffset = 0.35f;

    [Tooltip("Maximum absolute Z offset (meters) that can be applied to resolve overlaps with walls.")]
    public float maxResolveZOffset = 0.35f;

    [Tooltip("How many iterations to try resolving overlaps per frame.")]
    [Range(1, 12)]
    public int resolveIterations = 6;

    [Tooltip("Small extra distance to separate colliders after using ComputePenetration.")]
    public float resolveSkin = 0.002f;

    [Header("Debug")]
    [Tooltip("If enabled, draws overlap boxes used for environmental resolution in the Scene view.")]
    public bool debugDrawResolveBoxes = false;

    [Tooltip("Extra separation distance (meters) added when resolving overlaps. Helps prevent visible dipping/oscillation on shallow contacts.")]
    [Min(0f)]
    public float contactOffset = 0.01f;

    public override ItemActionInstance CreateInstance() => new Instance(this);

    private class Instance : ItemActionInstance
    {
        private readonly BroomCleanActionSO so;

        private int currentDir; // -1 or +1
        private int flips;
        private float timeSinceFlip;
        private float cooldown;

        private float brushSoundTimer;

        public Instance(BroomCleanActionSO so) => this.so = so;

        public override void OnUseStarted(ItemActionContext ctx)
        {
            currentDir = 0;
            flips = 0;
            timeSinceFlip = 0f;
            // keep cooldown as-is

            brushSoundTimer = 0f;
        }

        public override void OnUseCanceled(ItemActionContext ctx)
        {
            currentDir = 0;
            flips = 0;
            timeSinceFlip = 0f;

            brushSoundTimer = 0f;
        }

        public override void Tick(ItemActionContext ctx)
        {
            if (!ctx.IsUsing) return;

            cooldown -= ctx.DeltaTime;

            // Cleaning to anything the broom is currently touching (mask-driven).
            // Rate limiting is handled per object by Cleanable.
            ApplyCleanToTouching(ctx);

            // Brush sound while pushed by collision avoidance (wall or ground).
            bool isTouchingAnything = IsPushedByEnvironment(ctx);
            TickBrushSound(ctx, isTouchingAnything);

            timeSinceFlip += ctx.DeltaTime;
            if (timeSinceFlip > so.wiggleWindowSeconds)
            {
                currentDir = 0;
                flips = 0;
                timeSinceFlip = 0f;
            }

            float dyaw = ctx.CameraYawDelta;
            if (Mathf.Abs(dyaw) < so.minYawDeltaDegrees) return;

            int sign = dyaw > 0f ? 1 : -1;

            if (currentDir == 0)
            {
                currentDir = sign;
                return;
            }

            if (sign != currentDir)
            {
                currentDir = sign;
                flips++;
                timeSinceFlip = 0f;

                if (flips >= so.flipsNeeded && cooldown <= 0f)
                {
                    flips = 0;
                    cooldown = so.cleanCooldownSeconds;
                    TryClean(ctx);
                }
            }
        }

        private void TickBrushSound(ItemActionContext ctx, bool isTouching)
        {
            if (!isTouching)
            {
                brushSoundTimer = 0f;
                return;
            }

            if (so.brushSoundPrefab == null) return;
            if (so.brushSoundIntervalSeconds <= 0f) return;

            brushSoundTimer += ctx.DeltaTime;
            if (brushSoundTimer < so.brushSoundIntervalSeconds)
                return;

            brushSoundTimer = 0f;

            Vector3 pos = ctx.Held != null ? ctx.Held.transform.position : ctx.Camera.transform.position;
            Quaternion rot = ctx.Held != null ? ctx.Held.transform.rotation : Quaternion.identity;
            Object.Instantiate(so.brushSoundPrefab, pos, rot);
        }

        private static bool IsPushedByEnvironment(ItemActionContext ctx)
        {
            if (ctx.Held == null) return false;

            var controller = Object.FindFirstObjectByType<PlayerPickupController>();
            if (controller == null) return false;

            // If the held item is being pushed, it's effectively "touching" something.
            return controller.IsPushedByWall || controller.IsPushedByGround;
        }

        private bool IsTouchingCleanable(ItemActionContext ctx)
        {
            if (ctx.Held == null) return false;
            if (so.cleanableTouchMask == 0) return false;

            var heldColliders = ctx.Held.GetComponentsInChildren<Collider>();
            if (heldColliders == null || heldColliders.Length == 0) return false;

            // Use current held pose.
            Vector3 rootPos = ctx.Held.transform.position;
            Quaternion rootRot = ctx.Held.transform.rotation;

            for (int i = 0; i < heldColliders.Length; i++)
            {
                var c = heldColliders[i];
                if (c == null) continue;
                if (!c.enabled) continue;
                if (c.isTrigger) continue;

                Pose colliderPose = GetColliderWorldPoseAtTarget(c, rootPos, rootRot);
                OrientedBox obb = GetColliderOrientedBoxAt(c, colliderPose.position, colliderPose.rotation);

                var hits = Physics.OverlapBox(obb.center, obb.halfExtents, obb.rotation, so.cleanableTouchMask, QueryTriggerInteraction.Collide);
                for (int h = 0; h < hits.Length; h++)
                {
                    var hit = hits[h];
                    if (hit == null) continue;
                    if (!hit.enabled) continue;

                    if (hit.transform.IsChildOf(ctx.Held.transform))
                        continue;

                    var cleanable = hit.GetComponentInParent<Cleanable>();
                    if (cleanable != null)
                        return true;
                }
            }

            return false;
        }

        private void ApplyCleanToTouching(ItemActionContext ctx)
        {
            if (ctx.Held == null) return;
            if (so.cleanableTouchMask == 0) return;
            if (so.touchCleanAmount <= 0f) return;

            var heldColliders = ctx.Held.GetComponentsInChildren<Collider>();
            if (heldColliders == null || heldColliders.Length == 0) return;

            Vector3 rootPos = ctx.Held.transform.position;
            Quaternion rootRot = ctx.Held.transform.rotation;

            for (int i = 0; i < heldColliders.Length; i++)
            {
                var c = heldColliders[i];
                if (c == null) continue;
                if (!c.enabled) continue;
                if (c.isTrigger) continue;

                Pose colliderPose = GetColliderWorldPoseAtTarget(c, rootPos, rootRot);
                OrientedBox obb = GetColliderOrientedBoxAt(c, colliderPose.position, colliderPose.rotation);

                var hits = Physics.OverlapBox(obb.center, obb.halfExtents, obb.rotation, so.cleanableTouchMask, QueryTriggerInteraction.Collide);
                for (int h = 0; h < hits.Length; h++)
                {
                    var hit = hits[h];
                    if (hit == null) continue;
                    if (!hit.enabled) continue;

                    if (hit.transform.IsChildOf(ctx.Held.transform))
                        continue;

                    var cleanable = hit.GetComponentInParent<Cleanable>();
                    if (cleanable != null)
                        cleanable.Clean(so.touchCleanAmount);
                }
            }
        }

        private void TryClean(ItemActionContext ctx)
        {
            var ray = new Ray(ctx.Camera.transform.position, ctx.Camera.transform.forward);

            if (Physics.Raycast(ray, out var hit, so.cleanRange, so.cleanMask, QueryTriggerInteraction.Ignore))
            {
                var cleanable = hit.collider.GetComponentInParent<Cleanable>();
                if (cleanable != null)
                    cleanable.Clean(so.cleanAmountPerWiggle);
            }
        }

        public override void ModifyHoldTarget(ItemActionContext ctx, ref Vector3 targetPos, ref Quaternion targetRot)
        {
            if (!ctx.IsUsing) return;

            float s = Mathf.Sin(ctx.Time * so.wiggleSpeed);

            targetPos += ctx.HoldPoint.right * (s * so.wiggleSideMeters);
            // Roll (Z axis) instead of yaw (Y axis) for broom visual motion.
            targetRot = targetRot * Quaternion.Euler(0f, 0f, s * so.wiggleRollDegrees);
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

        private static Pose GetColliderWorldPoseAtTarget(Collider c, Vector3 targetRootPos, Quaternion targetRootRot)
        {
            Transform t = c.transform;

            var pickup = c.GetComponentInParent<Pickupable>();
            Transform root = pickup != null ? pickup.transform : t.root;

            Vector3 localPos = root.InverseTransformPoint(t.position);
            Quaternion localRot = Quaternion.Inverse(root.rotation) * t.rotation;

            return new Pose(
                targetRootPos + (targetRootRot * localPos),
                targetRootRot * localRot);
        }

        private static OrientedBox GetColliderOrientedBoxAt(Collider c, Vector3 worldPos, Quaternion worldRot)
        {
            Vector3 scale = GetApproxTargetScale(c);

            if (c is BoxCollider bc)
            {
                Vector3 scaledCenter = Vector3.Scale(bc.center, scale);
                Vector3 scaledSize = Vector3.Scale(bc.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                Vector3 half = scaledSize * 0.5f;

                return new OrientedBox(worldPos + (worldRot * scaledCenter), half, worldRot);
            }

            if (c is SphereCollider sc)
            {
                float max = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                float r = sc.radius * max;
                Vector3 scaledCenter = Vector3.Scale(sc.center, scale);

                return new OrientedBox(worldPos + (worldRot * scaledCenter), Vector3.one * r, worldRot);
            }

            if (c is CapsuleCollider cc)
            {
                float radiusScale;
                float heightScale;
                Vector3 abs = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

                switch (cc.direction)
                {
                    case 0: // X
                        radiusScale = Mathf.Max(abs.y, abs.z);
                        heightScale = abs.x;
                        break;
                    case 2: // Z
                        radiusScale = Mathf.Max(abs.x, abs.y);
                        heightScale = abs.z;
                        break;
                    default: // Y
                        radiusScale = Mathf.Max(abs.x, abs.z);
                        heightScale = abs.y;
                        break;
                }

                float r = cc.radius * radiusScale;
                float h = Mathf.Max(cc.height * heightScale, 2f * r);

                Vector3 half;
                switch (cc.direction)
                {
                    case 0:
                        half = new Vector3(h * 0.5f, r, r);
                        break;
                    case 2:
                        half = new Vector3(r, r, h * 0.5f);
                        break;
                    default:
                        half = new Vector3(r, h * 0.5f, r);
                        break;
                }

                Vector3 scaledCenter = Vector3.Scale(cc.center, scale);
                return new OrientedBox(worldPos + (worldRot * scaledCenter), half, worldRot);
            }

            Bounds b = c.bounds;
            return new OrientedBox(b.center, b.extents, c.transform.rotation);
        }

        private static Vector3 GetApproxTargetScale(Collider c)
        {
            // For held objects we assume scale stays consistent; use current transform scale.
            // (This avoids incorrect math from earlier target-rotation scale estimation.)
            return c.transform.lossyScale;
        }
    }
}
