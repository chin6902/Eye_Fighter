using UnityEngine;

public enum SoundType
{
    Barrier,
    PlayerWalk,
    PlayerHit,
    Jump,
}

[RequireComponent(typeof(AudioSource))]
public class SoundManager : MonoBehaviour
{
    [Header("SFX Settings")]
    [SerializeField] private AudioClip[] soundList;
    [Tooltip("Randomize SFX pitch between these values")]
    [SerializeField] private float minPitch = 0.95f;
    [SerializeField] private float maxPitch = 1.05f;

    [Header("BGM Settings")]
    [SerializeField] private AudioClip bgmClip;
    [Tooltip("If true, BGM will start on Awake")]
    [SerializeField] private bool playBgmOnAwake = true;
    [Range(0, 1)][SerializeField] private float bgmVolume = 0.5f;

    private static SoundManager instance;

    // SFX uses this AudioSource (on the same GameObject)
    private AudioSource sfxSource;
    // BGM uses its own AudioSource
    private AudioSource bgmSource;

    private void Awake()
    {
        // singleton setup
        if (instance == null)
        {
            instance = this;
        }

        sfxSource = GetComponent<AudioSource>();

        sfxSource.loop = false;

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.clip = bgmClip;
        bgmSource.loop = true;
        bgmSource.volume = bgmVolume;
        bgmSource.playOnAwake = false;

        if (playBgmOnAwake && bgmClip != null)
            bgmSource.Play();
    }

    #region SFX
    /// <summary>
    /// Play one of the enum-based clips, with random pitch.
    /// </summary>
    public static void PlaySound(SoundType sound, float volume = 1f)
    {
        if (instance == null) return;
        instance.PlayOneShotWithRandomPitch(instance.soundList[(int)sound], volume);
    }

    /// <summary>
    /// Play any AudioClip as SFX, with random pitch.
    /// </summary>
    public static void PlaySFX(AudioClip clip, float volume = 1f)
    {
        if (instance == null || clip == null) return;
        instance.PlayOneShotWithRandomPitch(clip, volume);
    }

    private void PlayOneShotWithRandomPitch(AudioClip clip, float volume)
    {
        // randomize pitch
        float originalPitch = sfxSource.pitch;
        sfxSource.pitch = Random.Range(minPitch, maxPitch);

        sfxSource.PlayOneShot(clip, volume);

        // restore
        sfxSource.pitch = originalPitch;
    }
    #endregion

    #region BGM
    /// <summary>
    /// Start playing (or resume) the BGM clip.
    /// </summary>
    public static void PlayBGM(float fadeTime = 0f)
    {
        if (instance == null || instance.bgmClip == null) return;
        instance.bgmSource.volume = instance.bgmVolume;
        instance.bgmSource.Play();
        // TODO: implement fade-in over fadeTime if desired
    }

    /// <summary>
    /// Stop the BGM immediately (or with fade-out).
    /// </summary>
    public static void StopBGM(float fadeTime = 0f)
    {
        if (instance == null) return;
        if (fadeTime <= 0f)
        {
            instance.bgmSource.Stop();
        }
        else
        {
            instance.StartCoroutine(instance.FadeOutBGM(fadeTime));
        }
    }

    /// <summary>
    /// Change BGM volume (0–1).
    /// </summary>
    public static void SetBGMVolume(float volume)
    {
        if (instance == null) return;
        instance.bgmVolume = Mathf.Clamp01(volume);
        instance.bgmSource.volume = instance.bgmVolume;
    }

    private System.Collections.IEnumerator FadeOutBGM(float duration)
    {
        float startVol = bgmSource.volume;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            bgmSource.volume = Mathf.Lerp(startVol, 0f, t / duration);
            yield return null;
        }
        bgmSource.Stop();
        bgmSource.volume = bgmVolume;
    }
    #endregion
}
