using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays one battle figure's animations through a small Playables graph on its Animator: idle, walk and run
/// blended by how fast the figure is moving, and one-off actions (an attack, a shot, a hit, a death) faded in over
/// them. Two action slots let a new action fade in while the last one fades out.
/// <para>
/// Actions are layered: each one plays twice, once on the whole body and once on an upper-body mask. While the
/// figure stands still the whole-body copy shows, so an attack steps and a flinch staggers; once it moves, the legs
/// go back to walking or running and only the upper body acts, so a hit while moving doesn't slide planted feet
/// along the ground. Whole-body actions (a death, a knockback) keep the legs whatever the speed.
/// </para>
/// The graph runs in game time, so slow motion and freeze frames slow and stop it too. Call <see cref="Dispose"/>
/// when the figure goes.
/// </summary>
public sealed class FigureAnimation
{
    const int Idle = 0, Walk = 1, Run = 2;
    const int LocomotionLayer = 0, WholeBodyLayer = 1, UpperBodyLayer = 2;
    /// <summary>Below this speed (m/s) an action owns the legs; above Moving Speed the legs keep moving.</summary>
    const float StillSpeed = 0.35f, MovingSpeed = 1.1f;
    /// <summary>How fast the legs hand over between an action and moving, per second.</summary>
    const float LegsHandover = 6f;

    PlayableGraph graph;
    readonly AnimationLayerMixerPlayable root;
    readonly AnimationMixerPlayable locomotion;
    readonly AnimationClipPlayable[] moves = new AnimationClipPlayable[3];
    /// <summary>The two action slots, as whole-body and upper-body copies.</summary>
    readonly AnimationMixerPlayable wholeBody, upperBody;
    readonly float walkSpeed, runSpeed;
    readonly bool hasMask;
    readonly float[] weights = new float[2];
    readonly float[] targets = new float[2];
    readonly float[] fadeRates = { 10f, 10f };
    int current;
    float actionEndsIn;
    bool holding, actionWholeBody;
    /// <summary>How much the action has the legs: 1 standing still, 0 moving.</summary>
    float legs = 1f;

    public bool Valid => graph.IsValid();
    /// <summary>The clip of the action playing now, or null when only moving.</summary>
    public AnimationClip Action { get; private set; }
    /// <summary>Holding a final pose (a death) until disposed.</summary>
    public bool Holding => holding;

    /// <param name="upperBodyMask">The body parts an action plays on while the figure moves; null plays actions on the whole body always.</param>
    public FigureAnimation(Animator animator, AnimationClip idle, AnimationClip walk, AnimationClip run, float walkSpeed, float runSpeed,
        float startTime, AvatarMask upperBodyMask)
    {
        this.walkSpeed = Mathf.Max(0.1f, walkSpeed);
        this.runSpeed = Mathf.Max(this.walkSpeed + 0.1f, runSpeed);
        graph = PlayableGraph.Create(animator.name + " figure");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Figure", animator);

        locomotion = AnimationMixerPlayable.Create(graph, 3);
        var clips = new[] { idle, walk != null ? walk : idle, run != null ? run : (walk != null ? walk : idle) };
        for (int i = 0; i < 3; i++)
        {
            moves[i] = AnimationClipPlayable.Create(graph, clips[i]);
            // start each figure at a different point in its cycles, so a formation doesn't step in unison
            moves[i].SetTime(startTime * clips[i].length);
            graph.Connect(moves[i], 0, locomotion, i);
        }
        locomotion.SetInputWeight(Idle, 1f);

        wholeBody = AnimationMixerPlayable.Create(graph, 2);
        upperBody = AnimationMixerPlayable.Create(graph, 2);
        root = AnimationLayerMixerPlayable.Create(graph, 3);
        graph.Connect(locomotion, 0, root, LocomotionLayer);
        graph.Connect(wholeBody, 0, root, WholeBodyLayer);
        graph.Connect(upperBody, 0, root, UpperBodyLayer);
        if (upperBodyMask != null)
            root.SetLayerMaskFromAvatarMask(UpperBodyLayer, upperBodyMask);
        root.SetInputWeight(LocomotionLayer, 1f);
        root.SetInputWeight(WholeBodyLayer, 0f);
        root.SetInputWeight(UpperBodyLayer, 0f);
        hasMask = upperBodyMask != null;
        output.SetSourcePlayable(root);
        graph.Play();
    }

