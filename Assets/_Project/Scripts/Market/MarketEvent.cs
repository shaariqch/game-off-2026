using System;
using UnityEngine;

/// <summary>Something happening at a place on the map that moves the price of one unit type for a while.</summary>
[Serializable]
public class MarketEvent
{
    public enum Category
    {
        /// <summary>A threat somewhere: something is about to be in demand.</summary>
        Invasion,
        /// <summary>Everything else going on in the kingdom.</summary>
        Notice,
    }

    [Tooltip("Seconds into the trading phase when it happens.")]
    [Min(0f)] public float time = 10f;
    public Category category = Category.Notice;
    [Tooltip("The place's name as it appears on a MapPlace in the scene.")]
    public string place;
    [Tooltip("The sentence the player reads. It names a place and what is happening, never a unit type.")]
    [TextArea] public string text;
    [Tooltip("The unit type whose price it moves.")]
    public UnitType affects;
    [Tooltip("How far the price moves at the event's height, as a share of the type's cost (0.5 is half again).")]
    public float strength = 0.5f;
    [Tooltip("Shown on the timeline before it happens.")]
    public bool known = true;
}
