using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the hand-placed towns on the kingdom map sitting on the ground. Every town is a child of the scene's
/// "Towns" root (outside "Kingdom Map", which the builder rebuilds), and every piece of a town is a direct child of
/// its town object, or of a group object under it whose name starts with "Group". Groups whose name starts with
/// "Afloat" (boats) or "Fixed" (the bridge, set by its banks) are left where they are.
/// </summary>
public static class TownTools
{
    const string RootName = "Towns";

    [MenuItem("Tools/Map/Settle Towns On Ground")]
    static void SettleFromMenu()
    {
        int moved = SettleAll();
        Debug.Log($"Settled {moved} town pieces on the ground. Save the scene (Ctrl+S) to keep it.");
    }

    /// <summary>Puts every town piece back on the terrain under it. Returns how many pieces it moved.</summary>
    public static int SettleAll()
    {
        var root = GameObject.Find(RootName);
        if (root == null)
            return 0;
        var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)
            .Where(t => t.gameObject.scene == root.scene).ToArray();
        int moved = 0;
        foreach (Transform town in root.transform)
            foreach (Transform piece in town)
            {
                // boats and the like sit on water, which a terrain rebuild doesn't move
                if (piece.name.StartsWith("Afloat") || piece.name.StartsWith("Fixed"))
                    continue;
                if (piece.name.StartsWith("Group"))
                {
                    foreach (Transform inner in piece)
                        moved += Settle(inner, terrains) ? 1 : 0;
                }
                else
                    moved += Settle(piece, terrains) ? 1 : 0;
            }
        return moved;
    }

    /// <summary>Buildings (and anything with deep foundations) stand on the highest ground under their footprint,
    /// so no corner floats; everything else stands on the ground at its pivot.</summary>
    public static bool Settle(Transform piece, Terrain[] terrains)
    {
        var p = piece.position;
        float y;
        if (piece.name.StartsWith("SM_Bld_"))
        {
            var renderers = piece.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return false;
            var b = renderers[0].bounds;
            foreach (var r in renderers)
                b.Encapsulate(r.bounds);
            // the footprint, a little inside the bounds (eaves overhang the walls)
            float ex = b.extents.x * 0.7f, ez = b.extents.z * 0.7f;
            y = float.MinValue;
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                y = Mathf.Max(y, Ground(terrains, b.center.x + i * ex, b.center.z + j * ez));
        }
        else
            y = Ground(terrains, p.x, p.z);
        if (float.IsNaN(y) || Mathf.Abs(y - p.y) < 0.005f)
            return false;
        Undo.RecordObject(piece, "Settle town piece");
        piece.position = new Vector3(p.x, y, p.z);
        return true;
    }

    public static float Ground(Terrain[] terrains, float x, float z)
    {
        foreach (var t in terrains)
        {
            var o = t.transform.position;
            var s = t.terrainData.size;
            if (x >= o.x && x <= o.x + s.x && z >= o.z && z <= o.z + s.z)
                return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
        }
        return float.NaN;
    }
}
