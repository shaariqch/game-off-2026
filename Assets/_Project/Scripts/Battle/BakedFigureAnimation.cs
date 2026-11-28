using UnityEngine;

/// <summary>
/// Plays a figure's baked animations (<see cref="BakedFigure"/>) on the GPU: the same behaviour as
/// <see cref="FigureAnimation"/>, but instead of driving bones it tells the figure's own copy of the Baked Figure
/// material which texture rows to read and how to blend them, and the shader skins the mesh.
/// <para>
/// Locomotion is idle, walk and run blended by speed (only two are ever in use at once), with walk and run sped up
/// to the ground speed. Two action slots cross-fade. Below 0.35 m/s an action plays on the whole body; above
/// 1.1 m/s on the upper body only, while the legs keep moving. Riders' actions never take the legs except a held
/// death. When an action plays on the upper body only, its upper bones are re-based onto the walking hips
/// (<c>R = L_hips × A_hips⁻¹</c>) so the body doesn't come apart at the waist; the hips themselves follow the legs.
/// </para>
/// Times advance by the game's delta time, so slow motion and freeze frames apply. <see cref="Dispose"/> destroys
/// the material copy.
/// </summary>
public sealed class BakedFigureAnimation : IFigureAnimation
{
    const int Idle = 0, Walk = 1, Run = 2;
    const float StillSpeed = 0.35f, MovingSpeed = 1.1f;
    const float LegsHandover = 6f;

    static readonly int LocoId = Shader.PropertyToID("_Loco");
    static readonly int[] ActIds = { Shader.PropertyToID("_Act1"), Shader.PropertyToID("_Act2") };
    static readonly int[] RebaseQIds = { Shader.PropertyToID("_Re1Q"), Shader.PropertyToID("_Re2Q") };
    static readonly int[] RebaseTIds = { Shader.PropertyToID("_Re1T"), Shader.PropertyToID("_Re2T") };
    static readonly Vector4 NoTurn = new Vector4(0f, 0f, 0f, 1f);

    readonly BakedFigure baked;
    readonly Material material;
    readonly Transform seat;
    readonly bool hasMask;
    readonly float walkSpeed, runSpeed;

    readonly BakedFigure.Clip[] moves = new BakedFigure.Clip[3];
    readonly float[] moveTimes = new float[3];
    readonly float[] moveSpeeds = { 1f, 1f, 1f };

    readonly BakedFigure.Clip[] slots = new BakedFigure.Clip[2];
    readonly bool[] slotUsed = new bool[2];
    readonly float[] slotTimes = new float[2];
    readonly float[] slotSpeeds = { 1f, 1f };
    readonly float[] weights = new float[2];
    readonly float[] targets = new float[2];
    readonly float[] fadeRates = { 10f, 10f };
    int current;
    float actionEndsIn;
    bool holding, actionWholeBody, mounted, paused;
    float legs = 1f;
    bool warned;

    public AnimationClip Action { get; private set; }
    public bool Holding => holding;
    public bool Mounted
    {
        get => mounted;
        set => mounted = value;
    }

    /// <param name="material">The figure's own copy of the Baked Figure material; destroyed by <see cref="Dispose"/>.</param>
    /// <param name="seat">Moved to the baked seat bone every update (a mount's, for its rider), or null.</param>
    /// <param name="upperBodyMask">Whether actions may play on the upper body only (false: always whole body, as a mount).</param>
    public BakedFigureAnimation(BakedFigure baked, Material material, AnimationClip idle, AnimationClip walk, AnimationClip run,
        float walkSpeed, float runSpeed, float startTime, bool upperBodyMask, Transform seat = null)
    {
        this.baked = baked;
        this.material = material;
        this.seat = seat;
        hasMask = upperBodyMask && baked.hipsBone >= 0;
        this.walkSpeed = Mathf.Max(0.1f, walkSpeed);
        this.runSpeed = Mathf.Max(this.walkSpeed + 0.1f, runSpeed);
        var clips = new[] { idle, walk != null ? walk : idle, run != null ? run : (walk != null ? walk : idle) };
        for (int i = 0; i < 3; i++)
        {
            if (!baked.TryGetClip(clips[i], out moves[i]))
                Warn(clips[i]);
            // start each figure at a different point in its cycles, so a formation doesn't step in unison
            moveTimes[i] = startTime * moves[i].length;
        }
        material.SetTexture("_Poses", baked.poses);
        Apply(0f, 0f, 0f);
    }

