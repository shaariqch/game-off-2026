using System.Collections.Generic;
using DamageNumbersPro;
using MoreMountains.Feedbacks;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runs a <see cref="BattleSim"/> in the scene: spawns a figure for every unit, moves them as the battle plays,
/// flies the arrows and logs the result. Enter starts another battle with the same armies.
/// The feel: Feel (More Mountains), driven from code, for slow motion and freeze frames (MMTimeManager) and a scale
/// spring on every figure that squashes when it's hit; Cinemachine impulses for the camera's jolts, which push along
/// the blow; and a closer Cinemachine camera that the view cuts to for big moments.
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
    [Tooltip("Each army's formation is centred on its spawn point.")]
    [SerializeField] Transform spawnA;
    [SerializeField] Transform spawnB;
    [Tooltip("Spawn area size, only drawn as a guide: x across the field (towards the enemy), y along the line.")]
    [SerializeField] Vector2 spawnSize = new Vector2(8f, 22f);
    [Tooltip("Units can't leave this area, centred on this object: x across, y deep.")]
    [SerializeField] Vector2 fieldSize = new Vector2(48f, 28f);
    [SerializeField] BattleSim.Settings simSettings = BattleSim.Settings.Default;
    [Tooltip("Active Terrain Zones under this object are part of the battlefield. Empty means the whole scene is searched.")]
    [SerializeField] Transform terrainRoot;
    [Tooltip("The land the battle is fought on. Units stand on its surface, and its slopes and heights count in the fight. Empty: the first active Terrain under Terrain Root, or flat ground.")]
    [SerializeField] Terrain ground;

    [Header("Playback")]
    [Tooltip("Battle seconds per real second.")]
    [SerializeField, Min(0f)] float speed = 1f;
    [SerializeField] bool startOnPlay = true;

    [Header("Camera")]
    [Tooltip("Every living unit is kept in this group, so a Cinemachine camera following it frames the fight. Optional.")]
    [SerializeField] CinemachineTargetGroup cameraGroup;
    [Tooltip("Room the camera leaves around each unit.")]
    [SerializeField, Min(0f)] float cameraUnitRadius = 1.5f;
    [Tooltip("Seconds over which a dead unit stops counting for the camera, so the view drifts instead of jumping.")]
    [SerializeField, Min(0.01f)] float cameraForgetTime = 2.5f;

    [Header("Figures")]
    [Tooltip("The figures' own material. On each figure it's swapped for its side's outfit material, the same atlas with the clothing recoloured (Tools > Battle > Build Team Colours makes them).")]
    [SerializeField] Material outfitSource;
    [SerializeField] Material outfitA;
    [SerializeField] Material outfitB;
    [Tooltip("Name of the bone a weapon is parented to.")]
    [SerializeField] string weaponBone = "Hand_R";
    [SerializeField] Vector3 weaponPosition;
    [SerializeField] Vector3 weaponRotation;
    [Tooltip("How quickly a figure catches up with its unit's position. The battle moves in small steps; this smooths them out.")]
    [SerializeField, Min(1f)] float followSharpness = 18f;
    [Tooltip("Degrees per second a figure turns towards where its unit faces.")]
    [SerializeField, Min(1f)] float turnSpeed = 540f;
    [Tooltip("How far a figure jabs forward when it lands a melee hit, and for how long.")]
    [SerializeField, Min(0f)] float lungeDistance = 0.45f;
    [SerializeField, Min(0.01f)] float lungeTime = 0.25f;
    [Tooltip("Seconds a dying figure takes to fall over, then how long it lies there before it's removed.")]
    [SerializeField, Min(0.01f)] float fallTime = 0.35f;
    [SerializeField, Min(0f)] float corpseTime = 4f;
    [Tooltip("With a death clip: seconds into it that the body hits the ground and raises dust.")]
    [SerializeField, Min(0.01f)] float deathLandTime = 0.9f;
    [Tooltip("How quickly a figure's walk or run follows changes in its speed.")]
    [SerializeField, Min(0.1f)] float speedSmoothing = 8f;
    [Tooltip("How hard a hit squashes the figure it lands on, through its scale spring. A charge squashes three times as hard.")]
    [SerializeField, Min(0f)] float hitSquash = 4f;

    [Header("Arrows")]
    [SerializeField] Mesh arrowMesh;
    [SerializeField] Material arrowMaterial;
    [SerializeField] Vector3 arrowScale = new Vector3(0.05f, 0.05f, 0.8f);
    [Tooltip("Height of an arrow's arc as a share of the distance it flies.")]
    [SerializeField, Min(0f)] float arrowArc = 0.18f;
    [SerializeField, Min(0f)] float arrowStartHeight = 1.4f;

    [Header("Effects")]
    [Tooltip("Soft round particle material that uses the particle's colour, for dust and hits.")]
    [SerializeField] Material particleMaterial;
    [SerializeField] Color dustColour = new Color(0.66f, 0.58f, 0.46f, 0.6f);
    [SerializeField] Color hitColour = new Color(0.45f, 0.06f, 0.05f, 1f);
    [SerializeField] Color sparkColour = new Color(1f, 0.85f, 0.55f, 1f);
    [Tooltip("Dust puffs a second kicked up by riding cavalry.")]
    [SerializeField, Min(0f)] float cavalryDust = 14f;

    [Header("Damage numbers (Damage Numbers Pro)")]
    [Tooltip("A 3D preset popped over a unit for every melee hit and arrow. The view spawns from its own hidden copy, set up with the values below, so the preset itself is never changed.")]
    [SerializeField] DamageNumber hitNumber;
    [Tooltip("The preset for charges and hits from behind.")]
    [SerializeField] DamageNumber heavyNumber;
    [Tooltip("Metres above a unit's feet a number starts.")]
    [SerializeField] float numberHeight = 2.2f;
    [Tooltip("Numbers keep the size they'd have at this camera distance, however far the camera is, times Number Scale.")]
    [SerializeField, Min(1f)] float numberBaseDistance = 15f;
    [SerializeField, Min(0.01f)] float numberScale = 0.7f;
    [SerializeField, Min(0.01f)] float heavyNumberScale = 1.5f;
    [Tooltip("Neutral colours, so a number never reads as one side's.")]
    [SerializeField] Color hitNumberColour = new Color(1f, 0.95f, 0.86f);
    [SerializeField] Color heavyNumberColour = new Color(1f, 0.72f, 0.2f);
    [Tooltip("Seconds a number stays up; 0 keeps the preset's own.")]
    [SerializeField, Min(0f)] float numberLifetime = 1f;
    [Tooltip("Most numbers of each kind on screen at once; each costs a few draw calls.")]
    [SerializeField, Min(1)] int maxNumbers = 16;
    [Tooltip("Seconds between a number's animation steps. Each step rebuilds its mesh, which is most of their cost.")]
    [SerializeField, Min(0f)] float numberUpdateDelay = 0.03f;
    [Tooltip("Hits close together add up into one running total that new hits fly into, instead of a number per hit.")]
    [SerializeField] bool numberMerging = true;
    [Tooltip("Metres within which a new hit joins a number already up.")]
    [SerializeField, Min(0.1f)] float mergeRadius = 4f;
    [Tooltip("Seconds a new hit takes to fly into the total, how much the total swells when it lands, and how many extra seconds it stays up.")]
    [SerializeField, Min(0.01f)] float mergeTime = 0.25f;
    [SerializeField, Min(1f)] float mergeBump = 1.35f;
    [SerializeField, Min(0f)] float mergeLinger = 0.6f;
    [Tooltip("A total grows from its normal size to this many times it as it climbs to Number Grow Until.")]
    [SerializeField, Min(1f)] float numberGrowth = 1.6f;
    [SerializeField, Min(2f)] float numberGrowUntil = 60f;

    [Header("Camera jolts")]
    [Tooltip("The camera is knocked along the blow (Cinemachine impulse, a recoil shape) for three things only: the lines meeting, a cavalry charge landing on screen, and the final kill. Metres for each.")]
    [SerializeField, Min(0f)] float contactJolt = 0.3f;
    [SerializeField, Min(0f)] float chargeJolt = 0.18f;
    [SerializeField, Min(0f)] float endJolt = 0.35f;
    [Tooltip("Seconds a jolt lasts, in real time, and the fewest real seconds between two charge jolts.")]
    [SerializeField, Min(0.05f)] float joltTime = 0.3f;
    [SerializeField, Min(0f)] float chargeJoltGap = 1.2f;
    [Tooltip("Seconds the battle freezes when the charge a moment is showing lands.")]
    [SerializeField, Min(0f)] float chargeFreeze = 0.07f;

    [Header("Big moments")]
    [Tooltip("A closer camera, off until a moment, that looks the same way as the battle camera from nearer. The view moves its target to the moment and turns it on; the Cinemachine Brain blends to it and back.")]
    [SerializeField] CinemachineCamera momentCamera;
    [Tooltip("Time slows and the moment camera pushes in just before the lines meet: when the two lines' nearest soldiers are this many metres apart.")]
    [SerializeField, Min(0f)] float contactWarnGap = 3.5f;
    [Tooltip("The same for a cavalry charge: metres the riders still have to ride. Only the battle's first Charge Moments charges get a moment, so moments stay rare.")]
    [SerializeField, Min(0f)] float chargeWarnGap = 3.5f;
    [SerializeField, Min(0)] int chargeMoments = 1;
    [Tooltip("Real seconds after one moment ends before another can start (the final kill always gets one).")]
    [SerializeField, Min(0f)] float momentGap = 5f;
    [Tooltip("Time scale and real seconds of slow motion for the lines meeting, a charge, and the final kill.")]
    [SerializeField] Vector2 contactSlowMotion = new Vector2(0.3f, 1.8f);
    [SerializeField] Vector2 chargeSlowMotion = new Vector2(0.25f, 1.6f);
    [SerializeField] Vector2 endSlowMotion = new Vector2(0.25f, 2.4f);
    [Tooltip("How fast slow motion eases in and out.")]
    [SerializeField, Min(0.1f)] float slowLerpSpeed = 6f;

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
    // TODO: galloping hooves into a crash of bodies and shields, played when a cavalry charge lands
    [SerializeField] AudioClip chargeClip;
    // TODO: a big shout and a clash of shields, played when the two battle lines meet
    [SerializeField] AudioClip contactClip;

    sealed class Figure
    {
        /// <summary>Follows the unit smoothly; the camera watches this.</summary>
        public Transform root;
        /// <summary>The body. Lunges, squashes and falling over happen here, so they don't shake the camera.</summary>
        public Transform visual;
        public MMSpringScale spring;
        /// <summary>Plays the figure's clips, or null for a unit type without animation.</summary>
        public FigureAnimation animation;
        /// <summary>Smoothed ground speed, which picks idle, walk or run.</summary>
        public float speed;
        public bool placed;
        public float seenAttackTime = -1f;
        public bool seenDeath;
        public bool landed;
        public float yaw;
        public float dustDebt;
        /// <summary>This rider's current charge has already been checked for a moment.</summary>
        public bool chargeSeen;
    }

    BattleSim sim;
    Transform figureRoot;
    readonly List<Figure> figures = new List<Figure>();
    readonly List<Transform> arrows = new List<Transform>();
    float stepDebt;
    bool reported;
    ParticleSystem dust, hits;
    DamageNumber hitTemplate, heavyTemplate;
    CinemachineImpulseSource jolts;
    /// <summary>Spine, head and arms: what an action moves while the figure's legs keep walking or running.</summary>
    AvatarMask upperBodyMask;
    float nextChargeJoltAt;
    /// <summary>What the moment camera follows.</summary>
    Transform focus;
    /// <summary>The moment follows the point between these two, or stays where it started when they're null.</summary>
    BattleSim.Unit focusUnit, focusOther;
    float momentUntil, nextMomentAt;
    bool contactMomentSeen;
    int chargeMomentsSeen;

    /// <summary>The battle being shown, or null before the first one.</summary>
    public BattleSim Sim => sim;

    void Awake()
    {
        upperBodyMask = new AvatarMask { name = "Battle Upper Body" };
        for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            upperBodyMask.SetHumanoidBodyPartActive(part, part is AvatarMaskBodyPart.Body or AvatarMaskBodyPart.Head
                or AvatarMaskBodyPart.LeftArm or AvatarMaskBodyPart.RightArm or AvatarMaskBodyPart.LeftFingers
                or AvatarMaskBodyPart.RightFingers or AvatarMaskBodyPart.LeftHandIK or AvatarMaskBodyPart.RightHandIK);
        // jolts: one sharp push along the blow and back, felt the same everywhere, in real time so slow motion
        // and freeze frames don't drag them out (the cameras listen with a CinemachineImpulseListener)
        jolts = gameObject.AddComponent<CinemachineImpulseSource>();
        jolts.ImpulseDefinition.ImpulseChannel = 1;
        jolts.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Recoil;
        jolts.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        jolts.ImpulseDefinition.ImpulseDuration = joltTime;
        CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
        if (momentCamera != null)
            momentCamera.enabled = false;
        // freeze frames and slow motion. It reads the normal fixed and maximum delta times when it initialises, and
        // Begin resets the time scale from Start, so it has to be initialised before any Start runs: a reset sent to
        // an uninitialised manager sets both to zero (which Unity clamps to 0.0001 s, and the battle crawls).
        if (FindAnyObjectByType<MMTimeManager>() == null)
            gameObject.AddComponent<MMTimeManager>().Initialization();
        dust = MakeParticles("Dust", new Vector2(0.8f, 1.6f), new Vector2(0.5f, 1.8f), new Vector2(0.9f, 1.9f), -0.05f);
        hits = MakeParticles("Hits", new Vector2(0.3f, 0.6f), new Vector2(2f, 5f), new Vector2(0.18f, 0.35f), 1.2f);
        focus = new GameObject("Moment Focus").transform;
        focus.SetParent(transform, false);
        if (momentCamera != null)
            momentCamera.Follow = focus;
        // hidden copies of the presets to spawn from; under an inactive parent they never run themselves
        var templates = new GameObject("Damage Number Templates");
        templates.SetActive(false);
        templates.transform.SetParent(transform, false);
        hitTemplate = MakeNumberTemplate(hitNumber, templates.transform, numberScale, "battle hits ");
        heavyTemplate = MakeNumberTemplate(heavyNumber != null ? heavyNumber : hitNumber, templates.transform, numberScale * heavyNumberScale, "battle heavy hits ");
    }

    /// <param name="group">Numbers only merge with numbers of the same group on the same unit.</param>
    DamageNumber MakeNumberTemplate(DamageNumber preset, Transform parent, float scale, string group)
    {
        if (preset == null)
            return null;
        var template = Instantiate(preset, parent);
        template.name = preset.name + " (battle)";
        template.transform.localScale = preset.transform.localScale * scale;
        // the camera sits 30 to 60 m away and zooms in for moments, so keep numbers one size on screen
        // (its size is distance / base distance times a factor running from close scale to far scale; 1 and 1 keeps it plain)
        template.consistentScreenSize = true;
        template.distanceScalingSettings.baseDistance = numberBaseDistance;
        template.distanceScalingSettings.closeDistance = 0f;
        template.distanceScalingSettings.farDistance = 1000f;
        template.distanceScalingSettings.closeScale = 1f;
        template.distanceScalingSettings.farScale = 1f;
        if (numberLifetime > 0f)
            template.lifetime = numberLifetime;
        // slow motion slows them too
        template.unscaledTime = false;
        template.enablePooling = true;
        template.poolSize = maxNumbers;
        // the oldest fade out early when a big melee would put more than this on screen
        template.limitActiveInstances = true;
        template.maxActiveInstances = maxNumbers;
        // damage in an area adds up: a new hit within Merge Radius of a number already up flies into it, and that
        // number adds it, swells and stays up a little longer. The oldest number in reach absorbs. Numbers don't
        // follow their unit, since following would put each unit's numbers in a group of their own.
        template.spamGroup = group;
        template.enableCombination = numberMerging;
        template.combinationSettings.method = DamageNumbersPro.CombinationMethod.ABSORB_NEW;
        template.combinationSettings.maxDistance = mergeRadius;
        template.combinationSettings.spawnDelay = 0.08f;
        template.combinationSettings.absorbDuration = mergeTime;
        template.combinationSettings.absorberScaleFactor = mergeBump;
        template.combinationSettings.bonusLifetime = mergeLinger;
        template.combinationSettings.moveToAbsorber = true;
        template.combinationSettings.teleportToAbsorber = false;
        // and the bigger the total, the bigger it's drawn
        template.enableScaleByNumber = true;
        template.scaleByNumberSettings.fromNumber = 1f;
        template.scaleByNumberSettings.toNumber = numberGrowUntil;
        template.scaleByNumberSettings.fromScale = 1f;
        template.scaleByNumberSettings.toScale = numberGrowth;
        // every number rebuilds its mesh this often; the preset's 0.0125 s cost about 7 ms a frame for 35 numbers
        template.updateDelay = numberUpdateDelay;
        // the preset's own colour gradient would override the colours set per number
        foreach (var text in template.GetComponentsInChildren<TMPro.TMP_Text>(true))
            text.enableVertexGradient = false;
        // numbers casting shadows added a shadow pass draw for every one of them
        foreach (var renderer in template.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        return template;
    }

    void ShowNumber(BattleSim.Unit unit, float damage, bool heavy)
    {
        var template = heavy ? heavyTemplate : hitTemplate;
        if (template == null || unit == null || unit.id >= figures.Count)
            return;
        var figure = figures[unit.id];
        if (figure.root == null)
            return;
        var number = template.Spawn(figure.root.position + Vector3.up * numberHeight, Mathf.Max(1, Mathf.RoundToInt(damage)));
        number.SetColor(heavy ? heavyNumberColour : hitNumberColour);
    }

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
        {
            sim.Tick();
            ShowEvents();
        }

        ShowUnits();
        ShowArrows();
        WatchForMoments();
        ShowMoment();

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
        // called in Edit mode, it built a whole army into the scene, which then got saved with it
        if (!Application.isPlaying)
        {
            Debug.LogWarning("BattleView.Begin only runs in Play mode.", this);
            return;
        }
        if (spawnA == null || spawnB == null)
        {
            Debug.LogError("BattleView needs both spawn points set.", this);
            return;
        }
        DisposeAnimations();
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
        momentUntil = nextMomentAt = nextChargeJoltAt = 0f;
        focusUnit = focusOther = null;
        contactMomentSeen = false;
        chargeMomentsSeen = 0;
        if (momentCamera != null)
            momentCamera.enabled = false;
        MMTimeScaleEvent.Trigger(MMTimeScaleMethods.Reset, 1f, 0f, false, 0f, false);
        if (dust != null) dust.Clear();
        if (hits != null) hits.Clear();

        int battleSeed = seed != 0 ? seed : Random.Range(1, int.MaxValue);
        var zones = TerrainZones();
        var land = ground != null && ground.isActiveAndEnabled ? ground
            : terrainRoot != null ? terrainRoot.GetComponentInChildren<Terrain>(false) : null;
        System.Func<Vector2, float> heightAt = null;
        if (land != null)
            heightAt = point => land.SampleHeight(transform.TransformPoint(new Vector3(point.x, 0f, point.y))) + land.transform.position.y - transform.position.y;
        sim = new BattleSim(armyA, armyB, Zone(spawnA), Zone(spawnB), FieldBounds(), battleSeed, simSettings, zones, armyAHolds, armyBHolds, heightAt);
        Debug.Log("[Battle] started with seed " + battleSeed + ", " + sim.units.Count + " units, " + zones.Count + " terrain zones");
        foreach (var unit in sim.units)
        {
            var figure = MakeFigure(unit);
            figures.Add(figure);
        }
        ShowUnits();
        // added once the figures are in place, so the camera doesn't start from a pile at the origin
        if (cameraGroup != null)
        {
            foreach (var figure in figures)
                cameraGroup.AddMember(figure.root, 1f, cameraUnitRadius);
        }
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
        var visual = new GameObject("Visual").transform;
        visual.SetParent(root, false);
        var figure = new Figure { root = root, visual = visual, yaw = Mathf.Atan2(unit.facing.x, unit.facing.y) * Mathf.Rad2Deg };

        if (unit.type.visualPrefab != null)
        {
            var body = Instantiate(unit.type.visualPrefab, visual);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one * unit.type.visualScale;
            var animators = body.GetComponentsInChildren<Animator>();
            var type = unit.type;
            if (type.Animated && animators.Length > 0)
            {
                // the sim moves the figure, so the clips play in place
                animators[0].enabled = true;
                animators[0].applyRootMotion = false;
                animators[0].cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                figure.animation = new FigureAnimation(animators[0], type.idleClip, type.walkClip, type.runClip,
                    type.walkClipSpeed, type.runClipSpeed, Random.value, upperBodyMask);
            }
            else
                // unanimated figures stay in their bind pose, and their Animators would only cost time
                foreach (var animator in animators)
                    animator.enabled = false;
            foreach (var collider in body.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            if (unit.type.weaponPrefab != null)
                Attach(body.transform, unit.type.weaponPrefab, weaponBone, weaponPosition, weaponRotation);
            foreach (var attachment in unit.type.attachments)
                Attach(body.transform, attachment.prefab, attachment.bone, attachment.position, attachment.rotation);
        }

        // dressed in the side's colour
        var outfit = unit.side == 0 ? outfitA : outfitB;
        if (outfitSource != null && outfit != null)
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int m = 0; m < materials.Length; m++)
                    if (materials[m] == outfitSource)
                    {
                        materials[m] = outfit;
                        changed = true;
                    }
                if (changed)
                    renderer.sharedMaterials = materials;
            }

        figure.spring = visual.gameObject.AddComponent<MMSpringScale>();
        figure.spring.Target = visual;
        return figure;
    }

    /// <summary>Parents a prop to a bone of the body, with its colliders off.</summary>
    static void Attach(Transform body, GameObject prefab, string bone, Vector3 position, Vector3 rotation)
    {
        if (prefab == null)
            return;
        var holder = FindDeep(body, bone);
        if (holder == null)
        {
            Debug.LogWarning("No bone called " + bone + " on " + body.name + " for " + prefab.name + ".", body);
            return;
        }
        var prop = Instantiate(prefab, holder);
        prop.transform.localPosition = position;
        prop.transform.localRotation = Quaternion.Euler(rotation);
        foreach (var collider in prop.GetComponentsInChildren<Collider>())
            collider.enabled = false;
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
                bool dyingAnimated = figure.animation != null && unit.type.deathClips.Length > 0;
                if (!figure.seenDeath)
                {
                    figure.seenDeath = true;
                    Play(deathClip);
                    if (dyingAnimated)
                        figure.animation.Play(Pick(unit.type.deathClips), 0.15f, 0f, true);
                }
                figure.animation?.Update(Time.deltaTime, 0f);
                float dead = sim.Time - unit.diedTime;
                SetCameraWeight(figure.root, 1f - dead / cameraForgetTime);
                if (dead > Mathf.Max(fallTime + corpseTime, cameraForgetTime))
                {
                    figure.animation?.Dispose();
                    figure.animation = null;
                    Destroy(figure.root.gameObject);
                    figure.root = null;
                    continue;
                }
                float fall = Mathf.Clamp01(dead / (dyingAnimated ? deathLandTime : fallTime));
                if (fall >= 1f && !figure.landed)
                {
                    figure.landed = true;
                    Emit(dust, figure.root.position + figure.root.forward * -0.6f, dustColour, 6);
                }
                figure.visual.localPosition = Vector3.zero;
                // without a death clip the figure just tips over
                figure.visual.localRotation = dyingAnimated ? Quaternion.identity : Quaternion.Euler(-90f * fall * fall, 0f, 0f);
                figure.visual.gameObject.SetActive(dead <= fallTime + corpseTime);
                continue;
            }

            if (unit.lastAttackTime != figure.seenAttackTime)
            {
                figure.seenAttackTime = unit.lastAttackTime;
                if (unit.lastAttackRanged)
                    Play(arrowShotClip);
                if (figure.animation != null)
                {
                    var type = unit.type;
                    // a blow fits inside the time until the next one
                    var clip = unit.lastAttackRanged ? type.shootClip : Pick(type.attackClips);
                    float interval = unit.lastAttackRanged ? type.rangedInterval : type.meleeInterval;
                    if (clip != null)
                        figure.animation.Play(clip, 0.12f, Mathf.Min(clip.length, interval * 0.95f));
                }
            }

            float targetYaw = Mathf.Atan2(unit.facing.x, unit.facing.y) * Mathf.Rad2Deg;
            figure.yaw = Mathf.MoveTowardsAngle(figure.yaw, targetYaw, turnSpeed * Time.deltaTime);
            var position = new Vector3(unit.position.x, sim.HeightAt(unit.position), unit.position.y);
            var before = figure.root.localPosition;
            bool wasPlaced = figure.placed;
            figure.root.localPosition = figure.placed
                ? Vector3.Lerp(figure.root.localPosition, position, 1f - Mathf.Exp(-followSharpness * Time.deltaTime))
                : position;
            figure.placed = true;
            figure.root.localRotation = Quaternion.Euler(0f, figure.yaw, 0f);

            // idle, walk or run by how fast the figure is actually going
            if (figure.animation != null && Time.deltaTime > 0f)
            {
                var moved = figure.root.localPosition - before;
                moved.y = 0f;
                float measured = wasPlaced ? moved.magnitude / Time.deltaTime : 0f;
                figure.speed = Mathf.Lerp(figure.speed, measured, 1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));
                figure.animation.Update(Time.deltaTime, figure.speed);
            }

            // riders kick up dust
            if (unit.type.IsCavalry && Time.deltaTime > 0f)
            {
                float moving = (figure.root.localPosition - before).magnitude / Time.deltaTime;
                if (moving > unit.type.speed * 0.5f)
                {
                    figure.dustDebt += cavalryDust * Time.deltaTime;
                    int puffs = Mathf.FloorToInt(figure.dustDebt);
                    figure.dustDebt -= puffs;
                    if (puffs > 0)
                        Emit(dust, figure.root.position - figure.root.forward * 0.5f, dustColour, puffs);
                }
            }

            // an unanimated figure jabs forward instead of swinging
            float lunge = 0f;
            if (figure.animation == null && unit.lastAttackTime >= 0f && !unit.lastAttackRanged)
            {
                float since = (sim.Time - unit.lastAttackTime) / lungeTime;
                if (since < 1f)
                    lunge = Mathf.Sin(since * Mathf.PI) * lungeDistance;
            }
            figure.visual.localPosition = new Vector3(0f, 0f, lunge);
        }
    }

    /// <summary>Shows what happened in the last sim step: shakes, squashes, dust and the big moments.</summary>
    void ShowEvents()
    {
        foreach (var e in sim.events)
        {
            var at = World(e.position, 0.9f);
            switch (e.kind)
            {
                case BattleSim.EventKind.Contact:
                    // the first blow between the lines: a thud down into the ground
                    Jolt(Vector3.down * contactJolt);
                    Play(contactClip);
                    // usually the moment started as the lines closed; this catches lines that met some other way
                    if (!contactMomentSeen)
                    {
                        contactMomentSeen = true;
                        Moment(at, contactSlowMotion);
                    }
                    break;
                case BattleSim.EventKind.Charge:
                {
                    bool showing = Time.unscaledTime < momentUntil && (focusUnit == e.unit || focusOther == e.other);
                    var along = transform.TransformDirection(new Vector3(e.position.x - e.unit.position.x, 0f, e.position.y - e.unit.position.y)).normalized;
                    // pushed the way the riders were going; bigger for the charge a moment is showing
                    if (showing || (Time.unscaledTime >= nextChargeJoltAt && OnScreen(at)))
                    {
                        nextChargeJoltAt = Time.unscaledTime + chargeJoltGap;
                        Jolt((along + Vector3.down * 0.4f) * chargeJolt * (showing ? 1.6f : 1f));
                    }
                    Play(chargeClip);
                    Squash(e.other, 3f);
                    Flinch(e.other, true);
                    ShowNumber(e.other, e.damage, true);
                    Emit(dust, World(e.position, 0.2f), dustColour, 14);
                    Emit(hits, at, hitColour, 8);
                    // a freeze goes on top of the moment's slow motion
                    if (showing && chargeFreeze > 0f)
                        MMFreezeFrameEvent.Trigger(chargeFreeze, 0f);
                    break;
                }
                case BattleSim.EventKind.BracedCharge:
                    Play(meleeHitClip);
                    Squash(e.unit, 2f);
                    ShowNumber(e.other, e.damage, false);
                    Emit(hits, at, sparkColour, 10);
                    break;
                case BattleSim.EventKind.Hit:
                    Play(meleeHitClip);
                    Squash(e.other, e.rear ? 1.6f : 1f);
                    Flinch(e.other, false);
                    ShowNumber(e.other, e.damage, e.rear);
                    Emit(hits, at, hitColour, e.rear ? 6 : 3);
                    break;
                case BattleSim.EventKind.ArrowHit:
                    Squash(e.other, 0.6f);
                    Flinch(e.other, false);
                    ShowNumber(e.other, e.damage, false);
                    Emit(hits, at, hitColour, 2);
                    break;
                case BattleSim.EventKind.ArrowMiss:
                    Emit(dust, World(e.position, 0.05f), dustColour, 2);
                    break;
                case BattleSim.EventKind.End:
                    // the last one falls, slowly, close up
                    Jolt(Vector3.down * endJolt);
                    Moment(World(e.position, 0f), endSlowMotion, null, null, true);
                    break;
            }
        }
    }

    Vector3 World(Vector2 point, float lift) => transform.TransformPoint(new Vector3(point.x, sim.HeightAt(point) + lift, point.y));

    /// <summary>Plays a hit reaction, unless the figure is busy with another action; a charge's knockback always plays.</summary>
    void Flinch(BattleSim.Unit unit, bool knockback)
    {
        if (unit == null || !unit.Alive || unit.id >= figures.Count)
            return;
        var animation = figures[unit.id].animation;
        if (animation == null || (!knockback && animation.Action != null))
            return;
        var clips = knockback && unit.type.knockbackClips.Length > 0 ? unit.type.knockbackClips : unit.type.hitClips;
        var clip = Pick(clips);
        if (clip != null)
            // a knockback staggers the whole body back, as the charge shoves the unit; a flinch is upper body while moving
            animation.Play(clip, knockback ? 0.08f : 0.12f, knockback ? Mathf.Min(clip.length, simSettings.chargeStagger + 0.3f) : 0f, false, knockback);
    }

    static AnimationClip Pick(AnimationClip[] clips) => clips != null && clips.Length > 0 ? clips[Random.Range(0, clips.Length)] : null;

    void DisposeAnimations()
    {
        foreach (var figure in figures)
        {
            figure.animation?.Dispose();
            figure.animation = null;
        }
    }

    void OnDestroy()
    {
        DisposeAnimations();
        if (upperBodyMask != null)
            Destroy(upperBodyMask);
    }

    void Squash(BattleSim.Unit unit, float strength)
    {
        if (unit == null || unit.id >= figures.Count)
            return;
        var spring = figures[unit.id].spring;
        if (spring != null && unit.Alive)
            spring.Bump(new Vector3(1f, -1.4f, 1f) * hitSquash * strength);
    }

    /// <summary>Knocks the camera this far (world metres, in this direction) and lets it spring back.</summary>
    void Jolt(Vector3 push)
    {
        if (jolts == null || push.sqrMagnitude < 0.0001f)
            return;
        jolts.ImpulseDefinition.ImpulseDuration = joltTime;
        jolts.GenerateImpulseWithVelocity(push);
    }

    static bool OnScreen(Vector3 point)
    {
        var camera = Camera.main;
        if (camera == null)
            return true;
        var view = camera.WorldToViewportPoint(point);
        return view.z > 0f && view.x > 0.05f && view.x < 0.95f && view.y > 0.05f && view.y < 0.95f;
    }

    /// <summary>
    /// Starts the big moments a little before they happen, so the camera is there in time: the two lines about to
    /// meet, and the battle's first cavalry charge about to land.
    /// </summary>
    void WatchForMoments()
    {
        if (sim.Winner != BattleSim.Running)
            return;
        if (!contactMomentSeen)
        {
            // the nearest soldiers of the two lines
            float nearest = float.MaxValue;
            foreach (var a in sim.units)
            {
                if (a.side != 0 || !a.Alive || a.type.role != UnitType.Role.Line)
                    continue;
                foreach (var b in sim.units)
                {
                    if (b.side != 1 || !b.Alive || b.type.role != UnitType.Role.Line)
                        continue;
                    nearest = Mathf.Min(nearest, (a.position - b.position).magnitude - a.type.radius - b.type.radius);
                }
            }
            if (nearest <= contactWarnGap)
            {
                contactMomentSeen = true;
                Moment(LineCentre(), contactSlowMotion);
            }
        }
        for (int i = 0; i < figures.Count && i < sim.units.Count; i++)
        {
            var unit = sim.units[i];
            if (!unit.type.IsCavalry)
                continue;
            float left = sim.ChargeDistanceLeft(unit);
            if (left < 0f)
            {
                figures[i].chargeSeen = false;
                continue;
            }
            if (figures[i].chargeSeen || left > chargeWarnGap)
                continue;
            figures[i].chargeSeen = true;
            if (chargeMomentsSeen < chargeMoments && Moment(FigurePosition(unit), chargeSlowMotion, unit, unit.target))
                chargeMomentsSeen++;
        }
    }

    /// <summary>The middle of both battle lines, where they're about to meet.</summary>
    Vector3 LineCentre()
    {
        Vector2 sum = Vector2.zero;
        int count = 0;
        foreach (var unit in sim.units)
            if (unit.Alive && unit.type.role == UnitType.Role.Line)
            {
                sum += unit.position;
                count++;
            }
        return World(count > 0 ? sum / count : Vector2.zero, 0f);
    }

    Vector3 FigurePosition(BattleSim.Unit unit) =>
        unit.id < figures.Count && figures[unit.id].root != null ? figures[unit.id].root.position : World(unit.position, 0f);

    /// <summary>
    /// Slows time and cuts to the moment camera over a point (following the point between two units if given),
    /// unless another moment ended too recently. True if it started.
    /// </summary>
    bool Moment(Vector3 at, Vector2 scaleAndTime, BattleSim.Unit follow = null, BattleSim.Unit other = null, bool always = false)
    {
        if (scaleAndTime.y <= 0f || (!always && Time.unscaledTime < nextMomentAt))
            return false;
        momentUntil = Time.unscaledTime + scaleAndTime.y;
        nextMomentAt = momentUntil + momentGap;
        MMTimeScaleEvent.Trigger(MMTimeScaleMethods.For, scaleAndTime.x, scaleAndTime.y, true, slowLerpSpeed, false);
        focusUnit = follow;
        focusOther = other;
        focus.position = at;
        return true;
    }

    /// <summary>Keeps the moment camera on the moment while it lasts, and off otherwise.</summary>
    void ShowMoment()
    {
        bool on = Time.unscaledTime < momentUntil;
        if (on && focusUnit != null)
            focus.position = focusOther != null ? Vector3.Lerp(FigurePosition(focusUnit), FigurePosition(focusOther), 0.5f) : FigurePosition(focusUnit);
        if (momentCamera != null && momentCamera.enabled != on)
            momentCamera.enabled = on;
    }

    void SetCameraWeight(Transform member, float weight, bool removeAtZero = true)
    {
        if (cameraGroup == null)
            return;
        int index = cameraGroup.FindMember(member);
        if (index < 0)
            return;
        if (weight <= 0f && removeAtZero)
        {
            cameraGroup.Targets.RemoveAt(index);
            return;
        }
        var target = cameraGroup.Targets[index];
        target.Weight = Mathf.Max(0f, weight);
        cameraGroup.Targets[index] = target;
    }

    ParticleSystem MakeParticles(string name, Vector2 lifetime, Vector2 speed, Vector2 size, float gravity)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(transform, false);
        // the emission hemisphere points up
        holder.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.gravityModifier = gravity;
        main.maxParticles = 600;
        main.playOnAwake = false;
        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.25f;
        var fade = system.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var grow = system.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
        var drag = system.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = 2.5f;
        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        system.Play();
        return system;
    }

    static void Emit(ParticleSystem system, Vector3 position, Color colour, int count)
    {
        if (system == null || count <= 0)
            return;
        var emit = new ParticleSystem.EmitParams { position = position, startColor = colour, applyShapeToPosition = true };
        system.Emit(emit, count);
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

    void OnDisable()
    {
        // leave no slow motion behind
        MMTimeScaleEvent.Trigger(MMTimeScaleMethods.Reset, 1f, 0f, false, 0f, false);
        if (momentCamera != null)
            momentCamera.enabled = false;
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
