using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runs a <see cref="BattleSim"/> in the scene: spawns a figure for every unit, moves them as the battle plays,
/// flies the arrows and logs the result. Enter starts another battle with the same armies.
/// </summary>
public class BattleView : MonoBehaviour
{
    [Header("Armies")]
    [SerializeField] List<ArmyEntry> armyA = new List<ArmyEntry>();
    [SerializeField] List<ArmyEntry> armyB = new List<ArmyEntry>();
    [Tooltip("0 picks a new seed for every battle. Any other number replays the same battle.")]
    [SerializeField] int seed;
    [Tooltip("The army stands its ground and lets the enemy come to it. Use it for the side that holds the terrain.")]
    [SerializeField] bool armyAHolds;
    [SerializeField] bool armyBHolds = true;

    [Header("Field")]
    [Tooltip("Centre of each army's spawn area.")]
    [SerializeField] Transform spawnA;
    [SerializeField] Transform spawnB;
    [Tooltip("Spawn area size: x across the field (towards the enemy), y along the line.")]
    [SerializeField] Vector2 spawnSize = new Vector2(8f, 22f);
    [Tooltip("Units can't leave this area, centred on this object: x across, y deep.")]
    [SerializeField] Vector2 fieldSize = new Vector2(48f, 28f);
    [SerializeField] BattleSim.Settings simSettings = BattleSim.Settings.Default;
    [Tooltip("Active Terrain Zones under this object are part of the battlefield. Empty means the whole scene is searched.")]
    [SerializeField] Transform terrainRoot;

    [Header("Playback")]
    [Tooltip("Battle seconds per real second.")]
    [SerializeField, Min(0f)] float speed = 1f;
    [SerializeField] bool startOnPlay = true;

    [Header("Camera")]
    [Tooltip("Every living unit is kept in this group, so a Cinemachine camera following it frames the fight. Optional.")]
    [SerializeField] CinemachineTargetGroup cameraGroup;
    [Tooltip("Room the camera leaves around each unit.")]
    [SerializeField, Min(0f)] float cameraUnitRadius = 1.5f;

    [Header("Figures")]
    [Tooltip("Flat disc under each unit, coloured by side.")]
    [SerializeField] Mesh markerMesh;
    [SerializeField] Material markerMaterialA;
    [SerializeField] Material markerMaterialB;
    [SerializeField, Min(0f)] float markerHeight = 0.03f;
    [Tooltip("Name of the bone a weapon is parented to.")]
    [SerializeField] string weaponBone = "Hand_R";
    [SerializeField] Vector3 weaponPosition;
    [SerializeField] Vector3 weaponRotation;
    [Tooltip("Degrees per second a figure turns towards where its unit faces.")]
    [SerializeField, Min(1f)] float turnSpeed = 540f;
    [Tooltip("How far a figure jabs forward when it lands a melee hit, and for how long.")]
    [SerializeField, Min(0f)] float lungeDistance = 0.45f;
    [SerializeField, Min(0.01f)] float lungeTime = 0.25f;
    [Tooltip("Seconds a dying figure takes to fall over, then how long it lies there before it's removed.")]
    [SerializeField, Min(0.01f)] float fallTime = 0.35f;
    [SerializeField, Min(0f)] float corpseTime = 4f;

    [Header("Arrows")]
    [SerializeField] Mesh arrowMesh;
    [SerializeField] Material arrowMaterial;
    [SerializeField] Vector3 arrowScale = new Vector3(0.05f, 0.05f, 0.8f);
    [Tooltip("Height of an arrow's arc as a share of the distance it flies.")]
    [SerializeField, Min(0f)] float arrowArc = 0.18f;
    [SerializeField, Min(0f)] float arrowStartHeight = 1.4f;

    [Header("Sounds (all optional)")]
    [SerializeField] AudioSource audioSource;
    // TODO: a short blade or blunt hit on a body, played when a melee attack lands
    [SerializeField] AudioClip meleeHitClip;
    // TODO: a bow string release, played when an archer shoots
    [SerializeField] AudioClip arrowShotClip;
    // TODO: a short grunt or body fall, played when a unit dies
    [SerializeField] AudioClip deathClip;
    // TODO: a short fanfare or horn for the end of the battle
    [SerializeField] AudioClip battleEndClip;

    sealed class Figure
    {
        public Transform root;
        public float seenAttackTime = -1f;
        public bool seenDeath;
        public float yaw;
    }

    BattleSim sim;
    Transform figureRoot;
    readonly List<Figure> figures = new List<Figure>();
    readonly List<Transform> arrows = new List<Transform>();
    float stepDebt;
    bool reported;

    /// <summary>The battle being shown, or null before the first one.</summary>
    public BattleSim Sim => sim;