    public void SetPaused(bool value) => paused = value;

    public void Update(float deltaTime, float speed)
    {
        if (paused || material == null)
            return;
        // idle to walk up to walking speed, walk to run from there to running speed
        float walk = Mathf.Clamp01(speed / walkSpeed);
        float run = Mathf.Clamp01((speed - walkSpeed) / (runSpeed - walkSpeed));
        for (int i = 0; i < 3; i++)
            moveTimes[i] += deltaTime * moveSpeeds[i];
        // feet keep pace with the ground (used from the next frame, as the Playables version does)
        moveSpeeds[Walk] = Mathf.Clamp(speed / walkSpeed, 0.6f, 1.5f);
        moveSpeeds[Run] = Mathf.Clamp(speed / runSpeed, 0.6f, 1.6f);
        for (int slot = 0; slot < 2; slot++)
            if (slotUsed[slot])
                slotTimes[slot] += deltaTime * slotSpeeds[slot];

        if (Action != null && !holding)
        {
            actionEndsIn -= deltaTime;
            if (actionEndsIn <= 0f)
            {
                targets[current] = 0f;
                Action = null;
            }
        }
        float action = 0f;
        for (int slot = 0; slot < 2; slot++)
        {
            weights[slot] = Mathf.MoveTowards(weights[slot], targets[slot], fadeRates[slot] * deltaTime);
            action += weights[slot];
        }
        if (action > 1f)
        {
            for (int slot = 0; slot < 2; slot++)
                weights[slot] /= action;
        }

        // the legs belong to the action while standing still (or for a whole-body action), to moving otherwise
        float wanted = !hasMask || holding || (actionWholeBody && !mounted) ? 1f
            : mounted ? 0f
            : 1f - Mathf.Clamp01((speed - StillSpeed) / (MovingSpeed - StillSpeed));
        legs = Mathf.MoveTowards(legs, wanted, LegsHandover * deltaTime);
        Apply(walk, run, legs);
    }

