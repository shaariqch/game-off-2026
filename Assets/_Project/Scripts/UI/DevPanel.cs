using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// A small testing panel, opened and closed with the ~ key (Editor and development builds only). Up and down pick an
/// entry, Enter runs it. While it's open the game's own keys are blocked. Entries go in <see cref="BuildEntries"/>,
/// and only the ones the user has asked for.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class DevPanel : MonoBehaviour
{
    sealed class Entry
    {
        public string label;
        /// <summary>Whether it can run now; the row shows dimmed when not.</summary>
        public Func<bool> available;
        public Action run;
        public Label row;
    }

    [SerializeField] StyleSheet styleSheet;
    [SerializeField] TradeRun run;

    readonly List<Entry> entries = new List<Entry>();
    VisualElement panel;
    ScrollView list;
    int selected;
    bool open;

    void Awake()
    {
#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
        Destroy(gameObject);
#endif
    }

    void OnEnable()
    {
        if (run == null)
            run = FindAnyObjectByType<TradeRun>();
        BuildEntries();
        var root = GetComponent<UIDocument>().rootVisualElement;
        root.Clear();
        root.pickingMode = PickingMode.Ignore;
        if (styleSheet != null)
            root.styleSheets.Add(styleSheet);
        panel = new VisualElement { name = "dev-panel" };
        panel.AddToClassList("dev-panel");
        var title = new Label("Dev");
        title.AddToClassList("dev-title");
        panel.Add(title);
        list = new ScrollView(ScrollViewMode.Vertical);
        list.AddToClassList("dev-list");
        list.verticalScrollerVisibility = ScrollerVisibility.Auto;
        list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        foreach (var entry in entries)
        {
            entry.row = new Label(entry.label);
            entry.row.AddToClassList("dev-row");
            list.Add(entry.row);
        }
        panel.Add(list);
        root.Add(panel);
        SetOpen(false);
    }

    void OnDisable()
    {
        GameInput.Unblock(this);
        open = false;
    }

    /// <summary>The panel's entries, in order. Add one only when the user asks for it.</summary>
    void BuildEntries()
    {
        entries.Clear();
        entries.Add(new Entry
        {
            label = "Start the battle now",
            available = () => run != null && run.CanForceBattle,
            run = () => run.ForceBattle(),
        });
    }

    void SetOpen(bool value)
    {
        open = value;
        panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        if (open) GameInput.Block(this, false);
        else GameInput.Unblock(this);
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || panel == null)
            return;
        if (keyboard.backquoteKey.wasPressedThisFrame)
            SetOpen(!open);
        if (!open || entries.Count == 0)
            return;

        int move = (keyboard.downArrowKey.wasPressedThisFrame ? 1 : 0) - (keyboard.upArrowKey.wasPressedThisFrame ? 1 : 0);
        if (move != 0)
        {
            selected = Mathf.Clamp(selected + move, 0, entries.Count - 1);
            list.ScrollTo(entries[selected].row);
        }
        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].row.EnableInClassList("dev-row--selected", i == selected);
            entries[i].row.EnableInClassList("dev-row--unavailable", !entries[i].available());
        }
        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            var entry = entries[selected];
            if (entry.available())
            {
                Debug.Log("[Dev] " + entry.label);
                SetOpen(false);
                entry.run();
            }
        }
    }
}
