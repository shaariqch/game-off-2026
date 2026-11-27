using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Plays Feel players for the Shift keys: presses, counting, letting go, a trade going through, a cancel and running
/// out. Each player holds its own sounds and UI Toolkit feedbacks, aimed at the Trade HUD's elements by name, so they
/// can be tuned in the inspector. <c>Tools > Trade > Build Trade Feel</c> makes a default set.
/// </summary>
public class TradeFeel : MonoBehaviour
{
    [System.Serializable]
    public class SidePlayers
    {
        [Tooltip("The key goes down and starts counting.")]
        public MMF_Player press;
        [Tooltip("The key comes up and the countdown starts.")]
        public MMF_Player release;
        [Tooltip("The count goes up by one. Its sounds' pitch rises with the count.")]
        public MMF_Player count;
        [Tooltip("Can't afford any more, or has none left to sell.")]
        public MMF_Player capped;
        [Tooltip("The other key cancelled this order.")]
        public MMF_Player cancelled;
        [Tooltip("The trade went through.")]
        public MMF_Player traded;
    }

    [SerializeField] TradeRun run;
    [Tooltip("The Trade HUD's document. Its parent's if empty.")]
    [SerializeField] UIDocument document;
    [SerializeField] SidePlayers buy = new SidePlayers();
    [SerializeField] SidePlayers sell = new SidePlayers();
    [Tooltip("How much the count tick's pitch rises per unit counted, and the count where it stops rising.")]
    [SerializeField] Vector2 countPitch = new Vector2(0.012f, 40f);

    // the HUD's root element the players were set up against; the document can rebuild its tree (on enable, or
    // when the layout is reloaded), and the players' UI Toolkit feedbacks hold on to the elements they found
    VisualElement builtFor;

    void OnEnable()
    {
        if (document == null)
            document = GetComponentInParent<UIDocument>();
        if (run == null)
            run = FindAnyObjectByType<TradeRun>();
        if (run == null)
            return;
        run.KeyMoved += OnKeyMoved;
        run.Traded += OnTraded;
        builtFor = null;
    }

    void OnDisable()
    {
        if (run == null)
            return;
        run.KeyMoved -= OnKeyMoved;
        run.Traded -= OnTraded;
    }

    void LateUpdate()
    {
        // after the HUD has built its elements in the same frame
        if (builtFor != null && builtFor.panel != null || document == null)
            return;
        var hud = document.rootVisualElement?.Q("hud");
        if (hud == null || hud.panel == null || hud.childCount == 0)
            return;
        builtFor = hud;
        Initialise();
    }

    void Initialise()
    {
        foreach (var players in new[] { buy, sell })
        foreach (var player in All(players))
            if (player != null)
                player.Initialization();
    }

    static MMF_Player[] All(SidePlayers p) => new[] { p.press, p.release, p.count, p.capped, p.cancelled, p.traded };

    SidePlayers For(TradeRun.Side side) => side == TradeRun.Side.Buy ? buy : sell;

    void OnKeyMoved(TradeRun.Side side, TradeRun.KeyMoment moment, int count)
    {
        if (builtFor == null)
            return;
        var players = For(side);
        switch (moment)
        {
            case TradeRun.KeyMoment.Press:
            case TradeRun.KeyMoment.Resume:
                Play(players.press);
                break;
            case TradeRun.KeyMoment.Release:
                Play(players.release);
                break;
            case TradeRun.KeyMoment.Count:
                SetPitch(players.count, 1f + Mathf.Min(count, countPitch.y) * countPitch.x);
                Play(players.count);
                break;
            case TradeRun.KeyMoment.Capped:
                Play(players.capped);
                break;
            case TradeRun.KeyMoment.Cancelled:
                Play(players.cancelled);
                break;
        }
    }

    void OnTraded(TradeRun.Side side, int unit, int count)
    {
        if (count > 0 && builtFor != null)
            Play(For(side).traded);
    }

    static void Play(MMF_Player player)
    {
        // a player switches its own component off while idle, so only its object being active matters
        if (player != null && player.gameObject.activeInHierarchy)
            player.PlayFeedbacks();
    }

    static void SetPitch(MMF_Player player, float pitch)
    {
        if (player == null)
            return;
        foreach (var feedback in player.FeedbacksList)
            if (feedback is MMF_Sound sound)
                sound.MinPitch = sound.MaxPitch = pitch;
    }
}
