using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// One run's trading: cash, the army held, the market, the timeline to the next battle and the two Shift keys.
/// The Trade HUD only shows what this holds.
/// </summary>
/// <remarks>
/// The keys follow the design doc: holding a Shift key counts up the number of units on it (slowly, then faster, up
/// to what can be afforded or is held); letting go starts a short countdown; when it runs out the trade goes through
/// at the price then. The same key again before that carries on counting and restarts the countdown, the other key
/// cancels, and moving the selection makes it go through at once. Battles aren't joined up yet: when the timeline
/// reaches one, trading stops and Enter starts the next trading phase.
/// </remarks>
[DefaultExecutionOrder(-50)] // the HUD reads the market in its OnEnable
public class TradeRun : MonoBehaviour
{
    public enum Focus { Map, Units }
    public enum Side { Buy, Sell }
    public enum OrderState { Idle, Holding, Counting }
    /// <summary>Something a Shift key did, for the HUD's feedback.</summary>
    public enum KeyMoment
    {
        /// <summary>The key went down and started counting at one.</summary>
        Press,
        /// <summary>The same key went down again during the countdown and carries on counting.</summary>
        Resume,
        /// <summary>The key came up and the countdown started.</summary>
        Release,
        /// <summary>The count went up by one.</summary>
        Count,
        /// <summary>The count reached what can be afforded or is held, or a press found nothing to trade.</summary>
        Capped,
        /// <summary>The other key cancelled this order.</summary>
        Cancelled,
    }

    public sealed class Order
    {
        public Side side;
        public OrderState state;
        /// <summary>Index of the unit type it's for.</summary>
        public int unit;
        public int count;
        /// <summary>The count has reached what can be afforded or is held.</summary>
        public bool capped;
        public float heldFor;
        public float countDebt;
        public float countdownLeft;
        /// <summary>A press that cancelled the other key, or couldn't start, does nothing more until it's let go.</summary>
        public bool spent;
    }

    [Header("Run")]
    [Tooltip("The unit types on the market, in the order the list shows them.")]
    [SerializeField] List<UnitType> units = new List<UnitType>();
    [SerializeField, Min(0)] int startingCash = 200;
    [Tooltip("The design doc's placeholder is five battles; how it feels to play will decide.")]
    [SerializeField, Min(1)] int battlesPerRun = 5;
    [Tooltip("Seconds of trading before each battle.")]
    [SerializeField, Min(5f)] float phaseLength = 75f;
    [Tooltip("0 picks a new seed every run.")]
    [SerializeField] int seed;

    [Header("Market")]
    [SerializeField] Market.Settings marketSettings = Market.Settings.Default;
    [Tooltip("Placeholder events, fired at fixed times in every trading phase.")]
    [SerializeField] List<MarketEvent> events = new List<MarketEvent>();

    [Header("Shift keys")]
    [Tooltip("Seconds between letting go and the trade going through.")]
    [SerializeField, Min(0.1f)] float countdown = 1.5f;
    [Tooltip("A press counts one at once; counting carries on only after the key has been held this long, so a tap is one.")]
    [SerializeField, Min(0f)] float tapGrace = 0.25f;
    [Tooltip("Units a second when counting starts, and after Ramp Time.")]
    [SerializeField] Vector2 countRate = new Vector2(4f, 20f);
    [SerializeField, Min(0.1f)] float rampTime = 2.5f;

    [Header("Map")]
    [SerializeField] MapCamera mapCamera;

    [Header("Sounds")]
    [Tooltip("The Shift keys' own sounds are on the HUD's TradeFeel players, with their feedback.")]
    [SerializeField] AudioSource audioSource;
    [Tooltip("Up and down move the selection in the unit list.")]
    [SerializeField] AudioClip selectClip;
    [Tooltip("Left and right move focus between the map and the unit list.")]
    [SerializeField] AudioClip focusClip;
    [SerializeField] AudioClip noticeClip;
    [SerializeField] AudioClip invasionClip;
    [Tooltip("The timeline reaches the battle and trading stops.")]
    [SerializeField] AudioClip battleClip;

    public Market Market { get; private set; }
    public int Cash { get; private set; }
    public int Selected { get; private set; }
    public Focus CurrentFocus { get; private set; } = Focus.Units;
    /// <summary>Seconds into this trading phase.</summary>
    public float PhaseTime { get; private set; }
    public float PhaseLength => phaseLength;
    /// <summary>The coming battle, from 1.</summary>
    public int Battle { get; private set; } = 1;
    public int BattlesPerRun => battlesPerRun;
    /// <summary>The timeline has reached the battle and trading has stopped.</summary>
    public bool AtBattle { get; private set; }
    public float Countdown => countdown;
    public IReadOnlyList<MarketEvent> Events => events;
    public readonly Order buy = new Order { side = Side.Buy };
    public readonly Order sell = new Order { side = Side.Sell };

