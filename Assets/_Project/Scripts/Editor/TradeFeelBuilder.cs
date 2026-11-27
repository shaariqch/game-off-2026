using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using MoreMountains.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// <c>Tools > Trade > Build Trade Feel</c>: makes (or remakes) the Trade HUD's <see cref="TradeFeel"/> and its Feel
/// players, one set per Shift key, with the default sounds and UI Toolkit feedbacks. Tune the players in the
/// inspector afterwards; running this again replaces them.
/// </summary>
public static class TradeFeelBuilder
{
    const string Sfx = "Assets/_Project/ThirdParty/CelerisLab/CompleteUISFX/";
    static readonly Color Gold = new Color(0.890f, 0.722f, 0.400f);
    static readonly Color Cream = new Color(0.945f, 0.902f, 0.816f);

    [MenuItem("Tools/Trade/Build Trade Feel")]
    public static void Build()
    {
        var hud = Object.FindAnyObjectByType<TradeHud>();
        if (hud == null)
        {
            Debug.LogError("Build Trade Feel: no TradeHud in the open scenes.");
            return;
        }
        var document = hud.GetComponent<UIDocument>();

        var old = hud.transform.Find("Trade Feel");
        if (old != null)
            Undo.DestroyObjectImmediate(old.gameObject);
        var root = new GameObject("Trade Feel");
        Undo.RegisterCreatedObjectUndo(root, "Build Trade Feel");
        root.transform.SetParent(hud.transform, false);
        var feel = root.AddComponent<TradeFeel>();

        var so = new SerializedObject(feel);
        so.FindProperty("run").objectReferenceValue = Object.FindAnyObjectByType<TradeRun>();
        BuildSide(so.FindProperty("buy"), root.transform, document, TradeRun.Side.Buy);
        BuildSide(so.FindProperty("sell"), root.transform, document, TradeRun.Side.Sell);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        Selection.activeGameObject = root;
        Debug.Log("Build Trade Feel: made the Shift keys' Feel players under " + hud.name + "/Trade Feel.");
    }

