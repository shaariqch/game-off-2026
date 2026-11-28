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
/// cancels, and moving the selection makes it go through at once. When the timeline reaches the battle, trading stops
/// and whoever listens to <see cref="BattleReached"/> fights it (with no one listening, Enter skips it).
///
/// Each trading phase draws its events at random from the pool, at random times, so they can overlap and pull the
/// same price both ways (their pushes add up). Raids open in the list for a while; Left Shift on one sends the whole
/// army, the timeline stops while it fights (<see cref="RaidReached"/>), and a win pays cash.
/// </remarks>
[DefaultExecutionOrder(-50)] // the HUD reads the market in its OnEnable
public class TradeRun : MonoBehaviour
{
    public enum Focus { Map, Units }
    public enum Side { Buy, Sell }
    /// <summary>What a row of the list is: a unit type on the market, the tip, an open raid, or a type the enemy has brought.</summary>
    public enum RowKind { Unit, Tip, Raid, Enemy }
    public enum OrderState { Idle, Holding, Counting }
    /// <summary>Something a Shift key did, for the HUD's feedback.</summary>
    public enum KeyMoment
    {
        /// <summary>The key went down and started counting at one.</summary>
        Press,
        /// <summary>The same key went down again during the countdown: one more, and holding carries on counting.</summary>
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

    /// <summary>One unit type in the coming battle's enemy army. Everyone sees the type; the count is scouted.</summary>
    public sealed class EnemyGroup
    {
        public UnitType type;
        public int count;
        public bool scouted;
    }

    /// <summary>Bandits raiding near a town: open in the list for a while, where Left Shift sends the whole army.</summary>
    public sealed class Raid
    {
        public string place;
        public string text;
        /// <summary>Timeline seconds when it shows in the list, and when the bandits are gone if no one has come.</summary>
        public float opens, closes;
        public readonly List<ArmyEntry> bandits = new List<ArmyEntry>();
        public int pay;

        public int BanditCount
        {
            get
            {
                int count = 0;
                foreach (var entry in bandits)
                    count += entry.count;
                return count;
            }
        }
    }

    public sealed class Order
    {
        public Side side;
        public OrderState state;
        /// <summary>The list row it's for (see <see cref="KindOf"/>).</summary>
        public int row;
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
    [Tooltip("Timeline seconds of trading before each battle; Pace sets how long that takes in real time.")]
    [SerializeField, Min(5f)] float phaseLength = 75f;
    [Tooltip("How fast the timeline and the market run: 1 is a phase in Phase Length seconds, 0.5 takes twice as long. "
        + "Event times, price moves and fades all slow together; the Shift keys keep their own timing.")]
    [SerializeField, Range(0.2f, 2f)] float pace = 0.4f;
    [Tooltip("0 picks a new seed every run.")]
    [SerializeField] int seed;

    [Header("Market")]
    [SerializeField] Market.Settings marketSettings = Market.Settings.Default;
    [Tooltip("The events a trading phase draws from. Each phase picks some at random and gives them random times (the " +
             "times and Known here are ignored), so they can overlap and pull the same price both ways.")]
    [SerializeField] List<MarketEvent> events = new List<MarketEvent>();
    [Tooltip("How many events a trading phase draws, at least and at most.")]
    [SerializeField] Vector2Int eventsPerPhase = new Vector2Int(4, 6);
    [Tooltip("Timeline seconds: drawn events fall between these.")]
    [SerializeField] Vector2 eventWindow = new Vector2(6f, 66f);
    [Tooltip("The chance a drawn event shows on the timeline from the start; the rest are hidden until they happen or a tip reveals them.")]
    [SerializeField, Range(0f, 1f)] float knownChance = 0.5f;

