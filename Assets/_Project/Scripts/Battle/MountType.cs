using UnityEngine;

/// <summary>
/// An animal a unit rides: its model and coat, where the rider sits, and its own clips. The view puts the mount
/// under the figure and keeps the rider on its back, so the rider rises and falls with the mount's stride.
/// </summary>
[CreateAssetMenu(menuName = "Game/Mount Type", fileName = "Mount Type")]
public class MountType : ScriptableObject
{
    [Header("Look")]
    public GameObject prefab;
    [Tooltip("Replaces every material on the model (a recoloured coat). Optional.")]
    public Material coat;
    [Min(0.01f)] public float scale = 1f;

    [Header("Rider")]
    [Tooltip("The mount's bone the rider sits on; the rider follows its position as the mount moves.")]
    public string seatBone = "body";
    [Tooltip("Where the rider's feet-level origin goes, in metres from the seat bone, in the figure's frame (x right, y up, z forward). The seated pose keeps the hips at standing height, so y is about the saddle's height above the bone minus the rider's hip height.")]
    public Vector3 riderOffset = new Vector3(0f, -0.6f, 0f);
    [Tooltip("When the mount falls, the rider drops to the ground this far to the side and plays its own death.")]
    public float fallClear = 0.9f;

    [Header("Animation (generic clips on the mount's own rig)")]
    public AnimationClip idleClip;
    public AnimationClip walkClip;
    public AnimationClip runClip;
    [Tooltip("Metres a second the walk and run clips look right at, for the mount at this scale.")]
    [Min(0.1f)] public float walkClipSpeed = 1.6f;
    [Min(0.1f)] public float runClipSpeed = 6f;
    [Tooltip("Played when the rider is hit, and when a charge lands.")]
    public AnimationClip hitClip;
    public AnimationClip chargeClip;
    public AnimationClip deathClip;
    [Tooltip("A blend shape on the mount's mesh set to full when it dies (the Quirky animals have eyes.dead). Optional.")]
    public string deadFace = "eyes.dead";

    [Tooltip("The mount's animations baked for the GPU (Tools > Battle > Bake Figures), or none to skin it with a SkinnedMeshRenderer.")]
    public BakedFigure baked;

    public bool Animated => idleClip != null;
}
