using UnityEngine;

[CreateAssetMenu(menuName = "Items/Actions/Sponge Clean", fileName = "SpongeCleanAction")]
public class SpongeCleanActionSO : ItemActionSO
{
    [Header("Cleaning")]
    public float cleanRange = 2.0f;
    public LayerMask cleanMask = ~0;
    [Range(0.01f, 1f)] public float cleanAmountPerWiggle = 0.2f;

    [Tooltip("If the sponge colliders are currently touching objects on this mask, they will be cleaned.")]
    public LayerMask cleanableTouchMask;

    [Tooltip("Clean amount applied per touch check (same behavior as the broom file).")]
    public float touchCleanAmount = 0.1f;

    [Header("Scrub Sound (while touching)")]
    [Tooltip("Prefab to spawn while the sponge is touching something. Typically a GameObject that has an AudioSource + PlaySoundOnce.")]
    public GameObject scrubSoundPrefab;

    [Tooltip("LayerMask used to detect 'touching' for scrub sounds. Use Everything by default.")]
    public LayerMask scrubSoundTouchMask = ~0;

    [Tooltip("Seconds between scrub sound spawns while touching.")]
    [Min(0.05f)]
    public float scrubSoundIntervalSeconds = 1f;

    [Header("Wiggle Detection (mouse left/right while holding Use)")]
    [Tooltip("Ignore tiny camera yaw changes smaller than this (degrees per frame).")]
    public float minYawDeltaDegrees = 0.6f;

    [Tooltip("How many direction flips (L->R or R->L) required to trigger a clean.")]
    public int flipsNeeded = 4;

    [Tooltip("If you take longer than this between flips, the wiggle count resets.")]
    public float wiggleWindowSeconds = 0.8f;

    [Tooltip("Cooldown after a clean trigger (prevents spam).")]
    public float cleanCooldownSeconds = 0.1f;

    [Header("Held Visual Scrub (camera-space X/Y)")]
    [Tooltip("Overall scrub animation speed.")]
    public float scrubSpeed = 10f;

    [Tooltip("Left/right scrub distance (camera right).")]
    public float scrubXMeters = 0.06f;

    [Tooltip("Up/down scrub distance (camera up).")]
    public float scrubYMeters = 0.04f;

    [Tooltip("Optional twist while scrubbing (degrees).")]
    public float scrubTwistDegrees = 10f;

    public override ItemActionInstance CreateInstance() => new Instance(this);

    private class Instance : ItemActionInstance
    {
        private readonly SpongeCleanActionSO so;

        private int currentDir; // -1 or +1
        private int flips;
        private float timeSinceFlip;
        private float cooldown;

        private float scrubSoundTimer;

        public Instance(SpongeCleanActionSO so) => this.so = so;

        public override void OnUseStarted(ItemActionContext ctx)
        {
            currentDir = 0;
            flips = 0;
            timeSinceFlip = 0f;
            scrubSoundTimer = 0f;
        }

        public override void OnUseCanceled(ItemActionContext ctx)
        {
            currentDir = 0;
            flips = 0;
            timeSinceFlip = 0f;
            scrubSoundTimer = 0f;
        }

        public override void Tick(ItemActionContext ctx)
        {
            if (!ctx.IsUsing) return;

            cooldown -= ctx.DeltaTime;

            // Clean anything the sponge is currently touching (mask-driven).
            TryCleanTouching(ctx);

            // Scrub sound while touching ANYTHING (mask-driven, defaults to Everything).
            bool isTouchingAnything = IsTouchingAnything(ctx);
            TickScrubSound(ctx, isTouchingAnything);

            // Optional: same wiggle-trigger clean behavior as the broom file (yaw flips).
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
                    TryCleanRay(ctx);
                }
            }
        }

        private void TickScrubSound(ItemActionContext ctx, bool isTouching)
        {
            if (!isTouching)
            {
                scrubSoundTimer = 0f;
                return;
            }

            if (so.scrubSoundPrefab == null) return;
            if (so.scrubSoundIntervalSeconds <= 0f) return;

            scrubSoundTimer += ctx.DeltaTime;
            if (scrubSoundTimer < so.scrubSoundIntervalSeconds)
                return;

            scrubSoundTimer = 0f;

            Vector3 pos = ctx.Held != null ? ctx.Held.transform.position : ctx.Camera.transform.position;
            Quaternion rot = ctx.Held != null ? ctx.Held.transform.rotation : Quaternion.identity;
            Object.Instantiate(so.scrubSoundPrefab, pos, rot);
        }

        private bool IsTouchingAnything(ItemActionContext ctx)
        {
            if (ctx.Held == null) return false;
            if (so.scrubSoundTouchMask == 0) return false;

            var heldColliders = ctx.Held.GetComponentsInChildren<Collider>();
            if (heldColliders == null || heldColliders.Length == 0) return false;

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

                var hits = Physics.OverlapBox(
                    obb.center,
                    obb.halfExtents,
                    obb.rotation,
                    so.scrubSoundTouchMask,
                    QueryTriggerInteraction.Collide);

                for (int h = 0; h < hits.Length; h++)
                {
                    var hit = hits[h];
                    if (hit == null) continue;
                    if (!hit.enabled) continue;

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

                var hits = Physics.OverlapBox(
                    obb.center,
                    obb.halfExtents,
                    obb.rotation,
                    so.cleanableTouchMask,
                    QueryTriggerInteraction.Collide);

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

        private void TryCleanRay(ItemActionContext ctx)
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

            // Scrub in camera-space: X = camera.right, Y = camera.up
            // This matches “cleaning a perpendicular surface” (wall normal ~ camera.forward).
            Transform cam = ctx.Camera != null ? ctx.Camera.transform : null;

            float t = ctx.Time * so.scrubSpeed;

            // Elliptical scrub (left/right + up/down together)
            float x = Mathf.Sin(t) * so.scrubXMeters;
            float y = Mathf.Cos(t) * so.scrubYMeters;

            if (cam != null)
            {
                targetPos += cam.right * x;
                targetPos += cam.up * y;

                if (so.scrubTwistDegrees != 0f)
                {
                    float twist = Mathf.Sin(t) * so.scrubTwistDegrees;
                    targetRot = Quaternion.AngleAxis(twist, cam.forward) * targetRot;
                }
            }
            else
            {
                // Fallback: use hold point axes if camera isn't available
                targetPos += ctx.HoldPoint.right * x;
                targetPos += ctx.HoldPoint.up * y;
            }
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
            Vector3 scale = c.transform.lossyScale;

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
    }
}
