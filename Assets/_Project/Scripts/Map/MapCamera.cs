using UnityEngine;

/// <summary>Scrolls the map camera north and south along the strip. The map scrolls vertically only.</summary>
public class MapCamera : MonoBehaviour
{
    [Tooltip("Metres a second at full scroll.")]
    [SerializeField, Min(1f)] float speed = 140f;
    [Tooltip("Seconds to reach full speed and to stop.")]
    [SerializeField, Min(0.01f)] float easing = 0.12f;
    [Tooltip("The camera's own z stays between these (south, north).")]
    [SerializeField] Vector2 zRange = new Vector2(-120f, 640f);

    float input, velocity;

    /// <summary>-1 south, 1 north, 0 stop. Set every frame.</summary>
    public void Scroll(float direction) => input = Mathf.Clamp(direction, -1f, 1f);

    /// <summary>Moves the view so a world point sits at the given height on screen (0 bottom, 1 top), within the range.</summary>
    public void LookAt(Vector3 point, Camera cam, float screenHeight = 0.5f)
    {
        var ray = cam.ViewportPointToRay(new Vector3(0.5f, screenHeight, 0f));
        // where that screen point meets the ground at the point's height
        float t = (point.y - ray.origin.y) / ray.direction.y;
        float groundZ = ray.origin.z + ray.direction.z * t;
        var position = transform.position;
        position.z = Mathf.Clamp(position.z + point.z - groundZ, zRange.x, zRange.y);
        transform.position = position;
        velocity = 0f;
    }

    void LateUpdate()
    {
        float target = input * speed;
        velocity = Mathf.MoveTowards(velocity, target, speed / easing * Time.unscaledDeltaTime);
        input = 0f;
        if (Mathf.Approximately(velocity, 0f))
            return;
        var position = transform.position;
        position.z = Mathf.Clamp(position.z + velocity * Time.unscaledDeltaTime, zRange.x, zRange.y);
        transform.position = position;
    }
}
