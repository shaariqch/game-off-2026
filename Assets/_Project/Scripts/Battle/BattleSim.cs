using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The battle itself, with no graphics: two armies on a field (x across, y deep) that fight until one is gone.
/// Each army deploys in formation (its line in front, ranged behind, cavalry on the wings) and the line marches
/// as one until it meets the enemy. Cavalry wait on the wings, then ride around the enemy's flank and charge.
/// <see cref="BattleView"/> shows it; <see cref="RunToEnd"/> plays one out instantly for testing match-ups.
/// </summary>
public sealed class BattleSim
{
    public const int Running = -1;
    public const int Draw = 2;
    public const float Step = 0.02f;

    public enum CavalryState
    {
        /// <summary>On its wing, keeping its place in the formation.</summary>
        Waiting,
        /// <summary>Riding wide around the enemy's flank.</summary>
        Flanking,
        /// <summary>Riding straight at its target.</summary>
        Charging,
        /// <summary>Fighting where it landed.</summary>
        Melee,
        /// <summary>Riding clear of the fight to charge again.</summary>
        Wheeling,
    }

    public enum EventKind
    {
        /// <summary>The first blow between the two battle lines.</summary>
        Contact,
        /// <summary>A cavalry charge landed and knocked its target back.</summary>
        Charge,
        /// <summary>A cavalry charge broke on braced spears.</summary>
        BracedCharge,
        /// <summary>A melee hit.</summary>
        Hit,
        ArrowHit,
        ArrowMiss,
        Death,
        /// <summary>The battle is decided.</summary>
        End,
    }

    /// <summary>Something that happened during the last <see cref="Tick"/>, for the view to show.</summary>
    public struct Event
    {
        public EventKind kind;
        public Vector2 position;
        /// <summary>Who did it (the attacker, the shooter, the unit that died).</summary>
        public Unit unit;
        /// <summary>Who it was done to, if anyone.</summary>
        public Unit other;
        public float damage;
        /// <summary>A melee hit from behind.</summary>
        public bool rear;
    }

    public sealed class Unit
    {
        public int id;
        public int side;
        public UnitType type;
        public Vector2 position;
        public Vector2 facing;
        public float health;
        public Unit target;
        /// <summary>Place in its army's formation: x forward of the formation's centre, y along the line.</summary>
        public Vector2 slot;
        public CavalryState cavalry;
        /// <summary>Sim time a stagger ends; until then the unit can't move or attack.</summary>
        public float stunnedUntil = -1f;
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
        internal Vector2 waypoint;
        internal float stateSince;
        internal bool inSlot;

        public bool Alive => diedTime < 0f;
    }

    /// <summary>Where an army's formation stands. Units keep to their slots in it until they engage.</summary>
    public sealed class Formation
    {
        public Vector2 anchor;
        /// <summary>Towards the enemy.</summary>
        public Vector2 forward;
        public Vector2 lateral;
        /// <summary>Standing its ground until the enemy comes close or shoots it.</summary>
        public bool holding;
        /// <summary>Stopped because its line is fighting.</summary>
        public bool halted;
        /// <summary>Sim time its line first fought, or -1.</summary>
        public float contactTime = -1f;
        /// <summary>The line has been fighting long enough to break up: every unit goes for the nearest enemy.</summary>
        public bool broken;
        internal bool engagedNow;

        public Vector2 SlotPosition(Vector2 slot) => anchor + forward * slot.x + lateral * slot.y;
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

        [Header("Formation")]
        [Tooltip("Metres between units side by side in a rank.")]
        public float fileSpacing;
        [Tooltip("Metres between one rank and the next.")]
        public float rankSpacing;
        [Tooltip("Most units side by side in one rank.")]
        public int maxFrontage;
        [Tooltip("Metres between the back of the line and the ranged units behind it.")]
        public float rangedGap;
        [Tooltip("Metres between the end of the line and the cavalry on each wing.")]
        public float wingGap;
        [Tooltip("The line marches at this share of its slowest unit's speed.")]
        public float marchSpeed;
        [Tooltip("The formation waits when its units fall this many metres behind their places, on a slope or in a marsh.")]
        public float maxSlotLag;
        [Tooltip("A unit leaves its place in the formation for an enemy this close (gap between bodies).")]
        public float engageDistance;
        [Tooltip("Seconds after its line first meets the enemy that a formation breaks up and every unit goes for the nearest enemy.")]
        public float generalMeleeAfter;

