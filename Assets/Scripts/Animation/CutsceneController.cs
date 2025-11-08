using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[DisallowMultipleComponent]
public class CutsceneController : MonoBehaviour
{
    [Header("PlayableDirector (Timeline)")]
    public PlayableDirector director;

    [Header("Exposed reference names used in the Timeline")]
    public string exposedSwordRefName = "Sword";
    public string exposedCircleRefName = "MagicCircle";
    public string exposedExplosionRefName = "GroundExplosion";

    [Header("Placeholders (assign the GameObjects you used in the Timeline bindings)")]
    public GameObject swordPlaceholder;
    public GameObject magicCirclePlaceholder;
    public GameObject explosionPlaceholder;

    [Header("Prefabs (instantiated as children of the boss during cutscene)")]
    public GameObject swordPrefab;
    public GameObject magicCirclePrefab;
    public GameObject groundExplosionPrefab;

    [Header("Default placement offsets (local to boss)")]
    public Vector3 swordLocalOffset = new Vector3(0f, 6f, 0f);
    public Vector3 circleLocalOffset = Vector3.zero;
    public Vector3 explosionLocalOffset = Vector3.zero;

    // runtime
    private BossHealth bossRef;
    private GameObject instantiatedSword;
    private GameObject instantiatedCircle;
    private GameObject instantiatedExplosion;
    private Action onCompleteCallback;

    private void Awake()
    {
        if (director == null)
            Debug.LogWarning("[CutsceneController] PlayableDirector is not assigned.");
    }

    public void PlayCutsceneForBoss(BossHealth boss, Action onComplete = null)
    {
        if (director == null)
        {
            Debug.LogError("[CutsceneController] PlayableDirector not assigned. Cannot play cutscene.");
            onComplete?.Invoke();
            return;
        }

        bossRef = boss;
        onCompleteCallback = onComplete;

        InstantiateAndBindPrefabs();

        director.stopped += OnDirectorStopped;

        director.time = 0;
        director.Evaluate();
        director.Play();
    }

    private void InstantiateAndBindPrefabs()
    {
        CleanupInstances();

        // Sword
        if (swordPrefab != null && bossRef != null)
        {
            instantiatedSword = Instantiate(swordPrefab, bossRef.transform);
            instantiatedSword.transform.localPosition = swordLocalOffset;
            instantiatedSword.transform.localRotation = Quaternion.identity;
            instantiatedSword.SetActive(false);

            director.SetReferenceValue(new PropertyName(exposedSwordRefName), instantiatedSword);
            RebindTracksFromPlaceholderToInstance(swordPlaceholder, instantiatedSword);
        }

        // Magic Circle
        if (magicCirclePrefab != null && bossRef != null)
        {
            instantiatedCircle = Instantiate(magicCirclePrefab, bossRef.transform);
            instantiatedCircle.transform.localPosition = circleLocalOffset;
            instantiatedCircle.transform.localRotation = Quaternion.identity;
            instantiatedCircle.SetActive(false);

            director.SetReferenceValue(new PropertyName(exposedCircleRefName), instantiatedCircle);
            RebindTracksFromPlaceholderToInstance(magicCirclePlaceholder, instantiatedCircle);
        }

        // Ground Explosion
        if (groundExplosionPrefab != null && bossRef != null)
        {
            instantiatedExplosion = Instantiate(groundExplosionPrefab, bossRef.transform);
            instantiatedExplosion.transform.localPosition = explosionLocalOffset;
            instantiatedExplosion.transform.localRotation = Quaternion.identity;
            instantiatedExplosion.SetActive(false);

            director.SetReferenceValue(new PropertyName(exposedExplosionRefName), instantiatedExplosion);
            RebindTracksFromPlaceholderToInstance(explosionPlaceholder, instantiatedExplosion);
        }

        director.Evaluate();
    }

    private void RebindTracksFromPlaceholderToInstance(GameObject placeholder, GameObject instance)
    {
        if (director == null || placeholder == null || instance == null) return;
        var timeline = director.playableAsset as TimelineAsset;
        if (timeline == null) return;

        foreach (var track in timeline.GetOutputTracks())
        {
            var bound = director.GetGenericBinding(track) as UnityEngine.Object;
            if (bound == placeholder)
            {
                director.SetGenericBinding(track, instance);
            }
        }
    }

    public void OnTimelineSignal()
    {
        if (bossRef == null)
        {
            Debug.LogWarning("[CutsceneController] OnTimelineSignal called but bossRef is null.");
            return;
        }

        try
        {
            bossRef.FinalizeDeath();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CutsceneController] Exception while calling bossRef.FinalizeDeath(): " + ex);
        }
    }

    private void OnDirectorStopped(PlayableDirector d)
    {
        director.stopped -= OnDirectorStopped;

        try { onCompleteCallback?.Invoke(); }
        catch (Exception ex) { Debug.LogWarning("[CutsceneController] onCompleteCallback threw: " + ex); }

        CleanupInstances();
        bossRef = null;
        onCompleteCallback = null;
    }

    private void CleanupInstances()
    {
        if (instantiatedSword != null) { try { Destroy(instantiatedSword); } catch { } instantiatedSword = null; }
        if (instantiatedCircle != null) { try { Destroy(instantiatedCircle); } catch { } instantiatedCircle = null; }
        if (instantiatedExplosion != null) { try { Destroy(instantiatedExplosion); } catch { } instantiatedExplosion = null; }
    }

    public void StopAndCleanup()
    {
        try { if (director != null && director.state == PlayState.Playing) director.Stop(); } catch { }
        CleanupInstances();
        bossRef = null;
        onCompleteCallback = null;
    }
}
