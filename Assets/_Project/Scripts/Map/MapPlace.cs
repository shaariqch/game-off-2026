using UnityEngine;

/// <summary>A named point of interest on the kingdom map. Market events name a place, and their notice is pinned here.</summary>
public class MapPlace : MonoBehaviour
{
    [Tooltip("The name events use, e.g. Millbrook.")]
    public string placeName;

    public static MapPlace Find(string name)
    {
        foreach (var place in FindObjectsByType<MapPlace>(FindObjectsSortMode.None))
            if (place.placeName == name)
                return place;
        return null;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.89f, 0.72f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, 4f);
    }
}
