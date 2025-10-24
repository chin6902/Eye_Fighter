using System.Collections;
using UnityEngine;

public enum SoundType
{
    Barrier,
    PlayerWalk,
    PlayerHit,
    Jump,
    Clear,
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
    [Tooltip("List of BGM clips you will use (indexable).")]
    [SerializeField] private AudioClip[] bgmClips;
    [Tooltip("If true, BGM will start on Awake using bgmClips[defaultBgmIndex]")]
    [SerializeField] private bool playBgmOnAwake = true;
    [Range(0f, 1f)]
    [SerializeField] private float bgmVolume = 0.5f;
    [Tooltip("Which index to play on Awake (safe-guarded)")]
    [SerializeField] private int defaultBgmIndex = 0;

    [Header("Lifecycle")]
    [Tooltip("If true, SoundManager will survive scene loads")]
    [SerializeField] private bool dontDestroyOnLoad = true;

    private static SoundManager instance;

    // Two audio sources to allow smooth crossfade
    private AudioSource bgmSourceA;
    private AudioSource bgmSourceB;
    private AudioSource activeBgm;
    private AudioSource inactiveBgm;

    // Single SFX source (keeps compatibility)
    private AudioSource sfxSource;

    private Coroutine bgmCrossfadeCoroutine;

    #region Unity Lifecycle

    private void Awake()
    {
        // Singleton setup - destroy duplicates
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (dontDestroyOnLoad)
        {
            DontDestroyOnLoad(gameObject);
        }

        // SFX source uses the existing AudioSource component on this GameObject
        sfxSource = GetComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;

        // Create two BGM AudioSources for crossfading
        bgmSourceA = gameObject.AddComponent<AudioSource>();
        bgmSourceB = gameObject.AddComponent<AudioSource>();

        bgmSourceA.loop = true;
        bgmSourceB.loop = true;
        bgmSourceA.playOnAwake = false;
        bgmSourceB.playOnAwake = false;

        activeBgm = bgmSourceA;
        inactiveBgm = bgmSourceB;

        if (playBgmOnAwake)
        {
            AudioClip startClip = GetBgmClipByIndex(defaultBgmIndex);
            if (startClip != null)
            {
                activeBgm.clip = startClip;
                activeBgm.volume = bgmVolume;
                activeBgm.Play();
            }
        }
    }

    #endregion

    #region SFX

    /// <summary>Play one of the enum-based clips with randomized pitch.</summary>
    public static void PlaySound(SoundType sound, float volume = 1f)
    {
        if (instance == null)
        {
            return;
        }

        int idx = (int)sound;
        if (instance.soundList == null)
        {
            Debug.LogWarning("SoundManager: soundList is null.");
            return;
        }

        if (idx < 0 || idx >= instance.soundList.Length)
        {
            Debug.LogWarning($"SoundManager: SoundType index {idx} out of range.");
            return;
        }

        AudioClip clip = instance.soundList[idx];
        if (clip == null)
        {
            Debug.LogWarning($"SoundManager: soundList[{idx}] is null.");
            return;
        }

        instance.PlayOneShotWithRandomPitch(clip, volume);
    }

    /// <summary>Backwards-compatible wrapper so PlaySFX(SoundType, volume) works.</summary>
    public static void PlaySFX(SoundType sound, float volume = 1f)
    {
        PlaySound(sound, volume);
    }

    /// <summary>Play arbitrary AudioClip as SFX with randomized pitch.</summary>
    public static void PlaySFX(AudioClip clip, float volume = 1f)
    {
        if (instance == null || clip == null)
        {
            return;
        }

        instance.PlayOneShotWithRandomPitch(clip, volume);
    }

    private void PlayOneShotWithRandomPitch(AudioClip clip, float volume)
    {
        if (sfxSource == null || clip == null)
        {
            return;
        }

        float originalPitch = sfxSource.pitch;
        sfxSource.pitch = Random.Range(minPitch, maxPitch);
        sfxSource.PlayOneShot(clip, volume);
        sfxSource.pitch = originalPitch;
    }

    #endregion

    #region BGM Helpers (index access)

    private AudioClip GetBgmClipByIndex(int idx)
    {
        if (bgmClips == null || bgmClips.Length == 0)
        {
            return null;
        }

        if (idx < 0 || idx >= bgmClips.Length)
        {
            Debug.LogWarning($"SoundManager: requested bgm index {idx} out of range. Valid 0..{bgmClips.Length - 1}");
            return null;
        }

        return bgmClips[idx];
    }

    /// <summary>Crossfade to the BGM at the given index.</summary>
    public static void CrossfadeToBGMIndex(int idx, float duration)
    {
        if (instance == null)
        {
            return;
        }

        AudioClip clip = instance.GetBgmClipByIndex(idx);
        instance.StartCrossfade(clip, duration);
    }

    /// <summary>Play the BGM at index immediately or crossfade if fadeTime &gt; 0.</summary>
    public static void PlayBGMIndex(int idx, float fadeTime = 0f)
    {
        if (instance == null)
        {
            return;
        }

        AudioClip clip = instance.GetBgmClipByIndex(idx);
        if (fadeTime <= 0f)
        {
            instance.StopActiveBGMImmediate();
            if (clip != null)
            {
                instance.activeBgm.clip = clip;
                instance.activeBgm.volume = instance.bgmVolume;
                instance.activeBgm.Play();
            }
        }
        else
        {
            CrossfadeToBGMIndex(idx, fadeTime);
        }
    }