    [Header("Raids")]
    [Tooltip("How many raids a trading phase has, at least and at most. Only one is open at a time.")]
    [SerializeField] Vector2Int raidsPerPhase = new Vector2Int(1, 2);
    [Tooltip("Timeline seconds: raids open between these.")]
    [SerializeField] Vector2 raidWindow = new Vector2(10f, 60f);
    [Tooltip("Timeline seconds a raid stays in the list before the bandits are gone.")]
    [SerializeField, Min(1f)] float raidOpenFor = 14f;
    [Tooltip("Places raids happen at (MapPlace names). Each is fought at the raid site of that name under the battle rig, or at the battle site.")]
    [SerializeField] List<string> raidPlaces = new List<string>();
    [Tooltip("The notice on the map; {0} is the place.")]
    [SerializeField] string raidText = "Bandits are raiding the farms near {0}.";
    [Tooltip("What bandits can be; each raid is one of them.")]
    [SerializeField] List<UnitType> banditTypes = new List<UnitType>();
    [Tooltip("What a raid's bandits are worth at cost before the first battle, and what each later battle adds. Each raid is a third more or less at random.")]
    [SerializeField] Vector2 banditWorth = new Vector2(60f, 30f);
    [Tooltip("Cash for beating a raid, as a share of the bandits' worth at cost.")]
    [SerializeField, Min(0f)] float raidPay = 1.2f;

    [Header("Intel")]
    [Tooltip("Cash for a tip, which reveals one random event still to come in this trading phase.")]
    [SerializeField, Min(0)] int tipPrice = 20;
    [Tooltip("Cash to scout one enemy unit type, which shows how many of it the enemy has.")]
    [SerializeField, Min(0)] int scoutPrice = 15;

    [Header("Battles")]
    [Tooltip("Cash for a win, as a share of what the enemy army was worth at cost. The design: a normal win pays " +
             "about enough to buy back what was lost, so a win with few losses comes out well ahead.")]
    [SerializeField, Min(0f)] float winPay = 0.5f;

    [Header("Enemy (placeholder: random types sharing a worth)")]
    [Tooltip("What the enemy army is worth at the first battle, and what each later battle adds, in unit cost.")]
    [SerializeField] Vector2 enemyWorth = new Vector2(240f, 120f);
    [Tooltip("How many unit types the enemy brings, at least and at most.")]
    [SerializeField] Vector2Int enemyTypes = new Vector2Int(2, 3);

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
    // TODO: a raid sound (a distant alarm bell or horn) for when a raid opens; until then the invasion clip plays
    [SerializeField] AudioClip raidClip;
    [Tooltip("The timeline reaches the battle and trading stops.")]
    [SerializeField] AudioClip battleClip;

    public Market Market { get; private set; }
    public int Cash { get; private set; }
    public int Selected { get; private set; }
    public Focus CurrentFocus { get; private set; } = Focus.Units;
    /// <summary>Counts every trading phase started, across runs, so the HUD knows when the events are new.</summary>
    public int Phase { get; private set; }
    /// <summary>Seconds into this trading phase.</summary>
    public float PhaseTime { get; private set; }
    public float PhaseLength => phaseLength;
    /// <summary>The coming battle, from 1.</summary>
    public int Battle { get; private set; } = 1;
    public int BattlesPerRun => battlesPerRun;
    /// <summary>The timeline has reached the battle and trading has stopped.</summary>
    public bool AtBattle { get; private set; }
    public float Countdown => countdown;
    /// <summary>This trading phase's events, in time order.</summary>
    public IReadOnlyList<MarketEvent> Events => phaseEvents;
    public readonly Order buy = new Order { side = Side.Buy };
    public readonly Order sell = new Order { side = Side.Sell };

    /// <summary>An event has just happened.</summary>
    public event Action<MarketEvent> EventStarted;
    /// <summary>A trade went through: side, list row, how many (units, tips or scouted types).</summary>
    public event Action<Side, int, int> Traded;
    /// <summary>A Shift key did something: side, what, and the order's count after it.</summary>
    public event Action<Side, KeyMoment, int> KeyMoved;
    /// <summary>A tip has revealed an event still to come.</summary>
    public event Action<MarketEvent> Tipped;
    /// <summary>The timeline has reached the battle. Whoever fights it calls <see cref="EndBattle"/> afterwards.
    /// With no one listening, Enter skips the battle.</summary>
    public event Action BattleReached;
    /// <summary>A battle has been fought: won or not, and the cash it paid.</summary>
    public event Action<bool, int> BattleEnded;
    /// <summary>A raid has opened in the list.</summary>
    public event Action<Raid> RaidOpened;
    /// <summary>The army has ridden out to a raid. Whoever fights it calls <see cref="EndRaid"/> afterwards; with no
    /// one listening, raids can't be answered.</summary>
    public event Action<Raid> RaidReached;
    /// <summary>A raid has been fought: won or not, and the cash it paid.</summary>
    public event Action<bool, int> RaidEnded;

