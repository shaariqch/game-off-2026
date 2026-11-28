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
        [Tooltip("Random wander of the price factor, per square root of a second.")]
        public float drift;
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
            drift = 0.012f,
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
        internal Vector2 range;

        /// <summary>The price factor with events, as the price uses it.</summary>
        public float Total => Mathf.Clamp(factor + eventPush, range.x, range.y);
        /// <summary>Prices at past moments, oldest first, for the trend.</summary>
        internal readonly Queue<(float time, int price)> history = new Queue<(float, int)>();

        public int Price => Mathf.Max(1, Mathf.RoundToInt(type.cost * Total));
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
            listing.factor += settings.drift * Mathf.Sqrt(dt) * Gaussian();
            listing.factor = Mathf.Clamp(listing.factor, settings.factorRange.x, settings.factorRange.y);

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
            spent += listing.Price;
            bought++;
            listing.factor = Mathf.Min(listing.factor + settings.buyImpact, settings.factorRange.y);
        }
        return (bought, spent);
    }

    /// <summary>Sells <paramref name="count"/> units one at a time, each at the price it has fallen to; returns the money made.</summary>
    public int Sell(int index, int count)
    {
        var listing = listings[index];
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            made += listing.Price;
            listing.factor = Mathf.Max(listing.factor - settings.sellImpact, settings.factorRange.x);
        }
        return made;
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
            int price = Mathf.Max(1, Mathf.RoundToInt(listing.type.cost * total));
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
