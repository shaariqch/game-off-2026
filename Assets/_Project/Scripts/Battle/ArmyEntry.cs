using System;
using UnityEngine;

/// <summary>A number of one unit type in an army.</summary>
[Serializable]
public struct ArmyEntry
{
    public UnitType type;
    [Min(0)] public int count;

    public ArmyEntry(UnitType type, int count)
    {
        this.type = type;
        this.count = count;
    }
}