    /// <summary>A battle or raid is being fought on the map (trading has stopped and someone took the fight).</summary>
    public bool Fighting { get; private set; }
    /// <summary>The army is out at a raid and the timeline has stopped.</summary>
    public bool Raiding { get; private set; }
    /// <summary>The raid in the list now, if any.</summary>
    public Raid OpenRaid { get; private set; }
    /// <summary>The raid being fought, if any.</summary>
    public Raid CurrentRaid { get; private set; }

    int[] held;
    int runSeed;
    readonly List<EnemyGroup> enemy = new List<EnemyGroup>();
    readonly HashSet<MarketEvent> revealed = new HashSet<MarketEvent>();
    readonly List<MarketEvent> hidden = new List<MarketEvent>();
    readonly List<MarketEvent> phaseEvents = new List<MarketEvent>();
    readonly Dictionary<MarketEvent, int> numbers = new Dictionary<MarketEvent, int>();
    readonly List<Raid> raids = new List<Raid>();
    int nextRaid;

    public IReadOnlyList<EnemyGroup> Enemy => enemy;
    public int TipPrice => tipPrice;
    public int ScoutPrice => scoutPrice;
    /// <summary>Units, then the tip, then the open raid (if any), then the enemy's types.</summary>
    public int RowCount => FirstEnemyRow + enemy.Count;
    int FirstEnemyRow => UnitCount + 1 + (OpenRaid != null ? 1 : 0);
    public RowKind KindOf(int row) =>
        row < UnitCount ? RowKind.Unit
        : row == UnitCount ? RowKind.Tip
        : row < FirstEnemyRow ? RowKind.Raid
        : RowKind.Enemy;
    public EnemyGroup EnemyAt(int row) => enemy[row - FirstEnemyRow];
    /// <summary>
    /// An event's number in this phase, as the timeline and its notice show it, or 0 while the player doesn't know of
    /// it. Numbers are given as the player learns of events: those known from the start in time order, then each
    /// hidden one when a tip reveals it or it happens. (Numbering every event by time gave hidden ones away: a gap
    /// between I and III showed something hidden sat there.)
    /// </summary>
    public int Number(MarketEvent marketEvent) => numbers.TryGetValue(marketEvent, out int number) ? number : 0;

    void GiveNumber(MarketEvent marketEvent)
    {
        if (!numbers.ContainsKey(marketEvent))
            numbers[marketEvent] = numbers.Count + 1;
    }
    /// <summary>Shown on the timeline before it happens: known from the start, or revealed by a tip.</summary>
    public bool IsKnown(MarketEvent marketEvent) => marketEvent.known || revealed.Contains(marketEvent);
    public bool WasTipped(MarketEvent marketEvent) => revealed.Contains(marketEvent);

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
        runSeed = seed != 0 ? seed : UnityEngine.Random.Range(1, int.MaxValue);
        Market = new Market(units, marketSettings, runSeed);
        Cash = startingCash;
        held = new int[Market.listings.Count];
        Battle = 1;
        StartPhase();
        Debug.Log($"[Trade] new run, seed {runSeed}, cash {Cash}");
    }

    void StartPhase()
    {
        Phase++;
        PhaseTime = 0f;
        AtBattle = false;
        Fighting = false;
        Reset(buy);
        Reset(sell);
        Raiding = false;
        CurrentRaid = null;
        revealed.Clear();
        MakeEnemy();
        MakeEvents();
        MakeRaids();
        Selected = Mathf.Clamp(Selected, 0, RowCount - 1);
    }