    /// <summary>An event has just happened.</summary>
    public event Action<MarketEvent> EventStarted;
    /// <summary>A trade went through: side, unit index, how many.</summary>
    public event Action<Side, int, int> Traded;
    /// <summary>A Shift key did something: side, what, and the order's count after it.</summary>
    public event Action<Side, KeyMoment, int> KeyMoved;

    int[] held;

    public int Held(int unit) => held[unit];
    public int UnitCount => Market.listings.Count;
    public UnitType Unit(int index) => Market.listings[index].type;
    public bool HasHappened(MarketEvent marketEvent) => marketEvent.time <= PhaseTime;
    /// <summary>The order whose key is down or counting down, if any.</summary>
    public Order Active => buy.state != OrderState.Idle ? buy : sell.state != OrderState.Idle ? sell : null;

    void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (mapCamera == null)
            mapCamera = FindAnyObjectByType<MapCamera>();
        NewRun();
    }

    public void NewRun()
    {
        int runSeed = seed != 0 ? seed : UnityEngine.Random.Range(1, int.MaxValue);
        Market = new Market(units, marketSettings, runSeed);
        Cash = startingCash;
        held = new int[Market.listings.Count];
        Selected = Mathf.Min(Selected, held.Length - 1);
        Battle = 1;
        StartPhase();
        Debug.Log($"[Trade] new run, seed {runSeed}, cash {Cash}");
    }

    void StartPhase()
    {
        PhaseTime = 0f;
        AtBattle = false;
        Reset(buy);
        Reset(sell);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        var keyboard = Keyboard.current;
        bool input = keyboard != null && !GameInput.Blocked;

        if (AtBattle)
        {
            if (input && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                // placeholder until battles are joined up: the battle is skipped
                Debug.Log($"[Trade] battle {Battle} skipped (not joined up yet)");
                if (Battle >= battlesPerRun) NewRun();
                else
                {
                    Battle++;
                    StartPhase();
                }
            }
            return;
        }

        float before = PhaseTime;
        PhaseTime = Mathf.Min(PhaseTime + dt, phaseLength);
        foreach (var marketEvent in events)
        {
            if (marketEvent.time > before && marketEvent.time <= PhaseTime)
            {
                Market.Start(marketEvent);
                Play(marketEvent.category == MarketEvent.Category.Invasion ? invasionClip : noticeClip);
                EventStarted?.Invoke(marketEvent);
                Debug.Log($"[Trade] event at {marketEvent.place}: {marketEvent.text}");
            }
        }
        Market.Tick(dt);

        if (input)
        {
            ReadArrows(keyboard);
            ReadKey(buy, sell, keyboard.leftShiftKey, dt);
            ReadKey(sell, buy, keyboard.rightShiftKey, dt);
        }
        RunCountdown(buy, dt);
        RunCountdown(sell, dt);

        if (PhaseTime >= phaseLength)
        {
            // whatever is still pending goes through before the battle
            Complete(buy);
            Complete(sell);
            AtBattle = true;
            Play(battleClip);
            Debug.Log($"[Trade] battle {Battle} reached with cash {Cash}, army {ArmyText()}");
        }
    }

    void ReadArrows(Keyboard keyboard)
    {
        if (keyboard.leftArrowKey.wasPressedThisFrame && CurrentFocus != Focus.Map)
        {
            CurrentFocus = Focus.Map;
            Play(focusClip);
        }
        if (keyboard.rightArrowKey.wasPressedThisFrame && CurrentFocus != Focus.Units)
        {
            CurrentFocus = Focus.Units;
            Play(focusClip);
        }

        if (CurrentFocus == Focus.Map)
        {
            float scroll = (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);
            if (mapCamera != null) mapCamera.Scroll(scroll);
            return;
        }

        int move = (keyboard.downArrowKey.wasPressedThisFrame ? 1 : 0) - (keyboard.upArrowKey.wasPressedThisFrame ? 1 : 0);
        int next = Mathf.Clamp(Selected + move, 0, UnitCount - 1);
        if (next != Selected)
        {
            // a trade still counting down for the old selection goes through now
            Complete(buy);
            Complete(sell);
            Selected = next;
            Play(selectClip);
        }
    }

    void ReadKey(Order mine, Order other, KeyControl key, float dt)
    {
        if (key.wasPressedThisFrame)
        {
            if (other.state != OrderState.Idle)
            {
                Reset(other);
                KeyMoved?.Invoke(other.side, KeyMoment.Cancelled, 0);
                mine.spent = true;
            }
            else if (mine.state == OrderState.Counting && mine.unit == Selected)
            {
                // carry on counting from where it was, without the tap pause
                mine.state = OrderState.Holding;
                mine.heldFor = tapGrace;
                mine.countDebt = 0f;
                KeyMoved?.Invoke(mine.side, KeyMoment.Resume, mine.count);
            }
            else
            {
                Complete(mine);
                if (Cap(mine.side, Selected) <= 0)
                {
                    KeyMoved?.Invoke(mine.side, KeyMoment.Capped, 0);
                    mine.spent = true;
                }
                else
                {
                    mine.state = OrderState.Holding;
                    mine.unit = Selected;
                    mine.count = 1;
                    mine.heldFor = 0f;
                    mine.countDebt = 0f;
                    mine.capped = Cap(mine.side, Selected) <= 1;
                    KeyMoved?.Invoke(mine.side, KeyMoment.Press, mine.count);
                }
            }
        }

        if (mine.spent)
        {
            if (!key.isPressed) mine.spent = false;
            return;
        }
        if (mine.state != OrderState.Holding)
            return;

        if (!key.isPressed)
        {
            mine.state = OrderState.Counting;
            mine.countdownLeft = countdown;
            KeyMoved?.Invoke(mine.side, KeyMoment.Release, mine.count);
            return;
        }

        mine.heldFor += dt;
        if (mine.heldFor < tapGrace)
            return;
        float rate = Mathf.Lerp(countRate.x, countRate.y, Mathf.Clamp01((mine.heldFor - tapGrace) / rampTime));
        mine.countDebt += rate * dt;
        int cap = Cap(mine.side, mine.unit);
        while (mine.countDebt >= 1f)
        {
            mine.countDebt -= 1f;
            if (mine.count < cap)
            {
                mine.count++;
                KeyMoved?.Invoke(mine.side, KeyMoment.Count, mine.count);
            }
            else
            {
                if (!mine.capped)
                    KeyMoved?.Invoke(mine.side, KeyMoment.Capped, mine.count);
                mine.capped = true;
                mine.countDebt = 0f;
                break;
            }
        }
    }

    void RunCountdown(Order order, float dt)
    {
        if (order.state != OrderState.Counting)
            return;
        order.countdownLeft -= dt;
        if (order.countdownLeft <= 0f)
            Complete(order);
    }

    /// <summary>How many the order could reach now: what the cash buys, or what is held.</summary>
    public int Cap(Side side, int unit) => side == Side.Buy ? Market.Affordable(unit, Cash) : held[unit];

    /// <summary>0 while held, then 0 to 1 through the countdown.</summary>
    public float Progress(Order order) =>
        order.state == OrderState.Counting ? Mathf.Clamp01(1f - order.countdownLeft / countdown) : 0f;

    /// <summary>Puts a pending order through now.</summary>
    void Complete(Order order)
    {
        if (order.state == OrderState.Idle || order.count <= 0)
        {
            Reset(order);
            return;
        }
        int unit = order.unit;
        if (order.side == Side.Buy)
        {
            var (bought, spent) = Market.Buy(unit, order.count, Cash);
            Cash -= spent;
            held[unit] += bought;
            Debug.Log($"[Trade] bought {bought} {Unit(unit).name} for {spent}, cash {Cash}");
            Traded?.Invoke(Side.Buy, unit, bought);
        }
        else
        {
            int count = Mathf.Min(order.count, held[unit]);
            int made = Market.Sell(unit, count);
            Cash += made;
            held[unit] -= count;
            Debug.Log($"[Trade] sold {count} {Unit(unit).name} for {made}, cash {Cash}");
            Traded?.Invoke(Side.Sell, unit, count);
        }
        Reset(order);
    }

    static void Reset(Order order)
    {
        order.state = OrderState.Idle;
        order.count = 0;
        order.capped = false;
        order.heldFor = order.countDebt = order.countdownLeft = 0f;
    }

    string ArmyText()
    {
        var parts = new List<string>();
        for (int i = 0; i < held.Length; i++)
            if (held[i] > 0) parts.Add(held[i] + " " + Unit(i).name);
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    void Play(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }
}
