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
    public float touchCleanAmount = 0.1f;

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

    [Header("Held Collision Avoidance")]
    [Tooltip("If the held broom overlaps colliders on this LayerMask, it will be pushed up/down (Y axis) until it no longer overlaps.")]
    public LayerMask environmentMask;

    [Tooltip("Maximum absolute Y offset (meters) that can be applied to resolve overlaps.")]
    public float maxResolveYOffset = 0.35f;

    [Tooltip("How many iterations to try resolving overlaps per frame.")]
    [Range(1, 12)]
    public int resolveIterations = 6;

    [Tooltip("Small extra distance to separate colliders after using ComputePenetration.")]
    public float resolveSkin = 0.002f;

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
            TryCleanTouching(ctx);

            // Brush sound while touching ANYTHING (mask-driven, defaults to Everything).
            bool isTouchingAnything = IsTouchingAnything(ctx);
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

        private bool IsTouchingAnything(ItemActionContext ctx)
        {
            if (ctx.Held == null) return false;
            if (so.brushSoundTouchMask == 0) return false;

            var heldColliders = ctx.Held.GetComponentsInChildren<Collider>();
            if (heldColliders == null || heldColliders.Length == 0) return false;

            for (int i = 0; i < heldColliders.Length; i++)
            {
                var c = heldColliders[i];
                if (c == null) continue;
                if (!c.enabled) continue;
                if (c.isTrigger) continue;

                Bounds b = c.bounds;
                var hits = Physics.OverlapBox(b.center, b.extents, c.transform.rotation, so.brushSoundTouchMask, QueryTriggerInteraction.Collide);

                for (int h = 0; h < hits.Length; h++)
                {
                    var hit = hits[h];
                    if (hit == null) continue;
                    if (!hit.enabled) continue;

                    // Ignore overlaps with the broom itself.
                    if (hit.transform.IsChildOf(ctx.Held.transform))
                        continue;

                    return true;
                }
            }

            return false;
        }

        private bool TryCleanTouching(ItemActionContext ctx)
        {
            if (ctx.Held == null) return false;
            if (so.cleanableTouchMask == 0) return false;

            bool touchedAny = false;

            var heldColliders = ctx.Held.GetComponentsInChildren<Collider>();
            if (heldColliders == null || heldColliders.Length == 0) return false;

            for (int i = 0; i < heldColliders.Length; i++)
            {
                var c = heldColliders[i];
                if (c == null) continue;
                if (!c.enabled) continue;
                if (c.isTrigger) continue;

                Bounds b = c.bounds;
                var hits = Physics.OverlapBox(b.center, b.extents, c.transform.rotation, so.cleanableTouchMask, QueryTriggerInteraction.Collide);
                for (int h = 0; h < hits.Length; h++)
                {
                    var hit = hits[h];
                    if (hit == null) continue;
                    if (!hit.enabled) continue;

                    if (hit.transform.IsChildOf(ctx.Held.transform))
                        continue;

                    touchedAny = true;

                    var cleanable = hit.GetComponentInParent<Cleanable>();
                    if (cleanable != null && so.touchCleanAmount > 0f)
                        cleanable.Clean(so.touchCleanAmount);
                }
            }

            return touchedAny;
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

            ResolveEnvironmentOverlap(ctx, ref targetPos, targetRot);
        }

        private void ResolveEnvironmentOverlap(ItemActionContext ctx, ref Vector3 targetPos, Quaternion targetRot)
        {
            if (so.environmentMask == 0) return;
            if (ctx.Held == null) return;

            var heldColliders = ctx.Held.GetComponentsInChildren<Collider>();
            if (heldColliders == null || heldColliders.Length == 0) return;

            float accumulatedY = 0f;

            for (int iter = 0; iter < so.resolveIterations; iter++)
            {
                bool anyPenetration = false;
                float requiredStepY = 0f;

                for (int i = 0; i < heldColliders.Length; i++)
                {
                    var c = heldColliders[i];
                    if (c == null) continue;
                    if (!c.enabled) continue;
                    if (c.isTrigger) continue;

                    Pose colliderPose = GetColliderWorldPoseAtTarget(c, targetPos, targetRot);

                    Bounds b = GetColliderBoundsAtTarget(c, colliderPose.position, colliderPose.rotation);
                    var envHits = Physics.OverlapBox(b.center, b.extents, colliderPose.rotation, so.environmentMask, QueryTriggerInteraction.Ignore);

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

                            float rel = colliderPose.position.y - env.bounds.center.y;
                            float sign = rel < 0f ? -1f : 1f;

                            float dy = sign * Mathf.Abs(direction.y) * (distance + so.resolveSkin);

                            if (Mathf.Abs(dy) < 1e-5f)
                                dy = sign * (distance + so.resolveSkin);

                            if (Mathf.Abs(dy) > Mathf.Abs(requiredStepY))
                                requiredStepY = dy;
                        }
                    }
                }

                if (!anyPenetration)
                    break;

                float nextAccum = accumulatedY + requiredStepY;
                if (Mathf.Abs(nextAccum) > so.maxResolveYOffset)
                {
                    requiredStepY = Mathf.Sign(nextAccum) * so.maxResolveYOffset - accumulatedY;
                    targetPos.y += requiredStepY;
                    break;
                }

                accumulatedY = nextAccum;
                targetPos.y += requiredStepY;
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

        private static Bounds GetColliderBoundsAtTarget(Collider c, Vector3 worldPos, Quaternion worldRot)
        {
            if (c is BoxCollider bc)
            {
                Vector3 size = Vector3.Scale(bc.size, bc.transform.lossyScale);
                var bounds = new Bounds(worldPos + (worldRot * Vector3.Scale(bc.center, bc.transform.lossyScale)), size);
                bounds.Expand(0.01f);
                return bounds;
            }

            if (c is SphereCollider sc)
            {
                float maxScale = Mathf.Max(Mathf.Abs(sc.transform.lossyScale.x), Mathf.Abs(sc.transform.lossyScale.y), Mathf.Abs(sc.transform.lossyScale.z));
                float r = sc.radius * maxScale;
                var bounds = new Bounds(worldPos + (worldRot * Vector3.Scale(sc.center, sc.transform.lossyScale)), Vector3.one * (2f * r));
                bounds.Expand(0.01f);
                return bounds;
            }

            if (c is CapsuleCollider cc)
            {
                Vector3 s = cc.transform.lossyScale;
                float radiusScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
                float r = cc.radius * radiusScale;
                float h = Mathf.Abs(cc.height * s.y);

                float extentY = Mathf.Max(h * 0.5f, r);
                float extentXZ = r;

                var bounds = new Bounds(worldPos + (worldRot * Vector3.Scale(cc.center, s)), new Vector3(extentXZ * 2f, extentY * 2f, extentXZ * 2f));
                bounds.Expand(0.02f);
                return bounds;
            }

            var b = c.bounds;
            b.Expand(0.05f);
            return b;
        }
    }
}