    /// <summary>Draws this phase's events from the pool, at random times.</summary>
    void MakeEvents()
    {
        phaseEvents.Clear();
        var random = new System.Random(unchecked(runSeed * 131 + Battle * 7 + 1));
        var pool = new List<MarketEvent>();
        foreach (var marketEvent in events)
            if (marketEvent != null && marketEvent.affects != null)
                pool.Add(marketEvent);
        int count = Mathf.Min(pool.Count, random.Next(eventsPerPhase.x, eventsPerPhase.y + 1));
        for (int i = 0; i < count; i++)
        {
            int pick = random.Next(i, pool.Count);
            (pool[i], pool[pick]) = (pool[pick], pool[i]);
            var source = pool[i];
            phaseEvents.Add(new MarketEvent
            {
                time = Mathf.Lerp(eventWindow.x, eventWindow.y, (float)random.NextDouble()),
                category = source.category,
                place = source.place,
                text = source.text,
                affects = source.affects,
                strength = source.strength,
                known = random.NextDouble() < knownChance,
            });
        }
        phaseEvents.Sort((a, b) => a.time.CompareTo(b.time));
        numbers.Clear();
        foreach (var marketEvent in phaseEvents)
            if (marketEvent.known)
                GiveNumber(marketEvent);
        var parts = new List<string>();
        foreach (var marketEvent in phaseEvents)
            parts.Add($"{(Number(marketEvent) > 0 ? Roman(Number(marketEvent)) : "-")} {marketEvent.time:0}s {marketEvent.affects.name} " +
                      $"{marketEvent.strength:+0.0;-0.0}{(marketEvent.known ? "" : " hidden")}");
        Debug.Log($"[Trade] battle {Battle} events: {string.Join(", ", parts)}");
    }

    /// <summary>Sets this phase's raids: one at a time, each opening somewhere in its share of the raid window.</summary>
    void MakeRaids()
    {
        raids.Clear();
        OpenRaid = null;
        nextRaid = 0;
        if (raidPlaces.Count == 0 || banditTypes.Count == 0)
            return;
        var random = new System.Random(unchecked(runSeed * 977 + Battle * 13 + 5));
        int count = random.Next(raidsPerPhase.x, raidsPerPhase.y + 1);
        float slot = (raidWindow.y - raidWindow.x) / Mathf.Max(1, count);
        string lastPlace = null;
        for (int i = 0; i < count; i++)
        {
            float start = raidWindow.x + slot * i;
            float opens = start + (float)random.NextDouble() * Mathf.Max(0f, slot - raidOpenFor);
            string place = raidPlaces[random.Next(raidPlaces.Count)];
            if (place == lastPlace && raidPlaces.Count > 1)
                place = raidPlaces[(raidPlaces.IndexOf(place) + 1 + random.Next(raidPlaces.Count - 1)) % raidPlaces.Count];
            lastPlace = place;
            var type = banditTypes[random.Next(banditTypes.Count)];
            float worth = (banditWorth.x + banditWorth.y * (Battle - 1)) * Mathf.Lerp(0.67f, 1.33f, (float)random.NextDouble());
            int bandits = Mathf.Max(2, Mathf.RoundToInt(worth / Mathf.Max(1, type.cost)));
            var raid = new Raid
            {
                place = place,
                text = string.Format(raidText, place),
                opens = opens,
                closes = Mathf.Min(opens + raidOpenFor, start + slot),
                pay = Mathf.RoundToInt(bandits * type.cost * raidPay),
            };
            raid.bandits.Add(new ArmyEntry(type, bandits));
            raids.Add(raid);
            Debug.Log($"[Trade] battle {Battle} raid at {raid.opens:0}s near {place}: {bandits} {type.name}, pays {raid.pay}");
        }
    }

    /// <summary>Opens the next raid in the list when its time comes, and takes it away when the bandits are gone.</summary>
    void UpdateRaids()
    {
        if (OpenRaid != null && PhaseTime >= OpenRaid.closes)
        {
            Debug.Log($"[Trade] the raid near {OpenRaid.place} is over: no one came");
            RemoveRaidRow();
        }
        if (OpenRaid == null && nextRaid < raids.Count && PhaseTime >= raids[nextRaid].opens)
        {
            // the rows after the tip move down one, and so does anything selected or pending on them
            int raidRow = UnitCount + 1;
            if (Selected >= raidRow) Selected++;
            if (buy.state != OrderState.Idle && buy.row >= raidRow) buy.row++;
            if (sell.state != OrderState.Idle && sell.row >= raidRow) sell.row++;
            OpenRaid = raids[nextRaid++];
            Play(raidClip != null ? raidClip : invasionClip);
            RaidOpened?.Invoke(OpenRaid);
            Debug.Log($"[Trade] raid: {OpenRaid.text}");
        }
    }

