using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one market: a price per unit type, moved by events, by the player's own buying and selling, and by a little
/// drift. No graphics, so it can be run from an eval.
/// </summary>
/// <remarks>
/// Each type's price is its cost times a factor plus whatever the running events add. The factor is pulled back
/// towards 1, so a price that the player's buying pushed up drifts back down over time. An event's push goes straight
/// onto the price: it rises over <see cref="Settings.eventRise"/> seconds, then fades over
/// <see cref="Settings.eventFade"/>, quick to move and slow to settle, as the design asks. (When the push only moved
/// the target the factor was pulled towards, the pull's lag kept a +50 % event to about +25 %, reached late.)
/// </remarks>
public sealed class Market
{
    [Serializable]
    public struct Settings
    {
        [Tooltip("How far one unit bought raises its type's price factor (0.06 is 6 % of its cost). High enough that holding a key to buy a lot visibly pumps the price.")]
        public float buyImpact;
        [Tooltip("How far one unit sold lowers its type's price factor.")]
        public float sellImpact;
        [Tooltip("How fast the player's own effect on a price wears off, per second (0.05 closes about 5 % of the gap a second). Events don't go through this.")]
        public float pull;
        [Tooltip("How hard the drift's speed is shaken, per square root of a second. Prices drift at a speed that " +
                 "itself changes slowly, so they run up or down for a while and turn, instead of jittering.")]
        public float drift;
        [Tooltip("How fast a drift's speed dies away, per second: lower gives longer runs before a price turns.")]
        public float driftTurn;
        [Tooltip("How far past the half-way mark a price's true value has to move before the shown price changes, in " +
                 "cash. Stops a price that sits on a boundary flicking back and forth; trades always show at once.")]
        public float priceHysteresis;
        [Tooltip("The price factor never goes outside this range.")]
        public Vector2 factorRange;
        [Tooltip("Seconds an event takes to reach its full push.")]
        public float eventRise;
        [Tooltip("Seconds an event's push then takes to fade away.")]
        public float eventFade;
        [Tooltip("A price is rising or falling compared with this many seconds ago.")]
        public float trendWindow;

        public static Settings Default => new Settings
        {
            buyImpact = 0.06f,
            sellImpact = 0.06f,
            pull = 0.05f,
            drift = 0.01f,
            driftTurn = 0.2f,
            priceHysteresis = 0.35f,
            factorRange = new Vector2(0.3f, 3f),
            eventRise = 4f,
            eventFade = 45f,
            trendWindow = 4f,
        };
    }

    public sealed class Listing
    {
        public UnitType type;
        /// <summary>The price factor without events: moved by trading and drift, pulled back towards 1.</summary>
        public float factor = 1f;
        /// <summary>What running events add to the price factor right now.</summary>
        public float eventPush;
        /// <summary>How fast drift is moving the factor now, per second.</summary>
        internal float driftSpeed;
        internal Vector2 range;

        /// <summary>The price factor with events, as the price uses it.</summary>
        public float Total => Mathf.Clamp(factor + eventPush, range.x, range.y);
        /// <summary>Prices at past moments, oldest first, for the trend.</summary>
        internal readonly Queue<(float time, int price)> history = new Queue<(float, int)>();

        /// <summary>The price shown and paid: the true price, held still until it moves clear of the last one shown
        /// (see <see cref="Settings.priceHysteresis"/>).</summary>
        public int Price => shown > 0 ? shown : Exact;
        int Exact => Mathf.Max(1, Mathf.RoundToInt(type.cost * Total));
        int shown;

        /// <summary>Moves the shown price to the true one once it's far enough away.</summary>
        internal void Settle(float hysteresis)
        {
            float raw = type.cost * Total;
            if (shown <= 0 || Mathf.Abs(raw - shown) >= 0.5f + hysteresis)
                shown = Exact;
        }

        /// <summary>Shows the true price now (after a trade, which should always show).</summary>
        internal void Snap() => shown = Exact;
        /// <summary>1 rising, -1 falling, 0 level, compared with the trend window ago.</summary>
        public int Trend { get; internal set; }
    }

    struct Running
    {
        public MarketEvent definition;
        public float started;
    }

