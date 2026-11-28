using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shows a <see cref="TradeRun"/> over the map: cash and the timeline along the top, the list on the right (the
/// market's units, the tip, and the enemy's types to scout),
/// event notices pinned to their places, and the two Shift keys along the bottom. Layout and look are in
/// UI/Trade HUD.uxml and .uss; this only fills them in.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class TradeHud : MonoBehaviour
{
    [SerializeField] TradeRun run;
    [Tooltip("The camera the map is seen through, for pinning notices to places. Main camera if empty.")]
    [SerializeField] Camera mapCamera;
    [SerializeField] Texture2D cashIcon;
    [SerializeField] Texture2D battleIcon;

    [Header("List")]
    [SerializeField] Texture2D tipIcon;
    [Tooltip("Shown under the Tip row when it's selected.")]
    [SerializeField] string tipDescription = "Reveals an event before it happens.";
    [Tooltip("Shown under an enemy row when it's selected.")]
    [SerializeField] string enemyDescription = "Scouting shows how many they have brought.";
    [SerializeField] Texture2D raidIcon;
    [Tooltip("Shown under the raid row when it's selected: {0} bandits, {1} the place, {2} the pay.")]
    [SerializeField] string raidDescription = "{0} bandits near {1}. Your whole army rides out; a win pays {2}.";

    [Header("Notices")]
    [Tooltip("Notices above this or below the second (reference pixels from the top) show as a ping on that edge instead.")]
    [SerializeField] Vector2 noticeBand = new Vector2(96f, 650f);
    [Tooltip("A notice opens to the left of its place when it would reach past this x (the unit list's left edge).")]
    [SerializeField] float noticeRightLimit = 990f;
    [SerializeField] Color noticeColour = new Color(0.498f, 0.769f, 0.788f);
    [SerializeField] Color invasionColour = new Color(0.878f, 0.396f, 0.31f);
    [SerializeField] Color raidColour = new Color(0.89f, 0.72f, 0.4f);
    [Tooltip("A notice fades out as its event's effect wears off: fully shown above the second share of the effect " +
             "left, gone at the first.")]
    [SerializeField] Vector2 noticeFade = new Vector2(0.5f, 0.75f);
    [Tooltip("Place names in a notice's sentence show in this colour, apart from the event colours.")]
    [SerializeField] Color placeColour = new Color(0.79f, 0.71f, 0.95f);

    [Header("Trade bar")]
    [Tooltip("Seconds the bar stays full after a trade goes through, while its word shows, before it fades.")]
    [SerializeField, Min(0f)] float tradeHold = 0.9f;

    sealed class Row
    {
        public TradeRun.RowKind kind;
        public VisualElement root;
        public Label held, price;
        public HudShape trend;
    }

    sealed class Key
    {
        public VisualElement root;
        public Label count;
        public HudShape arrow;
    }

    sealed class Notice
    {
        /// <summary>The event it's for, or null for a raid's notice.</summary>
        public MarketEvent marketEvent;
        public TradeRun.Raid raid;
        public MapPlace place;
        public VisualElement root, panel, ping, dot;
        public HudShape chevron;
        /// <summary>Known from a tip and still to come.</summary>
        public bool pending;
    }

    VisualElement hud, units, unitRows, eventDots, now, trackBehind, timeline, notices;
    VisualElement trade, tradeFillBuy, tradeFillSell;
    // the last trade that went through, so the bar can stay full for a moment after it
    TradeRun.Side tradedSide;
    float tradedAt = float.NegativeInfinity;
    Label cashValue, battleLabel, description;
    readonly List<Row> rows = new List<Row>();
    // the list's group headers, shown only while the list has focus
    readonly List<VisualElement> listExtras = new List<VisualElement>();
    int rowsForBattle = -1;
    readonly List<(MarketEvent marketEvent, VisualElement dot)> dots = new List<(MarketEvent, VisualElement)>();
    readonly List<Notice> shown = new List<Notice>();
    // where notices have been placed so far this frame, so later ones move clear of them
    readonly List<Rect> placed = new List<Rect>();
    Key buyKey, sellKey;
    int builtForBattle = -1;
    bool built;

    void OnEnable()
    {
        if (run == null)
            run = FindAnyObjectByType<TradeRun>();
        var root = GetComponent<UIDocument>().rootVisualElement;
        hud = root.Q("hud");
        if (hud == null || run == null)
        {
            Debug.LogError("TradeHud needs the Trade HUD layout on its UI Document and a TradeRun.", this);
            return;
        }
        units = hud.Q("units");
        unitRows = hud.Q("unit-rows");
        eventDots = hud.Q("event-dots");
        now = hud.Q("now");
        trackBehind = hud.Q("track-behind");
        timeline = hud.Q("timeline");
        notices = hud.Q("notices");
        cashValue = hud.Q<Label>("cash-value");
        battleLabel = hud.Q<Label>("battle-label");
        hud.Q("cash-icon").style.backgroundImage = cashIcon;
        hud.Q("battle-icon").style.backgroundImage = battleIcon;
        // the legend's "coming up" dot is the same dashed ring as the timeline's and the notices'
        foreach (var ringDot in hud.Query(className: "legend-dot--ring").ToList())
        {
            ringDot.Clear();
            ringDot.Add(HudShape.Ring(new Color(0.945f, 0.902f, 0.816f), 2f));
        }
        trade = hud.Q("trade");
        tradeFillBuy = hud.Q("trade-fill-buy");
        tradeFillSell = hud.Q("trade-fill-sell");
        hud.Q("trade-result").style.opacity = 0f;

        BuildRows();
        buyKey = BuildKey(hud.Q("buy"), TradeRun.Side.Buy);
        sellKey = BuildKey(hud.Q("sell"), TradeRun.Side.Sell);
        notices.Clear();
        shown.Clear();
        builtForBattle = -1;
        run.EventStarted += OnEventStarted;
        run.Tipped += OnTipped;
        run.Traded += OnTraded;
        run.RaidOpened += OnRaidOpened;
        tradedAt = float.NegativeInfinity;
        built = true;
    }

    void OnDisable()
    {
        if (run != null)
        {
            run.EventStarted -= OnEventStarted;
            run.Tipped -= OnTipped;
            run.Traded -= OnTraded;
            run.RaidOpened -= OnRaidOpened;
        }
        built = false;
    }

    /// <summary>
    /// The list, one row per <see cref="TradeRun"/> row: the market's units under Held and Price, then the tip, then
    /// the enemy's types under Count and Scout. The selected row's description sits under it.
    /// </summary>
    void BuildRows()
    {
        unitRows.Clear();
        rows.Clear();
        listExtras.Clear();
        unitRows.Add(Header("", "Held", "Price"));
        for (int i = 0; i < run.RowCount; i++)
        {
            var kind = run.KindOf(i);
            if (kind == TradeRun.RowKind.Tip)
            {
                var rule = new VisualElement();
                rule.AddToClassList("list-rule");
                unitRows.Add(rule);
                listExtras.Add(rule);
            }
            else if (kind == TradeRun.RowKind.Enemy && run.KindOf(i - 1) != TradeRun.RowKind.Enemy)
                unitRows.Add(Header("Enemy", "Count", "Scout"));

            var (iconImage, title) = kind switch
            {
                TradeRun.RowKind.Unit => (run.Unit(i).icon, run.Unit(i).name),
                TradeRun.RowKind.Tip => (tipIcon, "Tip"),
                TradeRun.RowKind.Raid => (raidIcon != null ? raidIcon : battleIcon, "Raid"),
                _ => (run.EnemyAt(i).type.icon, run.EnemyAt(i).type.name),
            };
            var row = new Row { kind = kind, root = new VisualElement() };
            row.root.AddToClassList("unit-row");
            var name = new VisualElement();
            name.AddToClassList("col-name");
            var icon = new VisualElement();
            icon.AddToClassList("icon");
            icon.AddToClassList("unit-icon");
            icon.style.backgroundImage = iconImage;
            name.Add(icon);
            var label = new Label(title);
            label.AddToClassList("serif");
            label.AddToClassList("unit-name");
            name.Add(label);
            row.root.Add(name);

            row.held = new Label();
            row.held.AddToClassList("col-held");
            row.held.AddToClassList("number");
            row.held.AddToClassList("unit-held");
            row.root.Add(row.held);

            var price = new VisualElement();
            price.AddToClassList("col-price");
            row.price = new Label();
            row.price.AddToClassList("number");
            row.price.AddToClassList("unit-price");
            price.Add(row.price);
            row.trend = new HudShape(HudShape.Kind.TriangleUp);
            row.trend.AddToClassList("trend");
            price.Add(row.trend);
            row.root.Add(price);

            unitRows.Add(row.root);
            rows.Add(row);
        }
        description = new Label();
        description.name = "description";
        description.AddToClassList("muted");
        unitRows.Add(description);
        rowsForBattle = run.Phase;
    }

    VisualElement Header(string title, string held, string price)
    {
        var header = new VisualElement();
        header.AddToClassList("unit-header");
        header.AddToClassList("muted");
        var name = new Label(title);
        name.AddToClassList("col-name");
        header.Add(name);
        var heldLabel = new Label(held);
        heldLabel.AddToClassList("col-held");
        header.Add(heldLabel);
        var priceLabel = new Label(price);
        priceLabel.AddToClassList("col-price");
        header.Add(priceLabel);
        listExtras.Add(header);
        return header;
    }

    Key BuildKey(VisualElement root, TradeRun.Side side)
    {
        root.Clear();
        var (content, count, arrow) = KeyContent(side);
        // named so the TradeFeel players can find them
        count.name = root.name + "-count";
        root.Add(content);
        return new Key { root = root, count = count, arrow = arrow };
    }

    static (VisualElement content, Label count, HudShape arrow) KeyContent(TradeRun.Side side)
    {
        var content = new VisualElement();
        content.AddToClassList("key-content");
        content.pickingMode = PickingMode.Ignore;
        var arrow = new HudShape(side == TradeRun.Side.Buy ? HudShape.Kind.ArrowUp : HudShape.Kind.ArrowDown);
        arrow.AddToClassList("key-arrow");
        var label = new Label(side == TradeRun.Side.Buy ? "Buy" : "Sell");
        label.AddToClassList("serif");
        label.AddToClassList("key-label");
        var count = new Label();
        count.AddToClassList("number");
        count.AddToClassList("key-count");
        if (side == TradeRun.Side.Buy)
        {
            content.Add(arrow);
            content.Add(label);
            content.Add(count);
        }
        else
        {
            content.Add(label);
            content.Add(count);
            content.Add(arrow);
            // the count sits between the word and the arrow
            count.style.marginRight = 14;
        }
        return (content, count, arrow);
    }

    void BuildDots()
    {
        eventDots.Clear();
        dots.Clear();
        foreach (var marketEvent in run.Events)
        {
            var dot = new VisualElement();
            dot.AddToClassList("dot");
            dot.AddToClassList(marketEvent.category == MarketEvent.Category.Invasion ? "dot--invasion" : "dot--notice");
            dot.style.left = Length.Percent(100f * Mathf.Clamp01(marketEvent.time / run.PhaseLength));
            // the same numeral as its notice, so the two can be matched; filled in once the event has a number
            var number = new Label();
            number.AddToClassList("serif");
            number.AddToClassList("dot-number");
            number.pickingMode = PickingMode.Ignore;
            dot.Add(number);
            // still to come: a dashed ring in the event's colour (shown by the dot--pending state)
            dot.Add(HudShape.Ring(Colour(marketEvent), 2.5f));
            eventDots.Add(dot);
            dots.Add((marketEvent, dot));
        }
        builtForBattle = run.Phase;
    }

    void OnEventStarted(MarketEvent marketEvent)
    {
        // a notice a tip put up ahead of time now shows as happening
        foreach (var notice in shown)
        {
            if (notice.marketEvent != marketEvent)
                continue;
            SetPending(notice, false);
            return;
        }
        ShowNotice(marketEvent, false);
    }

    void OnTipped(MarketEvent marketEvent) => ShowNotice(marketEvent, null, true);

    void OnRaidOpened(TradeRun.Raid raid) => ShowNotice(null, raid, false);

    void ShowNotice(MarketEvent marketEvent, bool pending) => ShowNotice(marketEvent, null, pending);

    /// <summary>A notice pinned to the place of an event, or of a raid (<paramref name="marketEvent"/> null).</summary>
    void ShowNotice(MarketEvent marketEvent, TradeRun.Raid raid, bool pending)
    {
        string placeName = marketEvent != null ? marketEvent.place : raid.place;
        string message = marketEvent != null ? marketEvent.text : raid.text;
        var place = MapPlace.Find(placeName);
        if (place == null)
        {
            Debug.LogWarning($"No MapPlace called {placeName} for \"{message}\".", this);
            return;
        }
        var colour = marketEvent != null ? Colour(marketEvent) : raidColour;
        var notice = new Notice { marketEvent = marketEvent, raid = raid, place = place, root = new VisualElement() };
        notice.root.AddToClassList("notice");
        notice.root.pickingMode = PickingMode.Ignore;
        var marker = new VisualElement();
        marker.AddToClassList("notice-marker");
        notice.root.Add(marker);
        notice.panel = new VisualElement();
        notice.panel.AddToClassList("panel");
        notice.panel.AddToClassList("notice-panel");
        notice.dot = new VisualElement();
        notice.dot.AddToClassList("notice-dot");
        // still to come: a dashed ring (shown while pending); happening: filled
        notice.dot.Add(HudShape.Ring(colour, 2f));
        notice.panel.Add(notice.dot);
        if (marketEvent != null)
        {
            var number = new Label(TradeRun.Roman(run.Number(marketEvent)));
            number.AddToClassList("serif");
            number.AddToClassList("notice-number");
            notice.panel.Add(number);
            // what it does, plainly: the unit's list icon and which way its price goes (working it out from the
            // sentence alone was unclear and, the user found, not fun)
            if (marketEvent.affects != null)
            {
                var unit = new VisualElement();
                unit.AddToClassList("icon");
                unit.AddToClassList("notice-unit");
                unit.style.backgroundImage = marketEvent.affects.icon;
                notice.panel.Add(unit);
                var trend = new HudShape(marketEvent.strength >= 0f ? HudShape.Kind.TriangleUp : HudShape.Kind.TriangleDown);
                trend.AddToClassList("notice-trend");
                notice.panel.Add(trend);
            }
        }
        var text = new Label(Highlight(message, colour, placeName, placeColour));
        text.AddToClassList("notice-text");
        notice.panel.Add(text);
        notice.root.Add(notice.panel);

        notice.ping = new VisualElement();
        notice.ping.AddToClassList("panel");
        notice.ping.AddToClassList("ping");
        notice.ping.pickingMode = PickingMode.Ignore;
        notice.chevron = new HudShape(HudShape.Kind.ChevronDown);
        notice.chevron.style.width = 18;
        notice.chevron.style.height = 18;
        notice.chevron.SetColour(colour);
        notice.ping.Add(notice.chevron);

        SetPending(notice, pending);
        notices.Add(notice.root);
        notices.Add(notice.ping);
        shown.Add(notice);
    }

    /// <summary>A notice for an event still to come shows its dot as a ring, as the timeline does.</summary>
    void SetPending(Notice notice, bool pending)
    {
        notice.pending = pending;
        notice.dot.EnableInClassList("notice-dot--pending", pending);
        notice.dot[0].style.display = pending ? DisplayStyle.Flex : DisplayStyle.None;
        notice.dot.style.backgroundColor = pending ? Color.clear
            : notice.marketEvent != null ? Colour(notice.marketEvent) : raidColour;
    }

    /// <summary>
    /// Words between asterisks in an event's sentence are its clue (which unit it moves); they show in the event's
    /// colour, so the part that matters stands out. The place's name shows in its own colour.
    /// </summary>
    static string Highlight(string message, Color colour, string place, Color placeColour)
    {
        string text = message ?? "";
        if (!string.IsNullOrEmpty(place))
            text = System.Text.RegularExpressions.Regex.Replace(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(place)}\b",
                $"<color=#{ColorUtility.ToHtmlStringRGB(placeColour)}>{place}</color>");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\*([^*]+)\*",
            $"<color=#{ColorUtility.ToHtmlStringRGB(colour)}>$1</color>");
    }

    Color Colour(MarketEvent marketEvent) =>
        marketEvent.category == MarketEvent.Category.Invasion ? invasionColour : noticeColour;

    void LateUpdate()
    {
        if (!built || run == null || run.Market == null)
            return;
        if (builtForBattle != run.Phase || dots.Count != run.Events.Count)
        {
            // a new phase has new events: the old phase's notices go
            bool newPhase = builtForBattle != run.Phase;
            if (newPhase)
            {
                notices.Clear();
                shown.Clear();
            }
            BuildDots();
            // a visible event is a known one: its notice is up from the start, as a tipped event's is
            if (newPhase)
                foreach (var marketEvent in run.Events)
                    if ((marketEvent.known || run.WasTipped(marketEvent)) && !run.HasHappened(marketEvent))
                        ShowNotice(marketEvent, true);
        }
        if (rowsForBattle != run.Phase || rows.Count != run.RowCount)
            BuildRows();

        hud.EnableInClassList("hud--battle", run.AtBattle);
        // a battle fought on the map has the screen to itself
        hud.EnableInClassList("hud--fighting", run.Fighting);
        cashValue.text = run.Cash.ToString();

        float progress = Mathf.Clamp01(run.PhaseTime / run.PhaseLength);
        now.style.left = Length.Percent(progress * 100f);
        trackBehind.style.width = Length.Percent(progress * 100f);
        foreach (var (marketEvent, dot) in dots)
        {
            bool happened = run.HasHappened(marketEvent);
            dot.style.display = run.IsKnown(marketEvent) || happened ? DisplayStyle.Flex : DisplayStyle.None;
            dot.EnableInClassList("dot--past", happened);
            // still to come: a dashed ring, as on its notice; filled once it happens
            dot.EnableInClassList("dot--pending", !happened);
            dot[1].style.display = happened ? DisplayStyle.None : DisplayStyle.Flex;
            // a hidden event only gets its number when a tip reveals it or it happens
            ((Label)dot[0]).text = TradeRun.Roman(run.Number(marketEvent));
        }
        battleLabel.text = $"Battle {run.Battle} of {run.BattlesPerRun}";
        timeline.EnableInClassList("timeline--battle", run.AtBattle);

        bool mapFocus = run.CurrentFocus == TradeRun.Focus.Map;
        units.EnableInClassList("units--map-focus", mapFocus);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool selected = i == run.Selected;
            row.root.EnableInClassList("unit-row--selected", selected);
            // with the map focused the list shrinks to the selected row, so it covers less of the map
            row.root.style.display = mapFocus && !selected ? DisplayStyle.None : DisplayStyle.Flex;
            int trend = 0;
            switch (row.kind)
            {
                case TradeRun.RowKind.Unit:
                    int held = run.Held(i);
                    row.held.text = held.ToString();
                    row.held.EnableInClassList("unit-held--none", held == 0);
                    row.price.text = run.Market[i].Price.ToString();
                    trend = run.Market[i].Trend;
                    break;
                case TradeRun.RowKind.Tip:
                    row.held.text = "";
                    row.price.text = run.TipPrice.ToString();
                    break;
                case TradeRun.RowKind.Raid:
                    // the bandits under Held, the pay under Price
                    row.held.text = run.OpenRaid != null ? run.OpenRaid.BanditCount.ToString() : "";
                    row.held.EnableInClassList("unit-held--none", false);
                    row.price.text = run.OpenRaid != null ? "+" + run.OpenRaid.pay : "";
                    break;
                default:
                    var group = run.EnemyAt(i);
                    row.held.text = group.scouted ? group.count.ToString() : "?";
                    row.held.EnableInClassList("unit-held--none", !group.scouted);
                    row.price.text = group.scouted ? "" : run.ScoutPrice.ToString();
                    break;
            }
            row.trend.style.visibility = trend == 0 ? Visibility.Hidden : Visibility.Visible;
            row.trend.Shape = trend < 0 ? HudShape.Kind.TriangleDown : HudShape.Kind.TriangleUp;
            row.trend.MarkDirtyRepaint();
        }
        foreach (var extra in listExtras)
            extra.style.display = mapFocus ? DisplayStyle.None : DisplayStyle.Flex;
        var selectedRow = rows[run.Selected];
        description.text = selectedRow.kind switch
        {
            TradeRun.RowKind.Unit => run.Unit(run.Selected).description,
            TradeRun.RowKind.Tip => tipDescription,
            TradeRun.RowKind.Raid => run.OpenRaid == null ? ""
                : string.Format(raidDescription, run.OpenRaid.BanditCount, run.OpenRaid.place, run.OpenRaid.pay),
            _ => enemyDescription,
        };
        description.style.display = mapFocus ? DisplayStyle.None : DisplayStyle.Flex;
        if (unitRows.IndexOf(description) != unitRows.IndexOf(selectedRow.root) + 1)
            description.PlaceInFront(selectedRow.root);

        ShowKey(buyKey, run.buy, run.sell);
        ShowKey(sellKey, run.sell, run.buy);
        ShowTradeBar();
        ShowNotices();
    }

    void ShowKey(Key key, TradeRun.Order order, TradeRun.Order other)
    {
        bool idle = order.state == TradeRun.OrderState.Idle;
        key.root.EnableInClassList("key--idle", idle);
        key.root.EnableInClassList("key--active", !idle);
        key.root.EnableInClassList("key--holding", order.state == TradeRun.OrderState.Holding);
        key.root.EnableInClassList("key--other", idle && other.state != TradeRun.OrderState.Idle);
        key.root.EnableInClassList("key--has-count", !idle);
        // tips, raids and scouting can only be bought
        bool noSelling = order.side == TradeRun.Side.Sell && idle && run.KindOf(run.Selected) != TradeRun.RowKind.Unit;
        key.root.EnableInClassList("key--closed", run.AtBattle || noSelling);
        // the count, and what it comes to: the price only moves once the trade goes through, so the climbing total
        // is where a long hold shows it's getting dearer
        // in soldiers: a buy brings a squad of them
        int soldiers = run.OrderSoldiers(order);
        string count = order.side == TradeRun.Side.Buy ? "+" + soldiers : "−" + soldiers;
        int total = idle ? 0 : run.OrderTotal(order);
        key.count.text = total > 0 ? count + " · " + total : count;
        key.arrow.MarkDirtyRepaint();
    }

    void OnTraded(TradeRun.Side side, int unit, int count)
    {
        if (count <= 0)
            return;
        tradedSide = side;
        tradedAt = Time.unscaledTime;
    }

    /// <summary>
    /// One bar for both keys: a buy fills it from the left to the line in the middle, a sell from the right, through
    /// the countdown after the key is let go. It shows while a key is in use, and stays full for a moment after a trade.
    /// </summary>
    void ShowTradeBar()
    {
        var active = run.Active;
        float buyFill = 0f, sellFill = 0f;
        bool justTraded = Time.unscaledTime - tradedAt < tradeHold;
        if (active != null)
        {
            float progress = run.Progress(active);
            if (active.side == TradeRun.Side.Buy) buyFill = progress;
            else sellFill = progress;
        }
        else if (justTraded)
        {
            if (tradedSide == TradeRun.Side.Buy) buyFill = 1f;
            else sellFill = 1f;
        }
        tradeFillBuy.style.width = Length.Percent(buyFill * 50f);
        tradeFillSell.style.width = Length.Percent(sellFill * 50f);
        trade.EnableInClassList("trade--shown", (active != null || justTraded) && !run.AtBattle);
    }

    void ShowNotices()
    {
        var cam = mapCamera != null ? mapCamera : Camera.main;
        var panel = hud.panel;
        if (cam == null || panel == null)
            return;
        placed.Clear();
        for (int i = shown.Count - 1; i >= 0; i--)
        {
            var notice = shown[i];
            // a notice stays up while its event is still to come, fades out as the event's effect wears off and goes
            // once less than noticeFade.x of it is left (old ones piled up while they barely moved prices); a raid's
            // stays while the raid is in the list
            float effectLeft = notice.marketEvent != null && run.HasHappened(notice.marketEvent)
                ? run.Market.Remaining(notice.marketEvent) : 1f;
            bool over = notice.marketEvent != null
                ? effectLeft <= noticeFade.x
                : notice.raid != run.OpenRaid;
            notice.root.style.opacity = Mathf.InverseLerp(noticeFade.x, noticeFade.y, effectLeft);
            if (over || notice.place == null)
            {
                notice.root.RemoveFromHierarchy();
                notice.ping.RemoveFromHierarchy();
                shown.RemoveAt(i);
                continue;
            }
            var position = notice.place.transform.position;
            // a place behind the camera projects flipped, onto the top of the screen; on this map that's south of the
            // view, so it belongs on the bottom edge (it once pinged "further up" with the map scrolled to the top)
            bool behind = cam.WorldToViewportPoint(position).z <= 0f;
            if (behind)
                position.z = cam.transform.position.z + 1f;
            var point = RuntimePanelUtils.CameraTransformWorldToPanel(panel, position, cam);
            bool above = !behind && point.y < noticeBand.x, below = behind || point.y > noticeBand.y;
            bool inView = !above && !below;
            notice.root.style.display = inView ? DisplayStyle.Flex : DisplayStyle.None;
            notice.ping.style.display = inView ? DisplayStyle.None : DisplayStyle.Flex;
            if (inView)
            {
                // the marker's centre sits on the place; the panel opens to the right, or to the left near the list
                float width = notice.panel.resolvedStyle.width;
                bool left = point.x + 17f + width > noticeRightLimit;
                notice.root.style.flexDirection = left ? FlexDirection.RowReverse : FlexDirection.Row;
                var marker = notice.root[0];
                marker.style.marginRight = left ? 0 : 11;
                marker.style.marginLeft = left ? 11 : 0;
                float height = notice.root.resolvedStyle.height;
                if (float.IsNaN(height)) height = 38f;
                // a notice that would cover one already placed (same town, or towns close together on screen)
                // moves down below it; its marker stays on the town
                float x = left ? point.x + 6f - (width + 23f) : point.x - 6f;
                float y = point.y - height * 0.5f;
                float shift = 0f;
                for (int pass = 0; pass < 8; pass++)
                {
                    bool moved = false;
                    var rect = new Rect(x, y + shift, width + 23f, height);
                    foreach (var other in placed)
                        if (other.Overlaps(rect))
                        {
                            shift = other.yMax + 4f - y;
                            moved = true;
                            break;
                        }
                    if (!moved) break;
                }
                placed.Add(new Rect(x, y + shift, width + 23f, height));
                notice.root.style.left = x;
                notice.root.style.top = y + shift;
                marker.style.translate = new Translate(0, -shift);
            }
            else
            {
                notice.chevron.Shape = above ? HudShape.Kind.ChevronUp : HudShape.Kind.ChevronDown;
                notice.ping.style.left = Mathf.Clamp(point.x, 40f, noticeRightLimit - 30f);
                notice.ping.style.top = above ? noticeBand.x - 8f : noticeBand.y + 6f;
            }
        }
    }
}