    void Apply(float walk, float run, float legsShare)
    {
        // only two of idle, walk and run are ever in use: idle to walk, or walk to run
        int a = run > 0f ? Walk : Idle, b = run > 0f ? Run : Walk;
        float blend = run > 0f ? run : walk;
        float rowA = BakedFigure.Row(moves[a], moveTimes[a]);
        float rowB = BakedFigure.Row(moves[b], moveTimes[b]);
        material.SetVector(LocoId, new Vector4(rowA, rowB, blend, 0f));

        // the walking hips, for re-basing actions that play on the upper body only
        Vector4 hipsTurn = NoTurn, hipsAt = Vector4.zero;
        bool rebase = baked.hipsBone >= 0 && baked.hipsPoses.Length > 0;
        if (rebase)
            Blend(baked.hipsPoses, rowA, rowB, blend, out hipsTurn, out hipsAt);

        Vector4 seatAt = Vector4.zero;
        bool seated = seat != null && baked.seatBone >= 0 && baked.seatPoses.Length > 0;
        if (seated)
            Blend(baked.seatPoses, rowA, rowB, blend, out _, out seatAt);
        float seatLeft = 1f;
        Vector4 seatSum = Vector4.zero;

        for (int slot = 0; slot < 2; slot++)
        {
            float weight = slotUsed[slot] ? weights[slot] : 0f;
            float whole = weight * legsShare, upper = weight * (1f - legsShare);
            if (!hasMask)
            {
                whole = weight;
                upper = 0f;
            }
            float row = weight > 0f ? BakedFigure.Row(slots[slot], slotTimes[slot]) : 0f;
            material.SetVector(ActIds[slot], new Vector4(row, whole, upper, 0f));

            Vector4 turn = NoTurn, offset = Vector4.zero;
            if (rebase && upper > 0f)
            {
                BakedFigure.Sample(baked.hipsPoses, row, out var actionTurn, out var actionAt);
                // R = L × A⁻¹: the action's hips moved onto the walking hips
                var l = ToQuaternion(hipsTurn);
                var r = l * Quaternion.Inverse(ToQuaternion(actionTurn));
                if (r.w < 0f)
                    r = new Quaternion(-r.x, -r.y, -r.z, -r.w);
                var moved = r * (Vector3)actionAt;
                turn = new Vector4(r.x, r.y, r.z, r.w);
                offset = (Vector3)hipsAt - moved;
            }
            material.SetVector(RebaseQIds[slot], turn);
            material.SetVector(RebaseTIds[slot], offset);

            if (seated && whole > 0f)
            {
                BakedFigure.Sample(baked.seatPoses, row, out _, out var at);
                seatSum += at * whole;
                seatLeft -= whole;
            }
        }
        if (seated)
        {
            var at = seatAt * Mathf.Max(0f, seatLeft) + seatSum;
            seat.localPosition = new Vector3(at.x, at.y, at.z);
        }
    }

    /// <summary>One bone's pose blended between two locomotion rows, as the shader blends it.</summary>
    static void Blend(float[] data, float rowA, float rowB, float blend, out Vector4 turn, out Vector4 at)
    {
        BakedFigure.Sample(data, rowA, out var qa, out var pa);
        if (blend <= 0f)
        {
            turn = qa.normalized;
            at = pa;
            return;
        }
        BakedFigure.Sample(data, rowB, out var qb, out var pb);
        if (Vector4.Dot(qa, qb) < 0f)
            qb = -qb;
        turn = Vector4.Lerp(qa, qb, blend).normalized;
        at = Vector4.Lerp(pa, pb, blend);
    }

    static Quaternion ToQuaternion(Vector4 q)
    {
        q = q.sqrMagnitude > 1e-8f ? q.normalized : NoTurn;
        return new Quaternion(q.x, q.y, q.z, q.w);
    }

    public void Play(AnimationClip clip, float fade, float duration = 0f, bool hold = false, bool wholeBodyAlways = false)
    {
        if (material == null || clip == null || holding)
            return;
        if (!baked.TryGetClip(clip, out var bakedClip))
        {
            Warn(clip);
            return;
        }
        float fadeRate = 1f / Mathf.Max(0.01f, fade);
        // the slot not in use takes the new clip; the old one fades out
        targets[current] = 0f;
        fadeRates[current] = fadeRate;
        current = 1 - current;
        float speed = duration > 0f ? clip.length / duration : 1f;
        slots[current] = bakedClip;
        slotUsed[current] = true;
        slotTimes[current] = 0f;
        slotSpeeds[current] = speed;
        weights[current] = 0f;
        targets[current] = 1f;
        fadeRates[current] = fadeRate;
        Action = clip;
        holding = hold;
        actionWholeBody = hold || wholeBodyAlways;
        // fade back out shortly before it ends
        actionEndsIn = clip.length / Mathf.Max(0.01f, speed) - fade;
    }

    void Warn(AnimationClip clip)
    {
        if (warned)
            return;
        warned = true;
        Debug.LogWarning($"{baked.name} has no baked copy of {(clip != null ? clip.name : "a missing clip")}: run Tools > Battle > Bake Figures.", baked);
    }

    public void Dispose()
    {
        if (material == null)
            return;
        if (Application.isPlaying)
            Object.Destroy(material);
        else
            Object.DestroyImmediate(material);
    }
}
