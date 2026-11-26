using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One kind of soldier: what it costs, how it fights and what it looks like.</summary>
[CreateAssetMenu(menuName = "Game/Unit Type", fileName = "Unit Type")]
public class UnitType : ScriptableObject
{
    /// <summary>A prop parented to one of the figure's bones.</summary>
    [Serializable]
    public struct Attachment
    {
        public GameObject prefab;
        [Tooltip("Name of the bone it hangs from, e.g. Hand_L or Spine_03.")]
        public string bone;
        [Tooltip("Position and rotation (degrees) relative to the bone.")]
        public Vector3 position;
        public Vector3 rotation;
    }

    [Serializable]
    public struct Against
    {
        public UnitType type;
        [Tooltip("Damage multiplier when attacking this type.")]
        public float damage;
        [Tooltip("Below 1 makes this type a preferred target, above 1 a less preferred one. It scales the distance used to pick the nearest enemy.")]
        public float targetWeight;
    }

    /// <summary>Where a type stands in the army's formation and how it fights in it.</summary>
    public enum Role
    {
        /// <summary>Foot soldiers in the battle line, which marches together and fights where it meets the enemy.</summary>
        Line,
        /// <summary>Behind the line, shooting over it.</summary>
        Ranged,
        /// <summary>On the wings. Rides around the enemy's flank and charges.</summary>
        Cavalry,
    }

    [Header("Market")]
    [Min(1)] public int cost = 10;

    [Header("Battle role")]
    public Role role;
    [Tooltip("Lower numbers stand nearer the front of the formation.")]
    public int rank;
    [Tooltip("Takes a cavalry charge from the front without being knocked back, and staggers the rider instead.")]
    public bool bracesCharge;

    [Header("Body")]
    [Min(1f)] public float health = 40f;
    [Min(0.1f)] public float speed = 3f;
    [Tooltip("Units can't overlap closer than their radii added together.")]
    [Min(0.1f)] public float radius = 0.4f;

    [Header("Melee")]
    [Min(0f)] public float meleeDamage = 6f;
    [Min(0.1f)] public float meleeInterval = 1f;
    [Tooltip("Gap between the two bodies at which a melee hit lands.")]
    [Min(0.05f)] public float meleeReach = 0.6f;

    [Header("Charge")]
    [Tooltip("Multiplies the first melee hit after moving at least Charge Distance. 1 means no charge.")]
    [Min(1f)] public float chargeMultiplier = 1f;
    [Min(0f)] public float chargeDistance = 5f;

    [Header("Ranged (damage 0 means melee only)")]
    [Min(0f)] public float rangedDamage;
    [Min(0.1f)] public float rangedInterval = 1.6f;
    [Min(1f)] public float range = 14f;
    [Tooltip("An enemy closer than this stops the unit shooting, and it fights with its melee values instead.")]
    [Min(0f)] public float tooCloseToShoot = 1.5f;
    [Min(1f)] public float projectileSpeed = 22f;
    [Range(0f, 1f)] public float accuracy = 0.8f;

    [Header("Match-ups")]
    public List<Against> against = new List<Against>();

    [Header("Look")]
    public GameObject visualPrefab;
    [Tooltip("Held in the right hand. Optional.")]
    public GameObject weaponPrefab;
    [Tooltip("Anything else the figure carries, each on its own bone: a bow in the left hand, a quiver on the back.")]
    public Attachment[] attachments = new Attachment[0];
    [Min(0.1f)] public float visualScale = 1f;

    [Header("Animation (humanoid clips; empty leaves the figure still)")]
    public AnimationClip idleClip;
    public AnimationClip walkClip;
    public AnimationClip runClip;
    [Tooltip("Metres a second the walk and run clips were made for. Their playback speeds up or slows to match how fast the figure moves.")]
    [Min(0.1f)] public float walkClipSpeed = 1.4f;
    [Min(0.1f)] public float runClipSpeed = 3.8f;
    [Tooltip("Melee blows; one is picked at random for each.")]
    public AnimationClip[] attackClips = new AnimationClip[0];
    [Tooltip("A shot, for ranged units.")]
    public AnimationClip shootClip;
    [Tooltip("Flinches when hit; one at random.")]
    public AnimationClip[] hitClips = new AnimationClip[0];
    [Tooltip("Played when knocked back by a charge.")]
    public AnimationClip[] knockbackClips = new AnimationClip[0];
    public AnimationClip[] deathClips = new AnimationClip[0];

    public bool Animated => idleClip != null;

    public bool IsRanged => rangedDamage > 0f;
    public bool IsCavalry => role == Role.Cavalry;

    public float DamageAgainst(UnitType other)
    {
        for (int i = 0; i < against.Count; i++)
            if (against[i].type == other)
                return against[i].damage;
        return 1f;
    }

    public float TargetWeight(UnitType other)
    {
        for (int i = 0; i < against.Count; i++)
            if (against[i].type == other)
                return against[i].targetWeight > 0f ? against[i].targetWeight : 1f;
        return 1f;
    }
}
