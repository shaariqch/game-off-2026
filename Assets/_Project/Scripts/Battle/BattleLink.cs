using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Fights the trading run's battles and raids in their own scene. When the timeline reaches one, it fades to black,
/// loads the battle scene beside the map, switches the map off (its cameras, COZY, terrain and towns: everything
/// but the run, the HUD and itself), picks a battlefield and starts the fight. When the battle has ended and the
/// result has had a moment, it fades out, hands the survivors back to the run, unloads the battle scene and brings
/// the map back.
/// </summary>
/// <remarks>
/// It stays on the map because <see cref="TradeRun"/> checks for listeners: with none, battles are skipped and
/// raids can't be answered. The battle scene's <see cref="BattleView"/> only acts when told to.
/// </remarks>
public class BattleLink : MonoBehaviour
{
    [SerializeField] TradeRun run;
    [Tooltip("The battle scene, by name (in Build Settings).")]
    [SerializeField] string battleScene = "Battle";
    [Tooltip("Off: the bandits come on at the player's army instead of holding their ground.")]
    [SerializeField] bool banditsHold;
    [Tooltip("Real seconds between the battle ending and the fade back to the map, so the last blow can land.")]
    [SerializeField, Min(0f)] float resultHold = 3f;
    [Tooltip("Real seconds each fade to and from black takes.")]
    [SerializeField, Min(0.01f)] float fadeTime = 0.4f;
    [Tooltip("The panel the fade is drawn on (the HUD's), so it covers the screen above everything.")]
    [SerializeField] PanelSettings fadePanel;

    /// <summary>The battle scene's view while a battle scene is open, else null.</summary>
    public BattleView View { get; private set; }

    bool busy, raid;
    Scene loaded;
    readonly List<GameObject> hidden = new List<GameObject>();
    VisualElement curtain;
    float curtainOpacity;
    int fadeRun;
    float timeScale = 1f, fixedDeltaTime = 0.02f;

    void OnEnable()
    {
        if (run == null)
            run = FindAnyObjectByType<TradeRun>();
        if (run != null)
        {
            run.BattleReached += OnBattleReached;
            run.RaidReached += OnRaidReached;
        }
    }

    void OnDisable()
    {
        if (run != null)
        {
            run.BattleReached -= OnBattleReached;
            run.RaidReached -= OnRaidReached;
        }
    }

    void OnBattleReached()
    {
        if (!busy)
            StartCoroutine(Fight(run.Army(), run.EnemyArmy(), true, false, run.Battle));
    }

    void OnRaidReached(TradeRun.Raid raidAt)
    {
        if (!busy)
            StartCoroutine(Fight(run.Army(), raidAt.bandits, banditsHold, true, run.Battle * 7 + raidAt.place.GetHashCode()));
    }