    void Start()
    {
        if (startOnPlay)
            Begin();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && !GameInput.Blocked
            && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            Begin();
        if (sim == null)
            return;

        stepDebt += Time.deltaTime * speed;
        // cap the catch-up so one long frame can't freeze the game
        int steps = Mathf.Min(Mathf.FloorToInt(stepDebt / BattleSim.Step), 50);
        stepDebt = Mathf.Min(stepDebt - steps * BattleSim.Step, BattleSim.Step);
        for (int i = 0; i < steps; i++)
            sim.Tick();

        ShowUnits();
        ShowArrows();

        if (sim.Winner != BattleSim.Running && !reported)
        {
            reported = true;
            Debug.Log("[Battle] " + sim.Describe());
            Play(battleEndClip);
        }
    }

    /// <summary>Clears the field and starts a new battle with the armies set in the Inspector.</summary>
    public void Begin()
    {
        if (spawnA == null || spawnB == null)
        {
            Debug.LogError("BattleView needs both spawn points set.", this);
            return;
        }
        if (figureRoot != null)
            Destroy(figureRoot.gameObject);
        figureRoot = new GameObject("Figures").transform;
        figureRoot.SetParent(transform, false);
        figures.Clear();
        arrows.Clear();
        if (cameraGroup != null)
            cameraGroup.Targets.Clear();
        stepDebt = 0f;
        reported = false;

        int battleSeed = seed != 0 ? seed : Random.Range(1, int.MaxValue);
        var zones = TerrainZones();
        sim = new BattleSim(armyA, armyB, Zone(spawnA), Zone(spawnB), FieldBounds(), battleSeed, simSettings, zones, armyAHolds, armyBHolds);
        Debug.Log("[Battle] started with seed " + battleSeed + ", " + sim.units.Count + " units, " + zones.Count + " terrain zones");
        foreach (var unit in sim.units)
        {
            var figure = MakeFigure(unit);
            figures.Add(figure);
            if (cameraGroup != null)
                cameraGroup.AddMember(figure.root, 1f, cameraUnitRadius);
        }
        ShowUnits();
    }

    Rect Zone(Transform centre)
    {
        var local = transform.InverseTransformPoint(centre.position);
        return new Rect(local.x - spawnSize.x * 0.5f, local.z - spawnSize.y * 0.5f, spawnSize.x, spawnSize.y);
    }

    List<BattleSim.Zone> TerrainZones()
    {
        var found = terrainRoot != null
            ? terrainRoot.GetComponentsInChildren<TerrainZone>(false)
            : FindObjectsByType<TerrainZone>(FindObjectsSortMode.None);
        var zones = new List<BattleSim.Zone>();
        foreach (var zone in found)
        {
            if (!zone.isActiveAndEnabled || zone.Terrain == null)
                continue;
            var local = transform.InverseTransformPoint(zone.transform.position);
            zones.Add(new BattleSim.Zone { centre = new Vector2(local.x, local.z), radii = zone.Radii, terrain = zone.Terrain });
        }
        return zones;
    }

    Rect FieldBounds() => new Rect(-fieldSize.x * 0.5f, -fieldSize.y * 0.5f, fieldSize.x, fieldSize.y);

