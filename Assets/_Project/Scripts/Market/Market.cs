using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one market: a price per unit type, moved by events, by the player's own buying and selling, and by a little
/// drift. No graphics, so it can be run from an eval.
/// </summary>
/// <remarks>
/// Each type's price is its cost times a factor. The factor is pulled back towards a target (1, plus whatever the
/// running events add), so a price that buying pushed up drifts back down over time. An event's push rises over
/// <see cref="Settings.eventRise"/> seconds, then fades over <see cref="Settings.eventFade"/>: quick to move, slow
/// to settle, as the design asks.
/// </remarks>
public sealed class Market
{
    [Serializable]
    public struct Settings
    {
        [Tooltip("How far one unit bought raises its type's price factor (0.02 is 2 % of its cost).")]
        public float buyImpact;
        [Tooltip("How far one unit sold lowers its type's price factor.")]
        public float sellImpact;
        [Tooltip("How fast a price factor returns to its target, per second (0.05 closes about 5 % of the gap a second).")]
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
            buyImpact = 0.015f,
            sellImpact = 0.015f,
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
        public float factor = 1f;
        /// <summary>What running events add to the target factor right now.</summary>
        public float eventPush;
        /// <summary>Prices at past moments, oldest first, for the trend.</summary>
        internal readonly Queue<(float time, int price)> history = new Queue<(float, int)>();

        public int Price => Mathf.Max(1, Mathf.RoundToInt(type.cost * factor));
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
                listings.Add(new Listing { type = type });
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
            float target = 1f + listing.eventPush;
            // the gap closes by a share per second whatever the frame rate
            listing.factor += (target - listing.factor) * (1f - Mathf.Exp(-settings.pull * dt));
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

    /// <summary>How many units <paramref name="cash"/> buys now, counting the price rising with each one.</summary>
    public int Affordable(int index, int cash)
    {
        var listing = listings[index];
        float factor = listing.factor;
        int count = 0, spent = 0;
        while (count < 999)
        {
            int price = Mathf.Max(1, Mathf.RoundToInt(listing.type.cost * factor));
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
