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
        internal bool pendingShot;

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

    /// <summary>An oval patch of terrain.</summary>
    public struct Zone
    {
        public Vector2 centre;
        public Vector2 radii;
        public TerrainType terrain;

        /// <summary>0 at the centre, 1 on the edge, more outside.</summary>
        public float Distance(Vector2 point)
        {
            float x = (point.x - centre.x) / radii.x, y = (point.y - centre.y) / radii.y;
            return Mathf.Sqrt(x * x + y * y);
        }
    }

    [Serializable]
    public struct Settings
    {
        [Tooltip("Seconds between a unit's looks for a better target.")]
        public float retargetInterval;
        [Tooltip("A battle still going after this many seconds ends, and the side with more value left wins.")]
        public float maxDuration;
        [Tooltip("An army holding its ground goes for the enemy once any of them comes this close to one of its units. It also stops holding once one of its units has been shot.")]
        public float holdEngageDistance;


        [Header("High ground (per metre a unit stands above its target, negative below)")]
        [Tooltip("Added to a shooter's range, as a share of it, per metre of height over its target.")]
        public float heightRange;
        [Tooltip("Added to a shot's damage, as a share of it, per metre of height over its target.")]
        public float heightShotDamage;
        [Tooltip("Added to a melee hit's damage, as a share of it, per metre of height over its target.")]
        public float heightMeleeDamage;
        [Tooltip("Height differences count up to this many metres.")]
        public float heightLimit;
        [Tooltip("How much climbing slows a unit: speed is divided by 1 + this times the slope (rise over run).")]
        public float uphillSlowdown;
        [Tooltip("Climbing a slope steeper than this (rise over run) loses a charge.")]
        public float chargeMaxSlope;

        public static Settings Default => new Settings
        {
            retargetInterval = 0.5f, maxDuration = 180f, holdEngageDistance = 4f,
            heightRange = 0.06f, heightShotDamage = 0.08f, heightMeleeDamage = 0.05f, heightLimit = 5f,
            uphillSlowdown = 2f, chargeMaxSlope = 0.12f,
        };
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
    readonly List<Zone> zones = new List<Zone>();
    readonly Func<Vector2, float> heightAt;
    // a side that holds stands its ground until the enemy gets close or shoots it; then the whole side goes together
    readonly bool[] holding = new bool[2];

    /// <summary>True while a side is still standing its ground.</summary>
    public bool IsHolding(int side) => holding[side];

    /// <param name="zones">Terrain on the field, or null for open ground everywhere.</param>
    /// <param name="heightAt">Ground height at a point of the field, or null for flat ground.</param>
    /// <param name="holdA">Army A stands its ground instead of advancing.</param>
    /// <param name="holdB">Army B stands its ground instead of advancing.</param>
    public BattleSim(IList<ArmyEntry> armyA, IList<ArmyEntry> armyB, Rect zoneA, Rect zoneB, Rect bounds, int seed, Settings settings,
        IList<Zone> zones = null, bool holdA = false, bool holdB = false, Func<Vector2, float> heightAt = null)
    {
        this.settings = settings;
        this.heightAt = heightAt;
        this.bounds = bounds;
        random = new System.Random(seed);
        if (zones != null)
            foreach (var zone in zones)
                if (zone.terrain != null && zone.radii.x > 0f && zone.radii.y > 0f)
                    this.zones.Add(zone);
        Spawn(armyA, 0, zoneA, Vector2.right, holdA);
        Spawn(armyB, 1, zoneB, Vector2.left, holdB);
        CheckEnd();
    }

    void Spawn(IList<ArmyEntry> army, int side, Rect zone, Vector2 facing, bool holds)
    {
        holding[side] = holds;
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

    /// <summary>The terrain under a point, or null on open ground. Where zones overlap the first one wins.</summary>
    public TerrainType TerrainAt(Vector2 point)
    {
        for (int i = 0; i < zones.Count; i++)
            if (zones[i].Distance(point) < 1f)
                return zones[i].terrain;
        return null;
    }

    /// <summary>Ground height at a point of the field.</summary>
    public float HeightAt(Vector2 point) => heightAt != null ? heightAt(point) : 0f;

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
            {
                shot.target.pendingDamage += shot.damage;
                shot.target.pendingShot = true;
            }
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
                if (unit.pendingShot)
                    holding[unit.side] = false;
                if (unit.health <= 0f)
                {
                    unit.health = 0f;
                    unit.diedTime = Time;
                }
            }
            unit.pendingDamage = 0f;
            unit.pendingShot = false;
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
        var ground = TerrainAt(unit.position);
        var targetGround = TerrainAt(target.position);
        float height = HeightAt(unit.position);
        // how far above its target the unit stands; high ground helps, low ground hurts
        float above = Mathf.Clamp(height - HeightAt(target.position), -settings.heightLimit, settings.heightLimit);

        bool shooting = type.IsRanged && gap > type.tooCloseToShoot;
        float reach = type.meleeReach;
        if (shooting)
        {
            reach = type.range * (ground != null ? ground.shooterRange : 1f) * Mathf.Max(0.3f, 1f + settings.heightRange * above);
            if (targetGround != null && targetGround.hiddenBeyond > 0f)
                reach = Mathf.Min(reach, targetGround.hiddenBeyond);
        }

        unit.cooldown -= Step;
        unit.facing = direction;
        if (gap > reach)
        {
            if (holding[unit.side])
            {
                if (gap > settings.holdEngageDistance)
                    return;
                holding[unit.side] = false;
            }
            float speed = type.speed * (ground != null ? ground.SpeedFor(type) : 1f);
            float slope = (HeightAt(unit.position + direction * 0.5f) - height) / 0.5f;
            if (slope > 0f)
            {
                speed /= 1f + settings.uphillSlowdown * slope;
                if (slope > settings.chargeMaxSlope)
                    unit.movedSinceAttack = 0f;
            }
            float move = Mathf.Min(speed * Step, gap - reach + 0.01f);
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
            bool hits = random.NextDouble() < type.accuracy * (targetGround != null ? targetGround.cover : 1f);
            Vector2 end = target.position;
            if (!hits)
                end += new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 3f;
            projectiles.Add(new Projectile
            {
                from = unit, target = target, start = unit.position, end = end,
                duration = Mathf.Max(0.05f, distance / type.projectileSpeed), hits = hits,
                damage = damage * type.rangedDamage * Mathf.Max(0.3f, 1f + settings.heightShotDamage * above),
            });
        }
        else
        {
            unit.cooldown = type.meleeInterval;
            damage *= type.meleeDamage;
            bool chargeGround = (ground == null || ground.allowsCharge) && (targetGround == null || targetGround.allowsCharge);
            if (chargeGround && type.chargeMultiplier > 1f && unit.movedSinceAttack >= type.chargeDistance)
                damage *= type.chargeMultiplier;
            damage *= Mathf.Max(0.3f, 1f + settings.heightMeleeDamage * above);
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

    /// <summary>One line: who won, and how many of each type each side has left out of how many.</summary>
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
    public static BattleSim RunToEnd(IList<ArmyEntry> armyA, IList<ArmyEntry> armyB, Rect zoneA, Rect zoneB, Rect bounds, int seed, Settings settings,
        IList<Zone> zones = null, bool holdA = false, bool holdB = false, Func<Vector2, float> heightAt = null)
    {
        var sim = new BattleSim(armyA, armyB, zoneA, zoneB, bounds, seed, settings, zones, holdA, holdB, heightAt);
        while (sim.Winner == Running)
            sim.Tick();
        return sim;
    }
}
