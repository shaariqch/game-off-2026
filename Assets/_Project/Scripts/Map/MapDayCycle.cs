using DistantLands.Cozy;
using UnityEngine;

/// <summary>
/// Runs COZY's time of day from the trading timeline: each trading phase is one day, opening at
/// <see cref="dayStart"/> and reaching the battle at <see cref="dayEnd"/>. COZY's own clock never moves on by itself,
/// since this sets it every frame before COZY updates; its weather still changes by itself.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-200)] // before COZY's Update reads the time
public class MapDayCycle : MonoBehaviour
{
    [SerializeField] TradeRun run;
    [Tooltip("Hour of the day when a trading phase opens.")]
    [SerializeField, Range(0f, 24f)] float dayStart = 7f;
    [Tooltip("Hour of the day when the phase reaches its battle (COZY's sun sets about 17:30 and dusk ends about 19:10).")]
    [SerializeField, Range(0f, 24f)] float dayEnd = 19.5f;
    [Tooltip("Time of year, 0 to 1 from the start of COZY's calendar (0.5 is midsummer): it sets the length of the " +
             "day and whether precipitation falls as rain or snow. COZY's calendar starts in winter.")]
    [SerializeField, Range(0f, 1f)] float timeOfYear = 0.5f;
    [Tooltip("The hour shown outside Play mode, to preview the light.")]
    [SerializeField, Range(0f, 24f)] float previewHour = 10f;

    /// <summary>The hour shown now, 0 to 24.</summary>
    public float Hour { get; private set; }

    public float PreviewHour
    {
        get => previewHour;
        set => previewHour = value;
    }

    void Update()
    {
        var cozy = CozyWeather.instance;
        if (cozy == null || cozy.timeModule == null)
            return;
        if (Application.isPlaying && run == null)
            run = FindAnyObjectByType<TradeRun>();
        Hour = Application.isPlaying && run != null
            ? Mathf.Lerp(dayStart, dayEnd, Mathf.Clamp01(run.PhaseTime / run.PhaseLength))
            : previewHour;
        cozy.timeModule.currentTime = Hour / 24f;
        cozy.timeModule.currentDay = Mathf.RoundToInt(timeOfYear * cozy.timeModule.DaysPerYear);
    }
}