    IEnumerator Fight(List<ArmyEntry> army, IList<ArmyEntry> enemy, bool enemyHolds, bool isRaid, int fieldSeed)
    {
        busy = true;
        raid = isRaid;
        yield return Enter(fieldSeed);
        bool done = false;
        int winner = BattleSim.Running;
        void OnFinished(int w)
        {
            winner = w;
            done = true;
        }
        bool started = false;
        if (View != null)
        {
            View.Finished += OnFinished;
            try
            {
                View.Begin(army, enemy, enemyHolds);
                started = View.Sim != null;
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }
        if (!started)
        {
            // no battle scene, or the battle wouldn't start: put the map back and count it lost, so the run isn't stuck
            Debug.LogError($"BattleLink couldn't fight in the battle scene {battleScene}.", this);
            if (View != null)
                View.Finished -= OnFinished;
            yield return Leave();
            busy = false;
            Finish(army, false);
            yield return Fade(0f);
            yield break;
        }
        // two frames for the figures to be built and drawn once behind the black (the first draw compiles shaders)
        yield return null;
        yield return null;
        yield return Fade(0f);
        while (!done)
            yield return null;
        View.Finished -= OnFinished;
        // real time: the final kill plays in slow motion
        yield return new WaitForSecondsRealtime(resultHold);
        yield return Fade(1f);
        var survivors = View.Survivors(0);
        // a draw leaves no one on either side: the enemy is gone, so it counts as a win with nothing left
        bool won = winner == 0 || winner == BattleSim.Draw;
        yield return Leave();
        // free before handing back: ending a raid at the phase's end raises the battle straight away
        busy = false;
        Finish(survivors, won);
        yield return Fade(0f);
    }

    void Finish(IList<ArmyEntry> survivors, bool won)
    {
        if (raid) run.EndRaid(survivors, won);
        else run.EndBattle(survivors, won);
    }

    /// <summary>
    /// Fades to black, loads the battle scene beside the map, switches the map off and picks a battlefield by the
    /// seed. <see cref="View"/> is the scene's view afterwards (null if it failed). The screen stays black: fade in
    /// once the battle has begun. Also used by <see cref="BattleStress"/>.
    /// </summary>
    public IEnumerator Enter(int fieldSeed)
    {
        yield return Fade(1f);
        timeScale = Time.timeScale;
        fixedDeltaTime = Time.fixedDeltaTime;
        var loading = SceneManager.LoadSceneAsync(battleScene, LoadSceneMode.Additive);
        View = null;
        if (loading == null)
            yield break;
        while (!loading.isDone)
            yield return null;
        loaded = SceneManager.GetSceneByName(battleScene);
        if (!loaded.IsValid())
            yield break;

        // the map goes dark: its cameras, listener, COZY (which rewrites the light and fog every frame), terrain
        // and towns, all but what keeps the run going (the run, the HUD, the dev panel and this)
        hidden.Clear();
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            // switching one root off can destroy another later in the list (on WebGL, one of COZY's runtime objects)
            if (root == null || !root.activeSelf || Keeps(root))
                continue;
            root.SetActive(false);
            hidden.Add(root);
        }
        SceneManager.SetActiveScene(loaded);

        View = null;
        foreach (var root in loaded.GetRootGameObjects())
        {
            View = root.GetComponentInChildren<BattleView>(true);
            if (View != null)
                break;
        }
        if (View != null && View.BattlefieldCount > 0)
            View.UseBattlefield(new System.Random(fieldSeed).Next(View.BattlefieldCount));
    }

    /// <summary>Unloads the battle scene and brings the map back, behind the black (fade in afterwards).</summary>
    public IEnumerator Leave()
    {
        if (View != null)
            View.Clear();
        View = null;
        if (loaded.IsValid() && loaded.isLoaded)
        {
            var unloading = SceneManager.UnloadSceneAsync(loaded);
            while (unloading != null && !unloading.isDone)
                yield return null;
        }
        // the battle's slow motion and freeze frames end with it
        Time.timeScale = timeScale;
        Time.fixedDeltaTime = fixedDeltaTime;
        SceneManager.SetActiveScene(gameObject.scene);
        foreach (var root in hidden)
            if (root != null)
                root.SetActive(true);
        hidden.Clear();
    }

    /// <summary>Roots that stay on during a battle: the run, the HUD and dev panel, and the one holding this.</summary>
    bool Keeps(GameObject root) =>
        root == gameObject || transform.IsChildOf(root.transform)
        || root.GetComponentInChildren<TradeRun>(true) != null
        || root.GetComponentInChildren<UIDocument>(true) != null;

    /// <summary>Fades the black curtain to this opacity over <see cref="fadeTime"/>, in real time.</summary>
    public IEnumerator Fade(float to)
    {
        var element = Curtain();
        if (element == null)
            yield break;
        // a newer fade (the next battle's, starting as this one ends) takes over from wherever this one got to
        int run = ++fadeRun;
        float from = curtainOpacity;
        for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
        {
            if (run != fadeRun)
                yield break;
            SetCurtain(Mathf.Lerp(from, to, t / fadeTime));
            yield return null;
        }
        if (run == fadeRun)
            SetCurtain(to);
    }

    void SetCurtain(float opacity)
    {
        curtainOpacity = opacity;
        curtain.style.opacity = opacity;
    }

    VisualElement Curtain()
    {
        if (curtain != null)
            return curtain;
        if (fadePanel == null)
            return null;
        var holder = new GameObject("Battle Fade");
        holder.transform.SetParent(transform, false);
        var document = holder.AddComponent<UIDocument>();
        document.panelSettings = fadePanel;
        // above the HUD and the dev panel
        document.sortingOrder = 50;
        curtain = new VisualElement { name = "battle-fade", pickingMode = PickingMode.Ignore };
        curtain.style.position = Position.Absolute;
        curtain.style.left = curtain.style.right = curtain.style.top = curtain.style.bottom = 0f;
        curtain.style.backgroundColor = Color.black;
        curtain.style.opacity = 0f;
        document.rootVisualElement.Add(curtain);
        return curtain;
    }
}