    public Settings settings;
    public readonly List<Listing> listings = new List<Listing>();
    readonly List<Running> running = new List<Running>();
    readonly System.Random random;
    float time;

    public Market(IEnumerable<UnitType> types, Settings settings, int seed)
    {
        this.settings = settings;
        random = new System.Random(seed);
        foreach (var type in types)
            if (type != null)
                listings.Add(new Listing { type = type, range = settings.factorRange });
    }

    public Listing this[int index] => listings[index];

    public int IndexOf(UnitType type) => listings.FindIndex(l => l.type == type);

    public void Start(MarketEvent marketEvent)
    {
        running.Add(new Running { definition = marketEvent, started = time });
    }

    /// <summary>How far an event pushes, 0 to 1, this many seconds after it started.</summary>
    public float EventShape(float age)
    {
        if (age < 0f) return 0f;
        if (age < settings.eventRise) return age / Mathf.Max(0.01f, settings.eventRise);
        return Mathf.Clamp01(1f - (age - settings.eventRise) / Mathf.Max(0.01f, settings.eventFade));
    }

    /// <summary>How strongly a running event is still felt, 0 to 1, or 0 once it has faded.</summary>
    public float Strength(MarketEvent marketEvent)
    {
        foreach (var r in running)
            if (r.definition == marketEvent)
                return EventShape(time - r.started);
        return 0f;
    }

    /// <summary>How much of a running event is left: 1 until it peaks, then falling to 0 as it fades (0 once gone).</summary>
    public float Remaining(MarketEvent marketEvent)
    {
        foreach (var r in running)
            if (r.definition == marketEvent)
            {
                float age = time - r.started;
                return age < settings.eventRise ? 1f : EventShape(age);
            }
        return 0f;
    }

    public void Tick(float dt)
    {
        time += dt;
        foreach (var listing in listings)
            listing.eventPush = 0f;
        for (int i = running.Count - 1; i >= 0; i--)
        {
            var r = running[i];
            float age = time - r.started;
            if (age > settings.eventRise + settings.eventFade)
            {
                running.RemoveAt(i);
                continue;
            }
            int index = IndexOf(r.definition.affects);
            if (index >= 0)
                listings[index].eventPush += r.definition.strength * EventShape(age);
        }

        foreach (var listing in listings)
        {
            listing.range = settings.factorRange;
            // the gap closes by a share per second whatever the frame rate
            listing.factor += (1f - listing.factor) * (1f - Mathf.Exp(-settings.pull * dt));
            // the noise moves the drift's speed, not the price itself: shaken straight onto the price, it rocked
            // across a rounding line (40, 41, 40, 41) and looked like a glitch rather than a market
            listing.driftSpeed += -listing.driftSpeed * settings.driftTurn * dt + settings.drift * Mathf.Sqrt(dt) * Gaussian();
            listing.factor += listing.driftSpeed * dt;
            listing.factor = Mathf.Clamp(listing.factor, settings.factorRange.x, settings.factorRange.y);
            listing.Settle(settings.priceHysteresis);

            int price = listing.Price;
            listing.history.Enqueue((time, price));
            while (listing.history.Count > 1 && listing.history.Peek().time < time - settings.trendWindow)
                listing.history.Dequeue();
            int then = listing.history.Peek().price;
            listing.Trend = price > then ? 1 : price < then ? -1 : 0;
        }
    }

    /// <summary>Buys up to <paramref name="count"/> units one at a time, each at the price it has reached; returns how many and what they cost.</summary>
    public (int bought, int spent) Buy(int index, int count, int cash)
    {
        var listing = listings[index];
        int bought = 0, spent = 0;
        while (bought < count && listing.Price <= cash - spent)
        {
            // the first unit at the price shown, each after it at the price the buying has pushed it to
            spent += listing.Price;
            bought++;
            listing.factor = Mathf.Min(listing.factor + settings.buyImpact, settings.factorRange.y);
            listing.Snap();
        }
        return (bought, spent);
    }

