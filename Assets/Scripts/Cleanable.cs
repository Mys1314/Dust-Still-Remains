using UnityEditor;
using UnityEngine;

public class Cleanable : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float dirt = 1f;
    [SerializeField] private bool disableWhenClean = true;
    public GameObject cleanSoundPrefab;
    public void Clean(float amount)
    {
        dirt = Mathf.Clamp01(dirt - amount);

        if (disableWhenClean && dirt <= 0f)
        {
            //destroy the object when fully clean
            Instantiate(cleanSoundPrefab, transform.position, Quaternion.identity);
            Destroy(gameObject);
        }
    }
}