    Figure MakeFigure(BattleSim.Unit unit)
    {
        var root = new GameObject(unit.type.name + " " + unit.id).transform;
        root.SetParent(figureRoot, false);
        var figure = new Figure { root = root, yaw = Mathf.Atan2(unit.facing.x, unit.facing.y) * Mathf.Rad2Deg };

        if (unit.type.visualPrefab != null)
        {
            var body = Instantiate(unit.type.visualPrefab, root);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one * unit.type.visualScale;
            // nothing animates the figures yet, so their Animators would only cost time
            foreach (var animator in body.GetComponentsInChildren<Animator>())
                animator.enabled = false;
            foreach (var collider in body.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            if (unit.type.weaponPrefab != null)
            {
                var hand = FindDeep(body.transform, weaponBone);
                if (hand != null)
                {
                    var weapon = Instantiate(unit.type.weaponPrefab, hand);
                    weapon.transform.localPosition = weaponPosition;
                    weapon.transform.localRotation = Quaternion.Euler(weaponRotation);
                    foreach (var collider in weapon.GetComponentsInChildren<Collider>())
                        collider.enabled = false;
                }
            }
        }

        var markerMaterial = unit.side == 0 ? markerMaterialA : markerMaterialB;
        if (markerMesh != null && markerMaterial != null)
        {
            var marker = new GameObject("Side Marker");
            marker.transform.SetParent(root, false);
            marker.transform.localPosition = new Vector3(0f, markerHeight, 0f);
            // sized from the mesh's own bounds, so any flat disc mesh works
            float width = unit.type.radius * 2.4f / Mathf.Max(0.001f, markerMesh.bounds.size.x);
            marker.transform.localScale = new Vector3(width, 0.01f, width);
            marker.AddComponent<MeshFilter>().sharedMesh = markerMesh;
            var renderer = marker.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = markerMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return figure;
    }

    static Transform FindDeep(Transform parent, string childName)
    {
        if (parent.name == childName)
            return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindDeep(parent.GetChild(i), childName);
            if (found != null)
                return found;
        }
        return null;
    }

    void ShowUnits()
    {
        for (int i = 0; i < figures.Count; i++)
        {
            var figure = figures[i];
            if (figure.root == null)
                continue;
            var unit = sim.units[i];

            if (!unit.Alive)
            {
                if (!figure.seenDeath)
                {
                    figure.seenDeath = true;
                    Play(deathClip);
                    if (cameraGroup != null)
                        cameraGroup.RemoveMember(figure.root);
                }
                float dead = sim.Time - unit.diedTime;
                if (dead > fallTime + corpseTime)
                {
                    Destroy(figure.root.gameObject);
                    figure.root = null;
                    continue;
                }
                float fall = Mathf.Clamp01(dead / fallTime);
                figure.root.localRotation = Quaternion.Euler(0f, figure.yaw, 0f) * Quaternion.Euler(-90f * fall * fall, 0f, 0f);
                continue;
            }

            if (unit.lastAttackTime != figure.seenAttackTime)
            {
                figure.seenAttackTime = unit.lastAttackTime;
                Play(unit.lastAttackRanged ? arrowShotClip : meleeHitClip);
            }

            float targetYaw = Mathf.Atan2(unit.facing.x, unit.facing.y) * Mathf.Rad2Deg;
            figure.yaw = Mathf.MoveTowardsAngle(figure.yaw, targetYaw, turnSpeed * Time.deltaTime);
            var position = new Vector3(unit.position.x, sim.HeightAt(unit.position), unit.position.y);
            if (unit.lastAttackTime >= 0f && !unit.lastAttackRanged)
            {
                float since = (sim.Time - unit.lastAttackTime) / lungeTime;
                if (since < 1f)
                    position += new Vector3(unit.facing.x, 0f, unit.facing.y) * (Mathf.Sin(since * Mathf.PI) * lungeDistance);
            }
            figure.root.localPosition = position;
            figure.root.localRotation = Quaternion.Euler(0f, figure.yaw, 0f);
        }
    }

    void ShowArrows()
    {
        if (arrowMesh == null || arrowMaterial == null)
            return;
        while (arrows.Count < sim.projectiles.Count)
        {
            var arrow = new GameObject("Arrow");
            arrow.transform.SetParent(figureRoot, false);
            arrow.transform.localScale = arrowScale;
            arrow.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
            var renderer = arrow.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = arrowMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arrows.Add(arrow.transform);
        }
        for (int i = 0; i < arrows.Count; i++)
        {
            bool flying = i < sim.projectiles.Count;
            if (arrows[i].gameObject.activeSelf != flying)
                arrows[i].gameObject.SetActive(flying);
            if (!flying)
                continue;
            var shot = sim.projectiles[i];
            arrows[i].localPosition = ArrowPoint(shot, shot.age);
            var ahead = ArrowPoint(shot, shot.age + 0.02f) - arrows[i].localPosition;
            if (ahead.sqrMagnitude > 0.000001f)
                arrows[i].localRotation = Quaternion.LookRotation(ahead);
        }
    }

    Vector3 ArrowPoint(BattleSim.Projectile shot, float age)
    {
        float t = Mathf.Clamp01(age / shot.duration);
        Vector2 flat = Vector2.Lerp(shot.start, shot.end, t);
        float length = (shot.end - shot.start).magnitude;
        // starts at bow height, peaks mid-flight, lands low on the target
        float height = Mathf.Lerp(sim.HeightAt(shot.start) + arrowStartHeight, sim.HeightAt(shot.end) + arrowStartHeight * 0.6f, t)
            + 4f * t * (1f - t) * length * arrowArc;
        return new Vector3(flat.x, height, flat.y);
    }

    void Play(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(fieldSize.x, 0.1f, fieldSize.y));
        if (spawnA != null)
        {
            Gizmos.color = new Color(0.2f, 0.5f, 1f);
            Gizmos.DrawWireCube(transform.InverseTransformPoint(spawnA.position), new Vector3(spawnSize.x, 0.1f, spawnSize.y));
        }
        if (spawnB != null)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.25f);
            Gizmos.DrawWireCube(transform.InverseTransformPoint(spawnB.position), new Vector3(spawnSize.x, 0.1f, spawnSize.y));
        }
    }
}
