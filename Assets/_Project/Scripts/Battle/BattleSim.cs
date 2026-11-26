using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The battle itself, with no graphics: two armies on a flat field (x across, y deep) that fight until one is gone.
/// <see cref="BattleView"/> shows it; <see cref="RunToEnd"/> plays one out instantly for testing match-ups.
/// </summary>
public sealed class BattleSim
{
    public const int Running = -1;
    public const int Draw = 2;
    public const float Step = 0.02f;

    public sealed class Unit
    {
        public int id;
        public int side;
        public UnitType type;
        public Vector2 position;
        public Vector2 facing;
        public float health;
        public Unit target;
        /// <summary>Sim time of the last attack, or -1. The view uses it for the lunge.</summary>
        public float lastAttackTime = -1f;
        public bool lastAttackRanged;
        /// <summary>Sim time of death, or -1 while alive.</summary>
        public float diedTime = -1f;
        internal float cooldown;
        internal float retargetIn;
        internal float movedSinceAttack;
        internal float pendingDamage;

        public bool Alive => diedTime < 0f;
    }

    public sealed class Projectile
    {
        public Unit from;
        public Unit target;
        public Vector2 start;
        public Vector2 end;
        public float age;
        public float duration;
        public bool hits;
        internal float damage;
    }

    [Serializable]
    public struct Settings
    {
        [Tooltip("Seconds between a unit's looks for a better target.")]
        public float retargetInterval;
        [Tooltip("A battle still going after this many seconds ends, and the side with more value left wins.")]
        public float maxDuration;

        public static Settings Default => new Settings { retargetInterval = 0.5f, maxDuration = 180f };
    }

    public readonly List<Unit> units = new List<Unit>();
    public readonly List<Projectile> projectiles = new List<Projectile>();
    public float Time { get; private set; }
    /// <summary><see cref="Running"/>, the winning side (0 or 1), or <see cref="Draw"/>.</summary>
    public int Winner { get; private set; } = Running;
    public bool TimedOut { get; private set; }

    readonly Settings settings;
    readonly Rect bounds;
    readonly System.Random random;

    public BattleSim(IList<ArmyEntry> armyA, IList<ArmyEntry> armyB, Rect zoneA, Rect zoneB, Rect bounds, int seed, Settings settings)
    {
        this.settings = settings;
        this.bounds = bounds;
        random = new System.Random(seed);
        Spawn(armyA, 0, zoneA, Vector2.right);
        Spawn(armyB, 1, zoneB, Vector2.left);
        CheckEnd();
    }