        [Header("Cavalry")]
        [Tooltip("Cavalry ride out once the lines meet, an enemy comes this close, or after Cavalry Wait seconds.")]
        public float cavalryEngageDistance;
        public float cavalryWait;
        [Tooltip("How far outside the enemy's ranks cavalry ride on their way round.")]
        public float flankMargin;
        [Tooltip("How far past their target, seen from their own side, cavalry swing in before charging.")]
        public float flankDepth;
        [Tooltip("Seconds cavalry fight where they landed before riding clear to charge again, unless they're pinned.")]
        public float cavalryMeleeTime;
        [Tooltip("How far cavalry ride clear before turning to charge again.")]
        public float wheelDistance;
        [Tooltip("Metres a charge knocks its target back, and seconds the target is staggered. Units near the target get some of it.")]
        public float chargeKnockback;
        public float chargeStagger;
        public float chargeSplashRadius;
        [Tooltip("Seconds a rider is staggered after charging braced spears from the front.")]
        public float braceStagger;
        [Tooltip("Melee damage multiplier for a hit from behind.")]
        public float rearMeleeDamage;
        [Tooltip("Every hit's damage varies by up to this share either way. Without it the number of hits to kill is fixed, and close match-ups tip all one way.")]
        public float damageSpread;

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
            fileSpacing = 1.6f, rankSpacing = 1.6f, maxFrontage = 8, rangedGap = 2.5f, wingGap = 3f,
            marchSpeed = 0.85f, maxSlotLag = 1.2f, engageDistance = 3f, generalMeleeAfter = 7f,
            cavalryEngageDistance = 6f, cavalryWait = 9f, flankMargin = 3f, flankDepth = 4f,
            cavalryMeleeTime = 2.5f, wheelDistance = 9f,
            chargeKnockback = 1.6f, chargeStagger = 0.7f, chargeSplashRadius = 1.6f, braceStagger = 0.8f,
            rearMeleeDamage = 1.35f, damageSpread = 0.15f,
            heightRange = 0.06f, heightShotDamage = 0.08f, heightMeleeDamage = 0.05f, heightLimit = 5f,
            uphillSlowdown = 2f, chargeMaxSlope = 0.12f,
        };
    }

    public readonly List<Unit> units = new List<Unit>();
    public readonly List<Projectile> projectiles = new List<Projectile>();
    /// <summary>What happened during the last <see cref="Tick"/>. Cleared at the start of each one.</summary>
    public readonly List<Event> events = new List<Event>();
    public readonly Formation[] formations = new Formation[2];
    public float Time { get; private set; }
    /// <summary><see cref="Running"/>, the winning side (0 or 1), or <see cref="Draw"/>.</summary>
    public int Winner { get; private set; } = Running;
    public bool TimedOut { get; private set; }

    readonly Settings settings;
    readonly Rect bounds;
    readonly System.Random random;
    readonly List<Zone> zones = new List<Zone>();
    readonly Func<Vector2, float> heightAt;
    readonly int[] lineAlive = new int[2];
    readonly int[] footAlive = new int[2];
    readonly float[] marchSpeed = new float[2];
    bool contactSeen;
    Vector2 lastDeath;

    /// <summary>True while a side is still standing its ground.</summary>
    public bool IsHolding(int side) => formations[side].holding;

    /// <param name="zoneA">Army A's formation is centred on this area's centre.</param>
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
        // each army faces the other
        Vector2 towardsB = zoneB.center - zoneA.center;
        towardsB = towardsB.sqrMagnitude > 0.01f ? towardsB.normalized : Vector2.right;
        Spawn(armyA, 0, zoneA.center, towardsB, holdA);
        Spawn(armyB, 1, zoneB.center, -towardsB, holdB);
        CheckEnd();
    }

    void Spawn(IList<ArmyEntry> army, int side, Vector2 centre, Vector2 forward, bool holds)
    {
        var formation = new Formation { anchor = centre, forward = forward, lateral = new Vector2(-forward.y, forward.x), holding = holds };
        formations[side] = formation;
        var army_ = new List<Unit>();
        if (army != null)
            foreach (var entry in army)
            {
                if (entry.type == null)
                    continue;
                for (int i = 0; i < entry.count; i++)
                    army_.Add(new Unit
                    {
                        side = side, type = entry.type, facing = forward, health = entry.type.health,
                        cooldown = (float)random.NextDouble() * 0.5f,
                        retargetIn = (float)random.NextDouble() * settings.retargetInterval,
                    });
            }
        Deploy(army_);
        foreach (var unit in army_)
        {
            var jitter = new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 0.3f;
            unit.position = ClampToField(formation.SlotPosition(unit.slot) + jitter);
            unit.id = units.Count;
            units.Add(unit);
        }
    }

    /// <summary>Gives every unit its place: the line in ranks at the front, ranged behind it, cavalry on the wings.</summary>
    void Deploy(List<Unit> army)
    {
        var line = army.FindAll(u => u.type.role == UnitType.Role.Line);
        var ranged = army.FindAll(u => u.type.role == UnitType.Role.Ranged);
        var cavalry = army.FindAll(u => u.type.role == UnitType.Role.Cavalry);
        Comparison<Unit> byRank = (a, b) => a.type.rank != b.type.rank ? a.type.rank.CompareTo(b.type.rank) : a.id.CompareTo(b.id);
        for (int i = 0; i < army.Count; i++)
            army[i].id = i;
        line.Sort(byRank);
        ranged.Sort(byRank);

        float depth = 0f;
        float halfWidth = PlaceBlock(line, ref depth);
        if (line.Count > 0 && ranged.Count > 0)
            depth -= settings.rangedGap - settings.rankSpacing;
        halfWidth = Mathf.Max(halfWidth, PlaceBlock(ranged, ref depth));
        // cavalry in pairs on alternate wings, starting level with the front rank
        float wingStart = (line.Count + ranged.Count > 0 ? halfWidth + settings.wingGap : 0.5f * settings.fileSpacing);
        for (int i = 0; i < cavalry.Count; i++)
        {
            float wing = i % 2 == 0 ? 1f : -1f;
            int place = i / 2;
            cavalry[i].slot = new Vector2(-(place / 2) * settings.rankSpacing * 1.4f, wing * (wingStart + (place % 2) * settings.fileSpacing * 1.3f));
            depth = Mathf.Min(depth, cavalry[i].slot.x - settings.rankSpacing);
        }
        // centre the whole formation on its anchor, front to back
        float shift = -(depth + settings.rankSpacing) * 0.5f;
        foreach (var unit in army)
            unit.slot.x += shift;
    }

    /// <summary>Lays units out in ranks from <paramref name="depth"/> backwards and moves it past them. Returns half the block's width.</summary>
    float PlaceBlock(List<Unit> block, ref float depth)
    {
        if (block.Count == 0)
            return 0f;
        int frontage = Mathf.Max(1, Mathf.Min(block.Count, settings.maxFrontage));
        int ranks = (block.Count + frontage - 1) / frontage;
        for (int i = 0; i < block.Count; i++)
        {
            int rank = i / frontage, file = i % frontage;
            int inRank = Mathf.Min(frontage, block.Count - rank * frontage);
            block[i].slot = new Vector2(depth - rank * settings.rankSpacing, (file - (inRank - 1) * 0.5f) * settings.fileSpacing);
        }
        depth -= ranks * settings.rankSpacing;
        return (frontage - 1) * 0.5f * settings.fileSpacing;
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
        events.Clear();
        if (Winner != Running)
            return;
        Time += Step;

        CountForces();
        for (int side = 0; side < 2; side++)
            MoveFormation(side);

        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (!unit.Alive)
                continue;
            unit.cooldown -= Step;
            if (Time < unit.stunnedUntil)
                continue;
            unit.retargetIn -= Step;
            if (unit.target == null || !unit.target.Alive || unit.retargetIn <= 0f)
            {
                // charging cavalry stay on the target they chose
                if (unit.target == null || !unit.target.Alive || unit.cavalry != CavalryState.Charging && unit.cavalry != CavalryState.Flanking)
                    unit.target = PickTarget(unit);
                unit.retargetIn = settings.retargetInterval;
            }
            if (unit.target == null)
                continue;
            // set again by MoveToSlot if the unit spends this step heading for its place
            unit.inSlot = false;
            if (unit.type.IsCavalry)
                ActCavalry(unit);
            else
                ActFoot(unit);
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
                events.Add(new Event { kind = EventKind.ArrowHit, position = shot.target.position, unit = shot.from, other = shot.target, damage = shot.damage });
            }
            else
                events.Add(new Event { kind = EventKind.ArrowMiss, position = shot.end, unit = shot.from });
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
                    formations[unit.side].holding = false;
                if (unit.health <= 0f)
                {
                    unit.health = 0f;
                    unit.diedTime = Time;
                    lastDeath = unit.position;
                    events.Add(new Event { kind = EventKind.Death, position = unit.position, unit = unit });
                }
            }
            unit.pendingDamage = 0f;
            unit.pendingShot = false;
        }

        Separate();
        CheckEnd();
    }

    void CountForces()
    {
        for (int side = 0; side < 2; side++)
        {
            lineAlive[side] = footAlive[side] = 0;
            marchSpeed[side] = float.MaxValue;
        }
        foreach (var unit in units)
        {
            if (!unit.Alive || unit.type.IsCavalry)
                continue;
            footAlive[unit.side]++;
            if (unit.type.role == UnitType.Role.Line)
                lineAlive[unit.side]++;
            marchSpeed[unit.side] = Mathf.Min(marchSpeed[unit.side], unit.type.speed);
        }
    }

    /// <summary>Marches a formation towards the enemy unless it's holding, fighting or waiting for stragglers.</summary>
    void MoveFormation(int side)
    {
        var formation = formations[side];
        formation.halted = formation.engagedNow;
        formation.engagedNow = false;
        if (formation.contactTime >= 0f && Time - formation.contactTime > settings.generalMeleeAfter)
            formation.broken = true;
        if (formation.holding || formation.halted || formation.broken || footAlive[side] == 0)
            return;
        // how far units heading for their places are behind them
        float lag = 0f;
        int counted = 0;
        foreach (var unit in units)
        {
            if (unit.side != side || !unit.Alive || unit.type.IsCavalry || !unit.inSlot)
                continue;
            lag += Vector2.Dot(formation.SlotPosition(unit.slot) - unit.position, formation.forward);
            counted++;
        }
        if (counted > 0 && lag / counted > settings.maxSlotLag)
            return;
        formation.anchor += formation.forward * marchSpeed[side] * settings.marchSpeed * Step;
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

    /// <summary>The closest living enemy, and the gap between their bodies.</summary>
    Unit Nearest(Unit unit, out float gap)
    {
        Unit best = null;
        gap = float.MaxValue;
        for (int i = 0; i < units.Count; i++)
        {
            var other = units[i];
            if (other.side == unit.side || !other.Alive)
                continue;
            float g = (other.position - unit.position).magnitude - unit.type.radius - other.type.radius;
            if (g < gap)
            {
                gap = g;
                best = other;
            }
        }
        return best;
    }

    float Gap(Unit a, Unit b) => (b.position - a.position).magnitude - a.type.radius - b.type.radius;

    /// <summary>How far this unit can hit its target from: its range if it shoots, else its melee reach.</summary>
    float Reach(Unit unit, Unit target, float gap, out bool shooting)
    {
        var type = unit.type;
        shooting = type.IsRanged && gap > type.tooCloseToShoot;
        if (!shooting)
            return type.meleeReach;
        var ground = TerrainAt(unit.position);
        var targetGround = TerrainAt(target.position);
        float above = Mathf.Clamp(HeightAt(unit.position) - HeightAt(target.position), -settings.heightLimit, settings.heightLimit);
        float reach = type.range * (ground != null ? ground.shooterRange : 1f) * Mathf.Max(0.3f, 1f + settings.heightRange * above);
        if (targetGround != null && targetGround.hiddenBeyond > 0f)
            reach = Mathf.Min(reach, targetGround.hiddenBeyond);
        return reach;
    }

    void ActFoot(Unit unit)
    {
        var formation = formations[unit.side];
        bool line = unit.type.role == UnitType.Role.Line;
        // in formation, the nearest enemy comes first once it's close
        if (!formation.broken)
        {
            var near = Nearest(unit, out float nearGap);
            if (near != null && near != unit.target && nearGap <= settings.engageDistance)
                unit.target = near;
        }
        var target = unit.target;
        float gap = Gap(unit, target);
        float reach = Reach(unit, target, gap, out bool shooting);

        if (gap <= reach)
        {
            if (line || lineAlive[unit.side] == 0)
                formation.engagedNow = true;
            Face(unit, target.position - unit.position);
            Attack(unit, target, shooting);
            return;
        }
        if (formation.holding)
        {
            if (gap > settings.holdEngageDistance)
            {
                MoveToSlot(unit);
                return;
            }
            formation.holding = false;
        }
        if (formation.broken || gap <= settings.engageDistance)
        {
            if (line)
                formation.engagedNow = true;
            MoveTowards(unit, target.position - unit.position, gap - reach + 0.01f);
            return;
        }
        MoveToSlot(unit);
    }

    void ActCavalry(Unit unit)
    {
        var formation = formations[unit.side];
        var target = unit.target;
        float gap = Gap(unit, target);
        switch (unit.cavalry)
        {
            case CavalryState.Waiting:
            {
                Nearest(unit, out float nearGap);
                if (formation.holding)
                {
                    if (nearGap > settings.holdEngageDistance)
                    {
                        MoveToSlot(unit);
                        return;
                    }
                    formation.holding = false;
                }
                bool go = formations[0].contactTime >= 0f || formations[1].contactTime >= 0f || formation.broken
                    || Time >= settings.cavalryWait || nearGap <= settings.cavalryEngageDistance || footAlive[unit.side] == 0;
                if (!go)
                {
                    MoveToSlot(unit);
                    return;
                }
                StartFlank(unit);
                return;
            }
            case CavalryState.Flanking:
            {
                // anyone in the way gets charged instead
                var near = Nearest(unit, out float nearGap);
                if (near != null && nearGap <= settings.engageDistance)
                {
                    unit.target = near;
                    SetState(unit, CavalryState.Charging);
                    return;
                }
                Vector2 toWaypoint = unit.waypoint - unit.position;
                if (toWaypoint.magnitude < 1.5f || Time - unit.stateSince > 7f)
                {
                    SetState(unit, CavalryState.Charging);
                    return;
                }
                MoveTowards(unit, toWaypoint, toWaypoint.magnitude);
                return;
            }
            case CavalryState.Charging:
            {
                float reach = unit.type.meleeReach;
                if (gap <= reach)
                {
                    Face(unit, target.position - unit.position);
                    // the charge lands with the first blow, so wait for it
                    if (unit.cooldown > 0f)
                        return;
                    Attack(unit, target, false);
                    SetState(unit, CavalryState.Melee);
                    return;
                }
                MoveTowards(unit, target.position - unit.position, gap - reach + 0.01f);
                return;
            }
            case CavalryState.Melee:
            {
                var near = Nearest(unit, out float nearGap);
                if (near != null && nearGap < gap)
                {
                    unit.target = target = near;
                    gap = nearGap;
                }
                // ride clear and come again, unless surrounded
                bool pinned = CountEnemiesWithin(unit, 0.8f) >= 2;
                if (!pinned && (Time - unit.stateSince > settings.cavalryMeleeTime || gap > settings.engageDistance))
                {
                    Vector2 away = unit.facing + Vector2.Perpendicular(unit.facing) * ((float)random.NextDouble() - 0.5f) * 0.8f;
                    unit.waypoint = ClampToField(unit.position + away.normalized * settings.wheelDistance, 2f);
                    SetState(unit, CavalryState.Wheeling);
                    return;
                }
                if (gap <= unit.type.meleeReach)
                {
                    Face(unit, target.position - unit.position);
                    Attack(unit, target, false);
                }
                else
                    MoveTowards(unit, target.position - unit.position, gap - unit.type.meleeReach + 0.01f);
                return;
            }
            case CavalryState.Wheeling:
            {
                Vector2 toWaypoint = unit.waypoint - unit.position;
                if (toWaypoint.magnitude < 1.2f || Time - unit.stateSince > 3f)
                {
                    unit.target = PickTarget(unit);
                    SetState(unit, CavalryState.Charging);
                    return;
                }
                MoveTowards(unit, toWaypoint, toWaypoint.magnitude);
                return;
            }
        }
    }

    void SetState(Unit unit, CavalryState state)
    {
        unit.cavalry = state;
        unit.stateSince = Time;
    }

    /// <summary>Picks a target and a point beside and behind it, outside the enemy's ranks on this unit's wing.</summary>
    void StartFlank(Unit unit)
    {
        var target = PickTarget(unit);
        if (target == null)
            return;
        unit.target = target;
        var formation = formations[unit.side];
        Vector2 outward = formation.lateral * (unit.slot.y >= 0f ? 1f : -1f);
        // how far the enemy's foot soldiers reach out towards this wing, measured from the target
        float extent = 0f;
        foreach (var other in units)
            if (other.side != unit.side && other.Alive && !other.type.IsCavalry)
                extent = Mathf.Max(extent, Vector2.Dot(other.position - target.position, outward));
        unit.waypoint = ClampToField(target.position + outward * (extent + settings.flankMargin) + formation.forward * settings.flankDepth, 1.5f);
        SetState(unit, CavalryState.Flanking);
    }

    int CountEnemiesWithin(Unit unit, float gap)
    {
        int count = 0;
        foreach (var other in units)
            if (other.side != unit.side && other.Alive && Gap(unit, other) <= gap)
                count++;
        return count;
    }

    /// <summary>Walks a unit to its place in the formation, then faces it forward.</summary>
    void MoveToSlot(Unit unit)
    {
        var formation = formations[unit.side];
        Vector2 offset = formation.SlotPosition(unit.slot) - unit.position;
        float distance = offset.magnitude;
        if (distance > 0.05f)
            MoveTowards(unit, offset, distance);
        unit.inSlot = true;
        if (distance < 0.5f)
            unit.facing = formation.forward;
    }

    /// <summary>Moves a unit up to <paramref name="limit"/> metres along a direction, slowed by ground and slope.</summary>
    void MoveTowards(Unit unit, Vector2 direction, float limit)
    {
        unit.inSlot = false;
        float length = direction.magnitude;
        if (length < 0.0001f || limit <= 0f)
            return;
        direction /= length;
        unit.facing = direction;
        var type = unit.type;
        var ground = TerrainAt(unit.position);
        float height = HeightAt(unit.position);
        float speed = type.speed * (ground != null ? ground.SpeedFor(type) : 1f);
        float slope = (HeightAt(unit.position + direction * 0.5f) - height) / 0.5f;
        if (slope > 0f)
        {
            speed /= 1f + settings.uphillSlowdown * slope;
            if (slope > settings.chargeMaxSlope)
                unit.movedSinceAttack = 0f;
        }
        float move = Mathf.Min(speed * Step, limit);
        unit.position += direction * move;
        unit.movedSinceAttack += move;
    }

    static void Face(Unit unit, Vector2 direction)
    {
        if (direction.sqrMagnitude > 0.0001f)
            unit.facing = direction.normalized;
    }

    void Attack(Unit unit, Unit target, bool shooting)
    {
        if (unit.cooldown > 0f)
            return;
        var type = unit.type;
        var ground = TerrainAt(unit.position);
        var targetGround = TerrainAt(target.position);
        float above = Mathf.Clamp(HeightAt(unit.position) - HeightAt(target.position), -settings.heightLimit, settings.heightLimit);
        Vector2 offset = target.position - unit.position;
        float distance = offset.magnitude;
        Vector2 direction = distance > 0.0001f ? offset / distance : unit.facing;

        unit.lastAttackTime = Time;
        unit.lastAttackRanged = shooting;
        float damage = type.DamageAgainst(target.type) * (1f + ((float)random.NextDouble() * 2f - 1f) * settings.damageSpread);
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
            unit.movedSinceAttack = 0f;
            return;
        }

        unit.cooldown = type.meleeInterval;
        damage *= type.meleeDamage;
        // the target faces whoever it's fighting; a blow from behind it lands harder
        bool rear = Vector2.Dot(target.facing, -direction) < -0.25f;
        if (rear)
            damage *= settings.rearMeleeDamage;
        bool chargeGround = (ground == null || ground.allowsCharge) && (targetGround == null || targetGround.allowsCharge);
        bool charging = chargeGround && type.chargeMultiplier > 1f && unit.movedSinceAttack >= type.chargeDistance;
        var kind = EventKind.Hit;
        if (charging)
        {
            bool braced = target.type.bracesCharge && Time >= target.stunnedUntil && Vector2.Dot(target.facing, -direction) > 0.2f;
            if (braced)
            {
                kind = EventKind.BracedCharge;
                unit.stunnedUntil = Time + settings.braceStagger;
                Shove(unit, -direction, settings.chargeKnockback * 0.6f);
            }
            else
            {
                kind = EventKind.Charge;
                damage *= type.chargeMultiplier;
                Shove(target, direction, settings.chargeKnockback);
                target.stunnedUntil = Mathf.Max(target.stunnedUntil, Time + settings.chargeStagger);
                foreach (var other in units)
                {
                    if (other == target || other.side != target.side || !other.Alive)
                        continue;
                    if ((other.position - target.position).sqrMagnitude > settings.chargeSplashRadius * settings.chargeSplashRadius)
                        continue;
                    Shove(other, direction, settings.chargeKnockback * 0.5f);
                    other.stunnedUntil = Mathf.Max(other.stunnedUntil, Time + settings.chargeStagger * 0.6f);
                }
            }
        }
        damage *= Mathf.Max(0.3f, 1f + settings.heightMeleeDamage * above);
        target.pendingDamage += damage;
        unit.movedSinceAttack = 0f;
        events.Add(new Event { kind = kind, position = target.position, unit = unit, other = target, damage = damage, rear = rear });

        // the battle lines have met
        if (type.role == UnitType.Role.Line && formations[unit.side].contactTime < 0f)
            formations[unit.side].contactTime = Time;
        if (target.type.role == UnitType.Role.Line && formations[target.side].contactTime < 0f)
            formations[target.side].contactTime = Time;
        if (!contactSeen && type.role == UnitType.Role.Line && target.type.role == UnitType.Role.Line)
        {
            contactSeen = true;
            events.Add(new Event { kind = EventKind.Contact, position = (unit.position + target.position) * 0.5f, unit = unit, other = target });
        }
    }

    void Shove(Unit unit, Vector2 direction, float distance) => unit.position = ClampToField(unit.position + direction * distance);

    Vector2 ClampToField(Vector2 point, float inset = 0f) => new Vector2(
        Mathf.Clamp(point.x, bounds.xMin + inset, bounds.xMax - inset),
        Mathf.Clamp(point.y, bounds.yMin + inset, bounds.yMax - inset));

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
            a.position = ClampToField(a.position);
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
        int winner = Running;
        if (aliveA == 0 && aliveB == 0) winner = Draw;
        else if (aliveB == 0) winner = 0;
        else if (aliveA == 0) winner = 1;
        else if (Time >= settings.maxDuration)
        {
            TimedOut = true;
            int valueA = ValueLeft(0), valueB = ValueLeft(1);
            winner = valueA == valueB ? Draw : valueA > valueB ? 0 : 1;
        }
        if (winner == Running)
            return;
        Winner = winner;
        events.Add(new Event { kind = EventKind.End, position = lastDeath });
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