    void RemoveRaidRow()
    {
        if (OpenRaid == null)
            return;
        int raidRow = UnitCount + 1;
        foreach (var order in new[] { buy, sell })
        {
            if (order.state == OrderState.Idle) continue;
            if (order.row == raidRow)
            {
                Reset(order);
                KeyMoved?.Invoke(order.side, KeyMoment.Cancelled, 0);
            }
            else if (order.row > raidRow) order.row--;
        }
        OpenRaid = null;
        if (Selected > raidRow) Selected--;
        Selected = Mathf.Clamp(Selected, 0, RowCount - 1);
    }

    int ArmySize()
    {
        int size = 0;
        foreach (int count in held)
            size += count;
        return size;
    }

    /// <summary>1 to 3999 in Roman numerals.</summary>
    public static string Roman(int number)
    {
        if (number <= 0) return "";
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var letters = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < values.Length; i++)
            while (number >= values[i])
            {
                text.Append(letters[i]);
                number -= values[i];
            }
        return text.ToString();
    }

    /// <summary>
    /// A placeholder enemy for the coming battle: a few random types sharing its worth, which grows each battle.
    /// </summary>
    void MakeEnemy()
    {
        enemy.Clear();
        var random = new System.Random(unchecked(runSeed * 31 + Battle));
        var types = new List<UnitType>();
        foreach (var listing in Market.listings)
            types.Add(listing.type);
        int typeCount = Mathf.Clamp(random.Next(enemyTypes.x, enemyTypes.y + 1), 1, types.Count);
        var shares = new float[typeCount];
        float total = 0f;
        for (int i = 0; i < typeCount; i++)
        {
            int pick = random.Next(i, types.Count);
            (types[i], types[pick]) = (types[pick], types[i]);
            shares[i] = 0.5f + (float)random.NextDouble();
            total += shares[i];
        }
        float worth = enemyWorth.x + enemyWorth.y * (Battle - 1);
        for (int i = 0; i < typeCount; i++)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(worth * shares[i] / total / types[i].cost));
            enemy.Add(new EnemyGroup { type = types[i], count = count });
        }
        // in the list's order, so the enemy's rows line up with the market's
        enemy.Sort((a, b) => Market.IndexOf(a.type).CompareTo(Market.IndexOf(b.type)));
        var parts = new List<string>();
        foreach (var group in enemy)
            parts.Add(group.count + " " + group.type.name);
        Debug.Log($"[Trade] battle {Battle} enemy: {string.Join(", ", parts)}");
    }

    /// <summary>Events still to come in this phase that the timeline doesn't show yet.</summary>
    List<MarketEvent> Hidden()
    {
        hidden.Clear();
        foreach (var marketEvent in phaseEvents)
            if (marketEvent.time > PhaseTime && !IsKnown(marketEvent))
                hidden.Add(marketEvent);
        return hidden;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        var keyboard = Keyboard.current;
        bool input = keyboard != null && !GameInput.Blocked;

        // the timeline waits while the army is out at a raid
        if (Raiding)
            return;

        if (AtBattle)
        {
            if (!Fighting && input && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                // with nothing in the scene to fight it, the battle is skipped
                Debug.Log($"[Trade] battle {Battle} skipped (no battle in the scene)");
                if (Battle >= battlesPerRun) NewRun();
                else
                {
                    Battle++;
                    StartPhase();
                }
            }
            return;
        }

        // the timeline and the market run at the pace; the keys below stay in real time
        float phaseDt = dt * pace;
        float before = PhaseTime;
        PhaseTime = Mathf.Min(PhaseTime + phaseDt, phaseLength);
        foreach (var marketEvent in phaseEvents)
        {
            if (marketEvent.time > before && marketEvent.time <= PhaseTime)
            {
                Market.Start(marketEvent);
                Play(marketEvent.category == MarketEvent.Category.Invasion ? invasionClip : noticeClip);
                GiveNumber(marketEvent);
                EventStarted?.Invoke(marketEvent);
                Debug.Log($"[Trade] event at {marketEvent.place}: {marketEvent.text}");
            }
        }
        Market.Tick(phaseDt);
        UpdateRaids();

        if (input)
        {
            ReadArrows(keyboard);
            ReadKey(buy, sell, keyboard.leftShiftKey, dt);
            ReadKey(sell, buy, keyboard.rightShiftKey, dt);
        }
        RunCountdown(buy, dt);
        RunCountdown(sell, dt);
        if (Raiding)
            return;

        if (PhaseTime >= phaseLength)
        {
            // whatever is still pending goes through before the battle
            Complete(buy);
            Complete(sell);
            if (Raiding)
                return;
            RemoveRaidRow();
            AtBattle = true;
            Play(battleClip);
            Fighting = BattleReached != null;
            Debug.Log($"[Trade] battle {Battle} reached with cash {Cash}, army {ArmyText()}");
            BattleReached?.Invoke();
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
        int next = Mathf.Clamp(Selected + move, 0, RowCount - 1);
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
            else if (mine.side == Side.Sell && KindOf(Selected) != RowKind.Unit)
            {
                // tips, raids and scouting can only be bought; the sell key does nothing on them
                mine.spent = true;
            }
            else if (mine.state == OrderState.Counting && mine.row == Selected)
            {
                // each press adds one, and holding carries on counting from there, so taps and holds mix
                mine.state = OrderState.Holding;
                mine.heldFor = 0f;
                mine.countDebt = 0f;
                if (mine.count < Cap(mine.side, mine.row))
                {
                    mine.count++;
                    KeyMoved?.Invoke(mine.side, KeyMoment.Resume, mine.count);
                }
                else
                {
                    mine.capped = true;
                    KeyMoved?.Invoke(mine.side, KeyMoment.Capped, mine.count);
                }
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
                    mine.row = Selected;
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
        int cap = Cap(mine.side, mine.row);
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

    /// <summary>How many the order could reach now: what the cash buys (and, for tips, what is left to reveal), or what is held.</summary>
    public int Cap(Side side, int row)
    {
        switch (KindOf(row))
        {
            case RowKind.Unit:
                return side == Side.Buy ? Market.Affordable(row, Cash) : held[row];
            case RowKind.Tip:
                if (side == Side.Sell) return 0;
                return Mathf.Min(tipPrice > 0 ? Cash / tipPrice : 99, Hidden().Count);
            case RowKind.Raid:
                // the whole army goes, once
                return side == Side.Buy && RaidReached != null && ArmySize() > 0 ? 1 : 0;
            default:
                var group = EnemyAt(row);
                return side == Side.Buy && !group.scouted && Cash >= scoutPrice ? 1 : 0;
        }
    }

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
        int unit = order.row;
        if (KindOf(order.row) == RowKind.Tip)
        {
            var left = Hidden();
            int count = Mathf.Min(order.count, left.Count, tipPrice > 0 ? Cash / tipPrice : 99);
            for (int i = 0; i < count; i++)
            {
                int pick = UnityEngine.Random.Range(0, left.Count);
                var marketEvent = left[pick];
                left.RemoveAt(pick);
                revealed.Add(marketEvent);
                GiveNumber(marketEvent);
                Cash -= tipPrice;
                Debug.Log($"[Trade] tip: {marketEvent.text} (at {marketEvent.time:0} s)");
                Tipped?.Invoke(marketEvent);
            }
            Traded?.Invoke(Side.Buy, order.row, count);
        }
        else if (KindOf(order.row) == RowKind.Raid)
        {
            var raid = OpenRaid;
            bool go = order.side == Side.Buy && Cap(Side.Buy, order.row) > 0;
            Traded?.Invoke(Side.Buy, order.row, go ? 1 : 0);
            Reset(order);
            if (go)
                StartRaid(raid);
            return;
        }
        else if (KindOf(order.row) == RowKind.Enemy)
        {
            var group = EnemyAt(order.row);
            bool scout = !group.scouted && Cash >= scoutPrice;
            if (scout)
            {
                group.scouted = true;
                Cash -= scoutPrice;
                Debug.Log($"[Trade] scouted: the enemy has {group.count} {group.type.name}");
            }
            Traded?.Invoke(Side.Buy, order.row, scout ? 1 : 0);
        }
        else if (order.side == Side.Buy)
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

    /// <summary>For testing (the dev panel): trading is on, so the battle can be started now.</summary>
    public bool CanForceBattle => !AtBattle && !Raiding;

    /// <summary>For testing (the dev panel): jumps the timeline to the battle. Pending trades go through first, and
    /// events and raids still to come are skipped.</summary>
    public void ForceBattle()
    {
        if (!CanForceBattle)
            return;
        Debug.Log($"[Trade] battle {Battle} forced at {PhaseTime:0}s");
        PhaseTime = phaseLength;
    }

    void StartRaid(Raid raid)
    {
        RemoveRaidRow();
        CurrentRaid = raid;
        Raiding = true;
        Fighting = true;
        Debug.Log($"[Trade] the army rides out to the raid near {raid.place}: {ArmyText()} against {raid.BanditCount} bandits");
        RaidReached?.Invoke(raid);
    }

    /// <summary>After a raid: the survivors are the army now, a win pays, and the timeline carries on.</summary>
    public void EndRaid(IList<ArmyEntry> survivors, bool won)
    {
        if (!Raiding)
            return;
        SetArmy(survivors);
        int pay = won && CurrentRaid != null ? CurrentRaid.pay : 0;
        Cash += pay;
        Debug.Log($"[Trade] raid {(won ? "won" : "lost")}, paid {pay}, army now {ArmyText()}, cash {Cash}");
        Raiding = false;
        Fighting = false;
        CurrentRaid = null;
        RaidEnded?.Invoke(won, pay);
    }

    void SetArmy(IList<ArmyEntry> survivors)
    {
        for (int i = 0; i < held.Length; i++)
            held[i] = 0;
        foreach (var entry in survivors)
        {
            int index = Market.IndexOf(entry.type);
            if (index >= 0) held[index] += entry.count;
        }
    }

    static void Reset(Order order)
    {
        order.state = OrderState.Idle;
        order.count = 0;
        order.capped = false;
        order.heldFor = order.countDebt = order.countdownLeft = 0f;
    }

    /// <summary>The army held, as battle entries.</summary>
    public List<ArmyEntry> Army()
    {
        var army = new List<ArmyEntry>();
        for (int i = 0; i < held.Length; i++)
            if (held[i] > 0) army.Add(new ArmyEntry(Unit(i), held[i]));
        return army;
    }

    /// <summary>The coming battle's enemy army.</summary>
    public List<ArmyEntry> EnemyArmy()
    {
        var army = new List<ArmyEntry>();
        foreach (var group in enemy)
            army.Add(new ArmyEntry(group.type, group.count));
        return army;
    }

    /// <summary>What the enemy army is worth at cost.</summary>
    public int EnemyWorth()
    {
        int worth = 0;
        foreach (var group in enemy)
            worth += group.count * group.type.cost;
        return worth;
    }

    /// <summary>
    /// After a battle: the survivors are the army now. A win pays cash and trading starts towards the next battle
    /// (or, after the last, a new run); a loss ends the run.
    /// </summary>
    public void EndBattle(IList<ArmyEntry> survivors, bool won)
    {
        if (!AtBattle)
            return;
        SetArmy(survivors);
        int pay = won ? Mathf.RoundToInt(EnemyWorth() * winPay) : 0;
        Cash += pay;
        Debug.Log($"[Trade] battle {Battle} {(won ? "won" : "lost")}, paid {pay}, army now {ArmyText()}, cash {Cash}");
        BattleEnded?.Invoke(won, pay);
        if (!won)
        {
            Debug.Log("[Trade] the run is over");
            NewRun();
        }
        else if (Battle >= battlesPerRun)
        {
            Debug.Log("[Trade] every battle won: the run is over");
            NewRun();
        }
        else
        {
            Battle++;
            StartPhase();
        }
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