    #endregion

    #region BGM Crossfade API (core)

    /// <summary>Crossfade to newClip over duration. If newClip is null, fade out current BGM.</summary>
    public static void CrossfadeToBGM(AudioClip newClip, float duration)
    {
        if (instance == null)
        {
            return;
        }

        instance.StartCrossfade(newClip, duration);
    }

    /// <summary>Immediately play a BGM clip (no crossfade). Optional fadeTime to crossfade from current BGM.</summary>
    public static void PlayBGM(AudioClip clip, float fadeTime = 0f)
    {
        if (instance == null)
        {
            return;
        }

        if (fadeTime <= 0f)
        {
            instance.StopActiveBGMImmediate();
            if (clip != null)
            {
                instance.activeBgm.clip = clip;
                instance.activeBgm.volume = instance.bgmVolume;
                instance.activeBgm.Play();
            }
        }
        else
        {
            CrossfadeToBGM(clip, fadeTime);
        }
    }

    /// <summary>Fade out and stop BGM over duration.</summary>
    public static void StopBGM(float fadeTime = 0f)
    {
        if (instance == null)
        {
            return;
        }

        if (fadeTime <= 0f)
        {
            instance.StopActiveBGMImmediate();
        }
        else
        {
            CrossfadeToBGM(null, fadeTime);
        }
    }

    /// <summary>Change BGM master volume (0–1). This affects target volume for crossfades.</summary>
    public static void SetBGMVolume(float volume)
    {
        if (instance == null)
        {
            return;
        }

        instance.bgmVolume = Mathf.Clamp01(volume);
        if (instance.activeBgm != null && instance.activeBgm.isPlaying)
        {
            instance.activeBgm.volume = instance.bgmVolume;
        }
    }

    private void StartCrossfade(AudioClip newClip, float duration)
    {
        if (bgmCrossfadeCoroutine != null)
        {
            StopCoroutine(bgmCrossfadeCoroutine);
            bgmCrossfadeCoroutine = null;
        }

        bgmCrossfadeCoroutine = StartCoroutine(CrossfadeCoroutine(newClip, Mathf.Max(0.001f, duration)));
    }

    private IEnumerator CrossfadeCoroutine(AudioClip newClip, float duration)
    {
        float elapsed = 0f;
        float startActiveVol = (activeBgm != null && activeBgm.isPlaying) ? activeBgm.volume : 0f;
        float targetVol = bgmVolume;

        // Fade-out only (newClip == null)
        if (newClip == null)
        {
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                if (activeBgm != null)
                {
                    activeBgm.volume = Mathf.Lerp(startActiveVol, 0f, t);
                }
                yield return null;
            }

            if (activeBgm != null)
            {
                activeBgm.Stop();
                activeBgm.clip = null;
                activeBgm.volume = bgmVolume;
            }

            bgmCrossfadeCoroutine = null;
            yield break;
        }

        // If the requested clip is already the active clip and playing, just ramp to target volume
        if (activeBgm != null && activeBgm.clip == newClip && activeBgm.isPlaying)
        {
            elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                activeBgm.volume = Mathf.Lerp(startActiveVol, targetVol, t);
                yield return null;
            }

            activeBgm.volume = targetVol;
            bgmCrossfadeCoroutine = null;
            yield break;
        }

        // Start new clip on inactive source
        if (inactiveBgm == null || activeBgm == null)
        {
            // Safety: fallback to single-source behavior
            if (activeBgm != null)
            {
                activeBgm.clip = newClip;
                activeBgm.volume = targetVol;
                if (!activeBgm.isPlaying)
                {
                    activeBgm.Play();
                }
            }

            bgmCrossfadeCoroutine = null;
            yield break;
        }

        inactiveBgm.clip = newClip;
        inactiveBgm.volume = 0f;
        inactiveBgm.loop = true;
        inactiveBgm.Play();

        // Crossfade loop
        elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            inactiveBgm.volume = Mathf.Lerp(0f, targetVol, t);
            if (activeBgm != null)
            {
                activeBgm.volume = Mathf.Lerp(startActiveVol, 0f, t);
            }
            yield return null;
        }

        // Finalize: stop previous, reset volumes, swap references
        if (activeBgm != null)
        {
            activeBgm.Stop();
            activeBgm.volume = bgmVolume;
        }

        // swap
        AudioSource tmp = activeBgm;
        activeBgm = inactiveBgm;
        inactiveBgm = tmp;

        bgmCrossfadeCoroutine = null;
        yield break;
    }

    private void StopActiveBGMImmediate()
    {
        if (bgmCrossfadeCoroutine != null)
        {
            StopCoroutine(bgmCrossfadeCoroutine);
            bgmCrossfadeCoroutine = null;
        }

        if (activeBgm != null)
        {
            activeBgm.Stop();
            activeBgm.clip = null;
        }

        if (inactiveBgm != null)
        {
            inactiveBgm.Stop();
            inactiveBgm.clip = null;
        }
    }

    public static void PlayDefaultBGM(float fadeTime = 0f)
    {
        if (instance == null) return;
        // Get the clip stored at the serialized defaultBgmIndex and play it (uses existing PlayBGM implementation).
        AudioClip clip = instance.GetBgmClipByIndex(instance.defaultBgmIndex);
        PlayBGM(clip, fadeTime);
    }

    #endregion
}
