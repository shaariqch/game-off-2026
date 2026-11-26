using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One kind of soldier: what it costs, how it fights and what it looks like.</summary>
[CreateAssetMenu(menuName = "Game/Unit Type", fileName = "Unit Type")]
public class UnitType : ScriptableObject
{
    [Serializable]
    public struct Against
    {
        public UnitType type;
        [Tooltip("Damage multiplier when attacking this type.")]
        public float damage;
        [Tooltip("Below 1 makes this type a preferred target, above 1 a less preferred one. It scales the distance used to pick the nearest enemy.")]
        public float targetWeight;
    }

    [Header("Market")]
    [Min(1)] public int cost = 10;

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
    [Min(0.1f)] public float visualScale = 1f;

    public bool IsRanged => rangedDamage > 0f;

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