    static void BuildSide(SerializedProperty players, Transform parent, UIDocument doc, TradeRun.Side side)
    {
        bool buy = side == TradeRun.Side.Buy;
        string key = buy ? "buy" : "sell", count = key + "-count";
        var group = new GameObject(buy ? "Buy" : "Sell").transform;
        group.SetParent(parent, false);

        // press: the key squashes in, the count pops, a wooden thunk
        var p = Player(group, "Press");
        Punch(Scale(p, doc, key), 0.16f, 1f, 0.93f, 0.3f);
        Punch(Scale(p, doc, count), 0.18f, 1f, 1.4f, 0.15f);
        Sound(p, 0.9f, 0.96f, 1.04f, "basic_interactions_and_navigation/buttons/wooden_button/wooden_button_click_in_01.wav",
            "basic_interactions_and_navigation/buttons/wooden_button/wooden_button_click_in_02.wav",
            "basic_interactions_and_navigation/buttons/wooden_button/wooden_button_click_in_06.wav");
        players.FindPropertyRelative("press").objectReferenceValue = p;

        // release: the key springs back a little past its size, a light click
        p = Player(group, "Release");
        Punch(Scale(p, doc, key), 0.22f, 1f, 1.05f, 0.35f);
        Sound(p, 0.8f, 0.95f, 1.05f, "basic_interactions_and_navigation/buttons/wooden_button/wooden_button_click_out_03.wav",
            "basic_interactions_and_navigation/buttons/wooden_button/wooden_button_click_out_05.wav",
            "basic_interactions_and_navigation/buttons/wooden_button/wooden_button_click_out_07.wav");
        players.FindPropertyRelative("release").objectReferenceValue = p;

        // count: the number bumps with every unit, a short tick (TradeFeel raises its pitch with the count)
        p = Player(group, "Count");
        Punch(Scale(p, doc, count), 0.12f, 1f, 1.3f, 0.2f);
        Sound(p, 0.45f, 1f, 1f, "basic_interactions_and_navigation/checkbox_tick_04.wav");
        players.FindPropertyRelative("count").objectReferenceValue = p;

        // capped: the key shakes no
        p = Player(group, "Capped");
        Shake(Translate(p, doc, key), 0.32f, 9f);
        Sound(p, 0.8f, 1f, 1f, "shop_and_economy/insufficient_currency_01.wav");
        players.FindPropertyRelative("capped").objectReferenceValue = p;

        // cancelled: a smaller shake, a closing swish
        p = Player(group, "Cancelled");
        Shake(Translate(p, doc, key), 0.25f, 5f);
        Sound(p, 0.8f, 1f, 1f, "basic_interactions_and_navigation/menu_close_01.wav");
        players.FindPropertyRelative("cancelled").objectReferenceValue = p;

        // traded: the word pops up over the line, the line and the fill flash, the key and the cash jump
        p = Player(group, "Traded");
        var text = (MMF_UIToolkitText)p.AddFeedback(typeof(MMF_UIToolkitText));
        Target(text, doc, "trade-result");
        text.NewText = buy ? "Success!" : "Sold!";

        var fade = (MMF_UIToolkitOpacity)p.AddFeedback(typeof(MMF_UIToolkitOpacity));
        Target(fade, doc, "trade-result");
        fade.Mode = MMF_UIToolkitFloatBase.Modes.Interpolate;
        fade.AllowAdditivePlays = true;
        fade.Duration = 1.15f;
        fade.Curve = Tween(new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.07f, 1f), new Keyframe(0.72f, 1f),
            new Keyframe(1f, 0f)));
        fade.CurveRemapZero = 0f;
        fade.CurveRemapOne = 1f;

        var pop = Scale(p, doc, "trade-result");
        pop.Duration = 0.4f;
        pop.CurveX = pop.CurveY = Tween(new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.35f, 1.25f),
            new Keyframe(0.65f, 0.95f), new Keyframe(1f, 1f)));
        pop.CurveRemapZeroX = pop.CurveRemapZeroY = 0.5f;
        pop.CurveRemapOneX = pop.CurveRemapOneY = 1f;

        var rise = Translate(p, doc, "trade-result");
        rise.AnimateX = false;
        rise.AnimateY = true;
        rise.Duration = 1.15f;
        rise.CurveY = Tween(new AnimationCurve(new Keyframe(0f, 0f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f)));
        rise.CurveRemapZeroY = 10f;
        rise.CurveRemapOneY = -12f;

        var mid = Scale(p, doc, "trade-mid");
        mid.AnimateX = false;
        Punch(mid, 0.3f, 1f, 1.7f, 0.25f);
        Flash(p, doc, "trade-mid", Gold, Cream, 0.45f);
        Flash(p, doc, buy ? "trade-fill-buy" : "trade-fill-sell", Cream, Gold, 0.35f);

        Punch(Scale(p, doc, key), 0.3f, 1f, 1.1f, 0.3f);
        Punch(Scale(p, doc, "cash-value"), 0.35f, 1f, 1.3f, 0.25f);
        var cashColour = (MMF_UIToolkitTextColor)p.AddFeedback(typeof(MMF_UIToolkitTextColor));
        Target(cashColour, doc, "cash-value");
        cashColour.Duration = 0.6f;
        cashColour.AllowAdditivePlays = true;
        cashColour.ColorOverTime = Gradient(Gold, Cream);

        if (buy)
        {
            Sound(p, 1f, 0.98f, 1.02f, "shop_and_economy/purchase_success_01.wav");
            Sound(p, 0.5f, 1f, 1f, "shop_and_economy/transaction_complete.wav");
        }
        else
        {
            Sound(p, 1f, 0.98f, 1.02f, "shop_and_economy/item_sold_01.wav");
            Sound(p, 0.45f, 1f, 1f, "positive_feedback_and_success/currency_gained.wav");
        }
        players.FindPropertyRelative("traded").objectReferenceValue = p;
    }

    static MMF_Player Player(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var player = go.AddComponent<MMF_Player>();
        // TradeFeel initialises the players once the HUD has built its elements
        player.InitializationMode = MMFeedbacks.InitializationModes.Script;
        return player;
    }

    static void Target(MMF_UIToolkit feedback, UIDocument doc, string element)
    {
        feedback.TargetDocument = doc;
        feedback.QueryMode = MMF_UIToolkit.QueryModes.Name;
        feedback.Query = element;
        feedback.Label = feedback.Label + " " + element;
    }

    static MMF_UIToolkitScale Scale(MMF_Player player, UIDocument doc, string element)
    {
        var scale = (MMF_UIToolkitScale)player.AddFeedback(typeof(MMF_UIToolkitScale));
        Target(scale, doc, element);
        scale.Mode = MMF_UIToolkitVector2Base.Modes.Interpolate;
        scale.AllowAdditivePlays = true;
        return scale;
    }

    static MMF_UIToolkitTranslate Translate(MMF_Player player, UIDocument doc, string element)
    {
        var move = (MMF_UIToolkitTranslate)player.AddFeedback(typeof(MMF_UIToolkitTranslate));
        Target(move, doc, element);
        move.Mode = MMF_UIToolkitVector2Base.Modes.Interpolate;
        move.AllowAdditivePlays = true;
        move.AnimateY = false;
        return move;
    }

    /// <summary>From <paramref name="rest"/> to <paramref name="peak"/> at <paramref name="peakAt"/> and back, ending at rest.</summary>
    static void Punch(MMF_UIToolkitVector2Base feedback, float duration, float rest, float peak, float peakAt)
    {
        feedback.Duration = duration;
        var curve = Tween(new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(peakAt, 1f), new Keyframe(1f, 0f)));
        feedback.CurveX = curve;
        feedback.CurveY = Tween(new AnimationCurve(curve.Curve.keys));
        feedback.CurveRemapZeroX = feedback.CurveRemapZeroY = rest;
        feedback.CurveRemapOneX = feedback.CurveRemapOneY = peak;
    }

    /// <summary>Side to side, dying away, by up to <paramref name="pixels"/>.</summary>
    static void Shake(MMF_UIToolkitTranslate feedback, float duration, float pixels)
    {
        feedback.Duration = duration;
        feedback.CurveX = Tween(new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.12f, 1f), new Keyframe(0.32f, -0.8f),
            new Keyframe(0.52f, 0.55f), new Keyframe(0.72f, -0.3f), new Keyframe(1f, 0f)));
        feedback.CurveRemapZeroX = 0f;
        feedback.CurveRemapOneX = pixels;
    }

    static void Flash(MMF_Player player, UIDocument doc, string element, Color from, Color to, float duration)
    {
        var flash = (MMF_UIToolkitBackgroundColor)player.AddFeedback(typeof(MMF_UIToolkitBackgroundColor));
        Target(flash, doc, element);
        flash.Duration = duration;
        flash.AllowAdditivePlays = true;
        flash.ColorOverTime = Gradient(from, to);
    }

    static void Sound(MMF_Player player, float volume, float minPitch, float maxPitch, params string[] clips)
    {
        var sound = (MMF_Sound)player.AddFeedback(typeof(MMF_Sound));
        var loaded = new AudioClip[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            loaded[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(Sfx + clips[i]);
            if (loaded[i] == null)
                Debug.LogWarning("Build Trade Feel: no clip at " + Sfx + clips[i]);
        }
        if (loaded.Length == 1) sound.Sfx = loaded[0];
        else sound.RandomSfx = loaded;
        sound.Label = "Sound " + (loaded[0] != null ? loaded[0].name : "?");
        sound.PlayMethod = MMF_Sound.PlayMethods.Pool;
        sound.PoolSize = 8;
        sound.MinVolume = sound.MaxVolume = volume;
        sound.MinPitch = minPitch;
        sound.MaxPitch = maxPitch;
        sound.SpatialBlend = 0f;
    }

    static MMTweenType Tween(AnimationCurve curve) =>
        new MMTweenType(curve, "", "Mode", (int)MMF_UIToolkitVector2Base.Modes.Interpolate,
            (int)MMF_UIToolkitVector2Base.Modes.ToDestination);

    static Gradient Gradient(Color from, Color to) => new Gradient
    {
        colorKeys = new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
        alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) },
    };
}
