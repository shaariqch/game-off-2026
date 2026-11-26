using UnityEngine;

/// <summary>An oval patch of one terrain type on the battlefield. Only active zones count.</summary>
public class TerrainZone : MonoBehaviour
{
    [SerializeField] TerrainType terrain;
    [Tooltip("Half-widths of the oval: x across the field, y deep.")]
    [SerializeField] Vector2 radii = new Vector2(8f, 10f);

    public TerrainType Terrain => terrain;
    public Vector2 Radii => radii;

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f);
        Vector3 last = Vector3.zero;
        for (int i = 0; i <= 48; i++)
        {
            float angle = i / 48f * Mathf.PI * 2f;
            var point = transform.position + new Vector3(Mathf.Cos(angle) * radii.x, 0.1f, Mathf.Sin(angle) * radii.y);
            if (i > 0)
                Gizmos.DrawLine(last, point);
            last = point;
        }
    }
}
