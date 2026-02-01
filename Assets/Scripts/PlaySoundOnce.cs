using UnityEngine;

public class PlaySoundOnce : MonoBehaviour
{
    [Header("Audio")]
    [Tooltip("AudioSource to play. If left null, will try GetComponent<AudioSource>() on this GameObject.")]
    public AudioSource audioSource;

    [Tooltip("Random pitch range used for playback.")]
    public Vector2 pitchRange = new Vector2(0.6f, 1.4f);

    [Tooltip("If true, playback begins in Start(). If false, call Play() manually.")]
    public bool playOnStart = true;

    bool _hasPlayed;

    void Reset()
    {
        audioSource = GetComponent<AudioSource>();
    }

    void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        if (playOnStart)
            Play();
    }

    public void Play()
    {
        if (_hasPlayed)
            return;

        if (audioSource == null)
        {
            Debug.LogWarning($"{nameof(PlaySoundOnce)} on '{name}' has no AudioSource assigned.", this);
            Destroy(gameObject);
            return;
        }

        var min = Mathf.Min(pitchRange.x, pitchRange.y);
        var max = Mathf.Max(pitchRange.x, pitchRange.y);
        audioSource.pitch = Random.Range(min, max);

        // If no clip is assigned, there's nothing to play.
        if (audioSource.clip == null)
        {
            Debug.LogWarning($"{nameof(PlaySoundOnce)} on '{name}' has no AudioClip assigned on its AudioSource.", this);
            Destroy(gameObject);
            return;
        }

        _hasPlayed = true;
        audioSource.Play();

        // Account for pitch affecting duration.
        var pitchAbs = Mathf.Max(0.001f, Mathf.Abs(audioSource.pitch));
        var duration = audioSource.clip.length / pitchAbs;

        Destroy(gameObject, duration);
    }
}