    /// <summary>Blends the moving clips for this ground speed and advances the action fades.</summary>
    public void Update(float deltaTime, float speed)
    {
        if (!graph.IsValid())
            return;
        // idle to walk up to walking speed, walk to run from there to running speed
        float walk = Mathf.Clamp01(speed / walkSpeed);
        float run = Mathf.Clamp01((speed - walkSpeed) / (runSpeed - walkSpeed));
        locomotion.SetInputWeight(Idle, 1f - walk);
        locomotion.SetInputWeight(Walk, walk * (1f - run));
        locomotion.SetInputWeight(Run, walk * run);
        // feet keep pace with the ground
        moves[Walk].SetSpeed(Mathf.Clamp(speed / walkSpeed, 0.6f, 1.5f));
        moves[Run].SetSpeed(Mathf.Clamp(speed / runSpeed, 0.6f, 1.6f));

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
            action = 1f;
        }
        // each copy mixes the two slots at full strength; the layers' weights fade the action as a whole
        for (int slot = 0; slot < 2; slot++)
        {
            float share = action > 0.0001f ? weights[slot] / action : 0f;
            wholeBody.SetInputWeight(slot, share);
            upperBody.SetInputWeight(slot, share);
        }

        // the legs belong to the action while standing still (or for a whole-body action), to moving otherwise
        float wanted = !hasMask || actionWholeBody ? 1f : 1f - Mathf.Clamp01((speed - StillSpeed) / (MovingSpeed - StillSpeed));
        legs = Mathf.MoveTowards(legs, wanted, LegsHandover * deltaTime);
        root.SetInputWeight(WholeBodyLayer, action * legs);
        root.SetInputWeight(UpperBodyLayer, action * (1f - legs));
    }

    /// <summary>Fades a one-off clip in over whatever is playing.</summary>
    /// <param name="duration">Seconds to play it for; the clip is sped up or slowed to fit. 0 plays it at its own speed.</param>
    /// <param name="hold">Stay on its last frame until disposed (a death).</param>
    /// <param name="wholeBodyAlways">The legs play it even while the figure moves (a death, a knockback).</param>
    public void Play(AnimationClip clip, float fade, float duration = 0f, bool hold = false, bool wholeBodyAlways = false)
    {
        if (!graph.IsValid() || clip == null || holding)
            return;
        float fadeRate = 1f / Mathf.Max(0.01f, fade);
        // the slot not in use takes the new clip; the old one fades out
        targets[current] = 0f;
        fadeRates[current] = fadeRate;
        current = 1 - current;
        float speed = duration > 0f ? clip.length / duration : 1f;
        Replace(wholeBody, clip, speed, hold);
        Replace(upperBody, clip, speed, hold);
        weights[current] = 0f;
        targets[current] = 1f;
        fadeRates[current] = fadeRate;
        Action = clip;
        holding = hold;
        actionWholeBody = hold || wholeBodyAlways;
        // fade back out shortly before it ends
        actionEndsIn = clip.length / Mathf.Max(0.01f, speed) - fade;
    }

    void Replace(AnimationMixerPlayable mixer, AnimationClip clip, float speed, bool hold)
    {
        var old = mixer.GetInput(current);
        if (old.IsValid())
        {
            graph.Disconnect(mixer, current);
            old.Destroy();
        }
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetSpeed(speed);
        playable.SetDuration(clip.length);
        if (hold)
            playable.SetApplyFootIK(false);
        graph.Connect(playable, 0, mixer, current);
    }

    public void Dispose()
    {
        if (graph.IsValid())
            graph.Destroy();
    }
}
