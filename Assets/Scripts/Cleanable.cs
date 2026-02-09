using UnityEngine;

public class Cleanable : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float dirt = 1f;
    [SerializeField] private bool disableWhenClean = true;
    public GameObject cleanSoundPrefab;

    [Header("Cleaning Cooldown")]
    [Tooltip("Minimum seconds between successful Clean() applications to this object.")]
    [SerializeField, Min(0f)] private float cleanCooldownSeconds = 0.5f;

    [Header("Visuals")]
    [Tooltip("Renderers whose material alpha will be driven by dirt (1 = visible, 0 = transparent). If empty, uses all child Renderers.")]
    [SerializeField] private Renderer[] targetRenderers;

    private float nextAllowedCleanTime;

    private MaterialPropertyBlock mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");

    private void Awake()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>(true);

        mpb = new MaterialPropertyBlock();
    }

    private void Start()
    {
        ApplyDirtToTransparency();
    }

    private void OnValidate()
    {
        dirt = Mathf.Clamp01(dirt);
        if (!Application.isPlaying)
        {
            if (targetRenderers == null || targetRenderers.Length == 0)
                targetRenderers = GetComponentsInChildren<Renderer>(true);

            if (mpb == null)
                mpb = new MaterialPropertyBlock();

            ApplyDirtToTransparency();
        }
    }

    public void Clean(float amount)
    {
        if (cleanCooldownSeconds > 0f && Time.time < nextAllowedCleanTime)
            return;

        nextAllowedCleanTime = Time.time + cleanCooldownSeconds;

        Debug.Log($"Cleaning {gameObject.name} by {amount}. Current dirt: {dirt}");
        dirt = Mathf.Clamp01(dirt - amount);

        ApplyDirtToTransparency();

        if (disableWhenClean && dirt <= 0f)
        {
            //destroy the object when fully clean
            Instantiate(cleanSoundPrefab, transform.position, Quaternion.identity);
            Destroy(gameObject);
        }
    }

    private void ApplyDirtToTransparency()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            return;

        float a = dirt;

        for (int r = 0; r < targetRenderers.Length; r++)
        {
            var rend = targetRenderers[r];
            if (rend == null) continue;

            rend.GetPropertyBlock(mpb);

            // Prefer HDRP Lit: _BaseColor, but also support _Color and _UnlitColor.
            var sharedMat = rend.sharedMaterial;
            if (sharedMat != null && sharedMat.HasProperty(BaseColorId))
            {
                Color c = sharedMat.GetColor(BaseColorId);
                c.a = a;
                mpb.SetColor(BaseColorId, c);
            }
            else if (sharedMat != null && sharedMat.HasProperty(ColorId))
            {
                Color c = sharedMat.GetColor(ColorId);
                c.a = a;
                mpb.SetColor(ColorId, c);
            }
            else if (sharedMat != null && sharedMat.HasProperty(UnlitColorId))
            {
                Color c = sharedMat.GetColor(UnlitColorId);
                c.a = a;
                mpb.SetColor(UnlitColorId, c);
            }

            rend.SetPropertyBlock(mpb);
        }
    }
}