    void Spawn(IList<ArmyEntry> army, int side, Rect zone, Vector2 facing)
    {
        if (army == null)
            return;
        foreach (var entry in army)
        {
            if (entry.type == null)
                continue;
            for (int i = 0; i < entry.count; i++)
            {
                var unit = new Unit
                {
                    id = units.Count, side = side, type = entry.type, facing = facing, health = entry.type.health,
                    cooldown = (float)random.NextDouble() * 0.5f,
                    retargetIn = (float)random.NextDouble() * settings.retargetInterval,
                };
                // a few tries at a free spot; the separation pass sorts out the rest
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    unit.position = new Vector2(
                        Mathf.Lerp(zone.xMin, zone.xMax, (float)random.NextDouble()),
                        Mathf.Lerp(zone.yMin, zone.yMax, (float)random.NextDouble()));
                    if (IsFree(unit))
                        break;
                }
                units.Add(unit);
            }
        }
    }

    bool IsFree(Unit unit)
    {
        for (int i = 0; i < units.Count; i++)
        {
            float gap = unit.type.radius + units[i].type.radius;
            if ((units[i].position - unit.position).sqrMagnitude < gap * gap)
                return false;
        }
        return true;
    }

    public void Tick()
    {
        if (Winner != Running)
            return;
        Time += Step;

        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (!unit.Alive)
                continue;
            unit.retargetIn -= Step;
            if (unit.target == null || !unit.target.Alive || unit.retargetIn <= 0f)
            {
                unit.target = PickTarget(unit);
                unit.retargetIn = settings.retargetInterval;
            }
            if (unit.target != null)
                Act(unit);
        }

        for (int i = projectiles.Count - 1; i >= 0; i--)
        {
            var shot = projectiles[i];
            shot.age += Step;
            if (shot.age < shot.duration)
                continue;
            if (shot.hits && shot.target.Alive)
                shot.target.pendingDamage += shot.damage;
            projectiles.RemoveAt(i);
        }

        // damage lands after everyone has acted, so the order units are stored in gives neither side the first blow
        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (unit.pendingDamage <= 0f)
                continue;
            if (unit.Alive)
            {
                unit.health -= unit.pendingDamage;
                if (unit.health <= 0f)
                {
                    unit.health = 0f;
                    unit.diedTime = Time;
                }
            }
            unit.pendingDamage = 0f;
        }

        Separate();
        CheckEnd();
    }

    Unit PickTarget(Unit unit)
    {
        Unit best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < units.Count; i++)
        {
            var other = units[i];
            if (other.side == unit.side || !other.Alive)
                continue;
            float score = (other.position - unit.position).magnitude * unit.type.TargetWeight(other.type);
            if (score < bestScore)
            {
                bestScore = score;
                best = other;
            }
        }
        return best;
    }

    void Act(Unit unit)
    {
        var type = unit.type;
        var target = unit.target;
        Vector2 offset = target.position - unit.position;
        float distance = offset.magnitude;
        Vector2 direction = distance > 0.0001f ? offset / distance : unit.facing;
        float gap = distance - type.radius - target.type.radius;
        bool shooting = type.IsRanged && gap > type.tooCloseToShoot;
        float reach = shooting ? type.range : type.meleeReach;

        unit.cooldown -= Step;
        unit.facing = direction;
        if (gap > reach)
        {
            float move = Mathf.Min(type.speed * Step, gap - reach + 0.01f);
            unit.position += direction * move;
            unit.movedSinceAttack += move;
            return;
        }
        if (unit.cooldown > 0f)
            return;

        unit.lastAttackTime = Time;
        unit.lastAttackRanged = shooting;
        float damage = type.DamageAgainst(target.type);
        if (shooting)
        {
            unit.cooldown = type.rangedInterval;
            bool hits = random.NextDouble() < type.accuracy;
            Vector2 end = target.position;
            if (!hits)
                end += new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 3f;
            projectiles.Add(new Projectile
            {
                from = unit, target = target, start = unit.position, end = end,
                duration = Mathf.Max(0.05f, distance / type.projectileSpeed), hits = hits, damage = damage * type.rangedDamage,
            });
        }
        else
        {
            unit.cooldown = type.meleeInterval;
            damage *= type.meleeDamage;
            if (type.chargeMultiplier > 1f && unit.movedSinceAttack >= type.chargeDistance)
                damage *= type.chargeMultiplier;
            target.pendingDamage += damage;
        }
        unit.movedSinceAttack = 0f;
    }

    void Separate()
    {
        for (int i = 0; i < units.Count; i++)
        {
            var a = units[i];
            if (!a.Alive)
                continue;
            for (int j = i + 1; j < units.Count; j++)
            {
                var b = units[j];
                if (!b.Alive)
                    continue;
                float minimum = a.type.radius + b.type.radius;
                Vector2 offset = b.position - a.position;
                float squared = offset.sqrMagnitude;
                if (squared >= minimum * minimum)
                    continue;
                float distance = Mathf.Sqrt(squared);
                // two units on exactly the same spot get pushed apart along a fixed direction
                Vector2 push = distance > 0.0001f ? offset / distance : new Vector2(0.7071f, 0.7071f);
                float half = (minimum - distance) * 0.5f;
                a.position -= push * half;
                b.position += push * half;
            }
            a.position = new Vector2(
                Mathf.Clamp(a.position.x, bounds.xMin, bounds.xMax),
                Mathf.Clamp(a.position.y, bounds.yMin, bounds.yMax));
        }
    }

    void CheckEnd()
    {
        int aliveA = 0, aliveB = 0;
        for (int i = 0; i < units.Count; i++)
        {
            if (!units[i].Alive)
                continue;
            if (units[i].side == 0) aliveA++;
            else aliveB++;
        }
        if (aliveA == 0 && aliveB == 0) Winner = Draw;
        else if (aliveB == 0) Winner = 0;
        else if (aliveA == 0) Winner = 1;
        else if (Time >= settings.maxDuration)
        {
            TimedOut = true;
            int valueA = ValueLeft(0), valueB = ValueLeft(1);
            Winner = valueA == valueB ? Draw : valueA > valueB ? 0 : 1;
        }
    }

    /// <summary>The cost of every unit a side started with.</summary>
    public int ValueStarted(int side)
    {
        int value = 0;
        for (int i = 0; i < units.Count; i++)
            if (units[i].side == side)
                value += units[i].type.cost;
        return value;
    }

    /// <summary>The cost of the units a side still has alive.</summary>
    public int ValueLeft(int side)
    {
        int value = 0;
        for (int i = 0; i < units.Count; i++)
            if (units[i].side == side && units[i].Alive)
                value += units[i].type.cost;
        return value;
    }

    /// <summary>One line per side: who won, and how many of each type are left out of how many.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.Append(Winner == Running ? "Battle running" : Winner == Draw ? "Draw" : Winner == 0 ? "Army A wins" : "Army B wins");
        if (TimedOut)
            text.Append(" (timed out)");
        text.Append(" after ").Append(Time.ToString("0")).Append(" s");
        for (int side = 0; side < 2; side++)
        {
            text.Append(side == 0 ? ". A: " : ". B: ");
            var seen = new List<UnitType>();
            for (int i = 0; i < units.Count; i++)
            {
                var type = units[i].type;
                if (units[i].side != side || seen.Contains(type))
                    continue;
                seen.Add(type);
                int started = 0, left = 0;
                for (int j = 0; j < units.Count; j++)
                {
                    if (units[j].side != side || units[j].type != type)
                        continue;
                    started++;
                    if (units[j].Alive) left++;
                }
                text.Append(type.name).Append(' ').Append(left).Append('/').Append(started).Append(", ");
            }
            text.Append("value ").Append(ValueLeft(side)).Append('/').Append(ValueStarted(side));
        }
        return text.ToString();
    }

    /// <summary>Plays a whole battle with no graphics and returns it finished.</summary>
    public static BattleSim RunToEnd(IList<ArmyEntry> armyA, IList<ArmyEntry> armyB, Rect zoneA, Rect zoneB, Rect bounds, int seed, Settings settings)
    {
        var sim = new BattleSim(armyA, armyB, zoneA, zoneB, bounds, seed, settings);
        while (sim.Winner == Running)
            sim.Tick();
        return sim;
    }
}