    /// <summary>
    /// What trading <paramref name="count"/> units would cost (buying) or make (selling) now, each at the price the
    /// ones before it pushed it to, as <see cref="Buy"/> and <see cref="Sell"/> work it out. Changes nothing.
    /// </summary>
    public int Total(int index, int count, bool buying) => Total(index, (float)count, buying);

    /// <summary>As <see cref="Total(int, int, bool)"/>, for a number of units that may end in a part of one (selling a part-squad).</summary>
    public int Total(int index, float units, bool buying)
    {
        var listing = listings[index];
        float factor = listing.factor;
        float total = 0f;
        bool first = true;
        while (units > 1e-4f)
        {
            float share = Mathf.Min(1f, units);
            float push = Mathf.Clamp(factor + listing.eventPush, settings.factorRange.x, settings.factorRange.y);
            int price = first ? listing.Price : Mathf.Max(1, Mathf.RoundToInt(listing.type.cost * push));
            total += price * share;
            factor = buying
                ? Mathf.Min(factor + settings.buyImpact * share, settings.factorRange.y)
                : Mathf.Max(factor - settings.sellImpact * share, settings.factorRange.x);
            units -= share;
            first = false;
        }
        return Mathf.RoundToInt(total);
    }

    /// <summary>
    /// How many units a budget buys from a fresh market (factor 1, no events), counting the price rising with each
    /// one as it would for the player: how an enemy or bandit army worth that much is sized.
    /// </summary>
    public float UnitsFor(UnitType type, float budget)
    {
        float factor = 1f, units = 0f;
        while (budget > 0f && units < 999f)
        {
            float price = Mathf.Max(1f, type.cost * Mathf.Clamp(factor, settings.factorRange.x, settings.factorRange.y));
            if (budget < price)
            {
                units += budget / price;
                break;
            }
            budget -= price;
            units++;
            factor += settings.buyImpact;
        }
        return units;
    }

    /// <summary>Sells <paramref name="count"/> units one at a time, each at the price it has fallen to; returns the money made.</summary>
    public int Sell(int index, int count) => Sell(index, (float)count);

    /// <summary>
    /// Sells a number of units that may end in a part of one (a squad that lost men in battle): each whole unit at
    /// the price it has fallen to, the part at that share of the price, moving the price by that share.
    /// </summary>
    public int Sell(int index, float units)
    {
        var listing = listings[index];
        float made = 0f;
        while (units > 1e-4f)
        {
            float share = Mathf.Min(1f, units);
            made += listing.Price * share;
            listing.factor = Mathf.Max(listing.factor - settings.sellImpact * share, settings.factorRange.x);
            listing.Snap();
            units -= share;
        }
        return Mathf.RoundToInt(made);
    }

    /// <summary>
    /// Where a type's price will land once <paramref name="units"/> more are bought (or, negative, sold): each unit
    /// bought pushes it up and each sold pushes it down, so the list can show the price climbing while a key is held.
    /// </summary>
    public int PriceAfter(int index, int units)
    {
        var listing = listings[index];
        float factor = units >= 0
            ? Mathf.Min(listing.factor + settings.buyImpact * units, settings.factorRange.y)
            : Mathf.Max(listing.factor + settings.sellImpact * units, settings.factorRange.x);
        float total = Mathf.Clamp(factor + listing.eventPush, settings.factorRange.x, settings.factorRange.y);
        return Mathf.Max(1, Mathf.RoundToInt(listing.type.cost * total));
    }

    /// <summary>How many units <paramref name="cash"/> buys now, counting the price rising with each one.</summary>
    public int Affordable(int index, int cash)
    {
        var listing = listings[index];
        float factor = listing.factor;
        int count = 0, spent = 0;
        while (count < 999)
        {
            float total = Mathf.Clamp(factor + listing.eventPush, settings.factorRange.x, settings.factorRange.y);
            // the first unit at the price shown, as Buy charges it
            int price = count == 0 ? listing.Price : Mathf.Max(1, Mathf.RoundToInt(listing.type.cost * total));
            if (spent + price > cash) break;
            spent += price;
            count++;
            factor = Mathf.Min(factor + settings.buyImpact, settings.factorRange.y);
        }
        return count;
    }

    float Gaussian()
    {
        // Box-Muller
        double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
