using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A kind of ground cover (forest, marsh) and what it does to the units standing on it. Height comes from the land itself.</summary>
[CreateAssetMenu(menuName = "Game/Terrain Type", fileName = "Terrain Type")]
public class TerrainType : ScriptableObject
{
    [Serializable]
    public struct ForUnit
    {
        public UnitType type;
        [Tooltip("Used for this unit type in place of the terrain's Speed.")]
        public float speed;
    }

    [Header("Moving")]
    [Tooltip("Multiplies the speed of a unit standing on this ground.")]
    [Range(0.1f, 2f)] public float speed = 1f;
    [Tooltip("Speed for particular unit types, in place of Speed.")]
    public List<ForUnit> speedFor = new List<ForUnit>();
    [Tooltip("Off: no charge bonus for a unit attacking from, or into, this ground.")]
    public bool allowsCharge = true;

    [Header("Shooting")]
    [Tooltip("Multiplies the range of a shooter standing on this ground.")]
    [Range(0.2f, 2f)] public float shooterRange = 1f;
    [Tooltip("Multiplies the chance that a shot at a unit on this ground hits.")]
    [Range(0f, 1f)] public float cover = 1f;
    [Tooltip("A unit on this ground can't be shot from further away than this. 0 means no limit.")]
    [Min(0f)] public float hiddenBeyond;

    public float SpeedFor(UnitType unit)
    {
        for (int i = 0; i < speedFor.Count; i++)
            if (speedFor[i].type == unit)
                return speedFor[i].speed;
        return speed;
    }
}
