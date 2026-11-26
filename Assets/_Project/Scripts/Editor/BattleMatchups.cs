using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Plays every pair of single-type armies against each other at equal money with no graphics, and reports how
/// often each side wins. Run it from Tools > Battle > Match-ups, or call <see cref="Run"/> from an eval.
/// </summary>
public static class BattleMatchups
{
    const string UnitFolder = "Assets/_Project/Data/Units";

    [MenuItem("Tools/Battle/Match-ups")]
    static void RunFromMenu() => Debug.Log(Run());

    /// <summary>Win rates for every pair of unit types, each army worth <paramref name="money"/>, on flat open ground.</summary>
    /// <param name="seeds">Battles per pairing; each is played with each type on each side.</param>
    /// <param name="defenderHolds">The second army stands its ground, as an army holding terrain does.</param>
    public static string Run(int money = 240, int seeds = 20, bool defenderHolds = false, BattleSim.Settings? settings = null)
    {
        var types = LoadTypes();
        var used = settings ?? BattleSim.Settings.Default;
        var text = new StringBuilder();
        text.Append("Match-ups at ").Append(money).Append(" money, ").Append(seeds).Append(" seeds a side")
            .Append(defenderHolds ? ", defender holds" : "").Append('\n');
        for (int a = 0; a < types.Count; a++)
            for (int b = a + 1; b < types.Count; b++)
            {
                var result = Play(types[a], types[b], money, seeds, defenderHolds, used);
                text.Append(types[a].name).Append(" vs ").Append(types[b].name).Append(": ")
                    .Append(Mathf.RoundToInt(result.winRate * 100f)).Append("% to ").Append(types[a].name)
                    .Append(", value left ").Append(result.leftA.ToString("0")).Append(" vs ").Append(result.leftB.ToString("0"))
                    .Append(", ").Append(result.seconds.ToString("0")).Append(" s\n");
            }
        return text.ToString();
    }

    public struct Result
    {
        /// <summary>Share of battles the first type won (draws count half).</summary>
        public float winRate;
        /// <summary>Average value each type had left at the end, out of the money spent.</summary>
        public float leftA, leftB;
        public float seconds;
    }

    /// <summary>Plays <paramref name="seeds"/> battles with each type on each side.</summary>
    public static Result Play(UnitType first, UnitType second, int money, int seeds, bool defenderHolds, BattleSim.Settings settings)
    {
        var armyFirst = new List<ArmyEntry> { new ArmyEntry { type = first, count = Mathf.Max(1, money / first.cost) } };
        var armySecond = new List<ArmyEntry> { new ArmyEntry { type = second, count = Mathf.Max(1, money / second.cost) } };
        var zoneA = new Rect(-19f, -11f, 8f, 22f);
        var zoneB = new Rect(11f, -11f, 8f, 22f);
        var bounds = new Rect(-24f, -14f, 48f, 28f);
        float wins = 0f, leftFirst = 0f, leftSecond = 0f, seconds = 0f;
        int battles = 0;
        for (int seed = 1; seed <= seeds; seed++)
            for (int swap = 0; swap < 2; swap++)
            {
                var sim = swap == 0
                    ? BattleSim.RunToEnd(armyFirst, armySecond, zoneA, zoneB, bounds, seed, settings, null, false, defenderHolds)
                    : BattleSim.RunToEnd(armySecond, armyFirst, zoneA, zoneB, bounds, seed, settings, null, false, defenderHolds);
                int firstSide = swap == 0 ? 0 : 1;
                wins += sim.Winner == firstSide ? 1f : sim.Winner == BattleSim.Draw ? 0.5f : 0f;
                leftFirst += sim.ValueLeft(firstSide);
                leftSecond += sim.ValueLeft(1 - firstSide);
                seconds += sim.Time;
                battles++;
            }
        return new Result { winRate = wins / battles, leftA = leftFirst / battles, leftB = leftSecond / battles, seconds = seconds / battles };
    }

    static List<UnitType> LoadTypes()
    {
        var types = new List<UnitType>();
        foreach (var guid in AssetDatabase.FindAssets("t:UnitType", new[] { UnitFolder }))
            types.Add(AssetDatabase.LoadAssetAtPath<UnitType>(AssetDatabase.GUIDToAssetPath(guid)));
        types.Sort((x, y) => x.cost != y.cost ? x.cost.CompareTo(y.cost) : string.CompareOrdinal(x.name, y.name));
        return types;
    }
}
