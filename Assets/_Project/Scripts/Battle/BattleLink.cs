using System.Collections;
using System.Collections.Generic;
using DistantLands.Cozy;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Fights the trading run's battles on the map: when the timeline reaches a battle, it moves the
/// <see cref="BattleView"/> to that battle's site, gives it the army held and the enemy's, and turns the battle
/// camera on (the brain blends down from the map view). When the battle has ended and the result has had a moment,
/// it clears the field, hands the survivors back to the run and turns the camera off again. COZY's birdsong and
/// weather sounds fade down while the battle is on.
/// </summary>
public class BattleLink : MonoBehaviour
{
    [SerializeField] TradeRun run;
    [SerializeField] BattleView view;
    [Tooltip("Turned on for the battle; the Cinemachine brain blends to it from the map view and back.")]
    [SerializeField] CinemachineCamera battleCamera;
    [Tooltip("Where battles are fought, in turn: battle 1 at the first, and round again past the last. The player's " +
             "army comes from the site's -X side, the enemy holds its +X side, and the camera looks along +Z.")]
    [SerializeField] List<Transform> sites = new List<Transform>();
    [Tooltip("Real seconds between the battle ending and the map coming back, so the last blow can land.")]
    [SerializeField, Min(0f)] float resultHold = 3f;
    [Tooltip("How loud COZY's ambience and weather sounds stay during a battle, as a share of their own volume.")]
    [SerializeField, Range(0f, 1f)] float ambienceInBattle = 0.15f;
    [Tooltip("Real seconds the ambience takes to fade down at a battle's start and back up after it.")]
    [SerializeField, Min(0.01f)] float ambienceFade = 1.5f;

    bool fighting, inBattle;
    float duck = 1f;
    // COZY sets its sources' volumes itself (not necessarily every frame), so each is scaled from the volume COZY
    // last gave it: what it set, and what was written over it
    readonly Dictionary<AudioSource, (float set, float written)> ducked = new Dictionary<AudioSource, (float, float)>();
    readonly List<AudioSource> found = new List<AudioSource>();

    void OnEnable()
    {
        if (run == null)
            run = FindAnyObjectByType<TradeRun>();
        if (view == null)
            view = GetComponentInChildren<BattleView>();
        if (battleCamera != null)
            battleCamera.enabled = false;
        if (run != null)
            run.BattleReached += OnBattleReached;
        if (view != null)
            view.Finished += OnFinished;
    }

    void OnDisable()
    {
        if (run != null)
            run.BattleReached -= OnBattleReached;
        if (view != null)
            view.Finished -= OnFinished;
    }

    void OnBattleReached()
    {
        if (view == null)
            return;
        if (sites.Count > 0)
        {
            var site = sites[(run.Battle - 1) % sites.Count];
            if (site != null)
                view.transform.SetPositionAndRotation(site.position, site.rotation);
        }
        fighting = true;
        inBattle = true;
        view.Begin(run.Army(), run.EnemyArmy());
        if (battleCamera != null)
            battleCamera.enabled = true;
    }

    void OnFinished(int winner)
    {
        if (!fighting)
            return;
        fighting = false;
        StartCoroutine(Return(winner));
    }

    IEnumerator Return(int winner)
    {
        // real time: the final kill plays in slow motion
        yield return new WaitForSecondsRealtime(resultHold);
        var survivors = view.Survivors(0);
        // a draw leaves no one on either side: the enemy is gone, so it counts as a win with nothing left
        bool won = winner == 0 || winner == BattleSim.Draw;
        view.Clear();
        if (battleCamera != null)
            battleCamera.enabled = false;
        run.EndBattle(survivors, won);
        inBattle = false;
    }

    void LateUpdate()
    {
        duck = Mathf.MoveTowards(duck, inBattle ? ambienceInBattle : 1f, Time.unscaledDeltaTime / ambienceFade);
        if (duck >= 1f && ducked.Count == 0)
            return;
        found.Clear();
        if (CozyWeather.instance != null)
            CozyWeather.instance.GetComponentsInChildren(true, found);
        foreach (var thunder in FindObjectsByType<CozyThunder>(FindObjectsSortMode.None))
            found.AddRange(thunder.GetComponentsInChildren<AudioSource>(true));
        foreach (var source in found)
        {
            float set = source.volume;
            if (ducked.TryGetValue(source, out var last) && Mathf.Abs(source.volume - last.written) < 0.0001f)
                set = last.set;
            float written = set * duck;
            source.volume = written;
            ducked[source] = (set, written);
        }
        if (duck >= 1f)
            ducked.Clear();
    }
}
