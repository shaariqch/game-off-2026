using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shows a <see cref="TradeRun"/> over the map: cash and the timeline along the top, the unit list on the right,
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

    [Header("Notices")]
    [Tooltip("Notices above this or below the second (reference pixels from the top) show as a ping on that edge instead.")]
    [SerializeField] Vector2 noticeBand = new Vector2(96f, 650f);
    [Tooltip("A notice opens to the left of its place when it would reach past this x (the unit list's left edge).")]
    [SerializeField] float noticeRightLimit = 990f;
    [SerializeField] Color noticeColour = new Color(0.498f, 0.769f, 0.788f);
    [SerializeField] Color invasionColour = new Color(0.878f, 0.396f, 0.31f);

    [Header("Trade bar")]
    [Tooltip("Seconds the bar stays full after a trade goes through, while its word shows, before it fades.")]
    [SerializeField, Min(0f)] float tradeHold = 0.9f;

    sealed class Row
    {
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
        public MarketEvent marketEvent;
        public MapPlace place;
        public VisualElement root, panel, ping;
        public HudShape chevron;
    }

    VisualElement hud, units, unitRows, eventDots, now, trackBehind, timeline, notices;
    VisualElement trade, tradeFillBuy, tradeFillSell;
    // the last trade that went through, so the bar can stay full for a moment after it
    TradeRun.Side tradedSide;
    float tradedAt = float.NegativeInfinity;
    Label cashValue, battleLabel, description;
    readonly List<Row> rows = new List<Row>();
    readonly List<(MarketEvent marketEvent, VisualElement dot)> dots = new List<(MarketEvent, VisualElement)>();
    readonly List<Notice> shown = new List<Notice>();
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
        description = hud.Q<Label>("description");
        hud.Q("cash-icon").style.backgroundImage = cashIcon;
        hud.Q("battle-icon").style.backgroundImage = battleIcon;
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
        run.Traded += OnTraded;
        tradedAt = float.NegativeInfinity;
        built = true;
    }

    void OnDisable()
    {
        if (run != null)
        {
            run.EventStarted -= OnEventStarted;
            run.Traded -= OnTraded;
        }
        built = false;
    }

    void BuildRows()
    {
        unitRows.Clear();
        rows.Clear();
        for (int i = 0; i < run.UnitCount; i++)
        {
            var type = run.Unit(i);
            var row = new Row { root = new VisualElement() };
            row.root.AddToClassList("unit-row");
            var name = new VisualElement();
            name.AddToClassList("col-name");
            var icon = new VisualElement();
            icon.AddToClassList("icon");
            icon.AddToClassList("unit-icon");
            icon.style.backgroundImage = type.icon;
            name.Add(icon);
            var label = new Label(type.name);
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
            row.trend.style.width = 12;
            row.trend.style.height = 12;
            row.trend.style.marginLeft = 5;
            price.Add(row.trend);
            row.root.Add(price);

            unitRows.Add(row.root);
            rows.Add(row);
        }
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
            eventDots.Add(dot);
            dots.Add((marketEvent, dot));
        }
        builtForBattle = run.Battle;
    }

    void OnEventStarted(MarketEvent marketEvent)
    {
        var place = MapPlace.Find(marketEvent.place);
        if (place == null)
        {
            Debug.LogWarning($"No MapPlace called {marketEvent.place} for the event \"{marketEvent.text}\".", this);
            return;
        }
        var colour = marketEvent.category == MarketEvent.Category.Invasion ? invasionColour : noticeColour;
        var notice = new Notice { marketEvent = marketEvent, place = place, root = new VisualElement() };
        notice.root.AddToClassList("notice");
        notice.root.pickingMode = PickingMode.Ignore;
        var marker = new VisualElement();
        marker.AddToClassList("notice-marker");
        notice.root.Add(marker);
        notice.panel = new VisualElement();
        notice.panel.AddToClassList("panel");
        notice.panel.AddToClassList("notice-panel");
        var dot = new VisualElement();
        dot.AddToClassList("notice-dot");
        dot.style.backgroundColor = colour;
        notice.panel.Add(dot);
        var text = new Label(marketEvent.text);
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

        notices.Add(notice.root);
        notices.Add(notice.ping);
        shown.Add(notice);
    }

    void LateUpdate()
    {
        if (!built || run == null || run.Market == null)
            return;
        if (builtForBattle != run.Battle || dots.Count != run.Events.Count)
            BuildDots();

        hud.EnableInClassList("hud--battle", run.AtBattle);
        cashValue.text = run.Cash.ToString();

        float progress = Mathf.Clamp01(run.PhaseTime / run.PhaseLength);
        now.style.left = Length.Percent(progress * 100f);
        trackBehind.style.width = Length.Percent(progress * 100f);
        foreach (var (marketEvent, dot) in dots)
        {
            bool happened = run.HasHappened(marketEvent);
            dot.style.display = marketEvent.known || happened ? DisplayStyle.Flex : DisplayStyle.None;
            dot.EnableInClassList("dot--past", happened);
        }
        battleLabel.text = $"Battle {run.Battle} of {run.BattlesPerRun}";
        timeline.EnableInClassList("timeline--battle", run.AtBattle);

        units.EnableInClassList("units--map-focus", run.CurrentFocus == TradeRun.Focus.Map);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var listing = run.Market[i];
            int held = run.Held(i);
            row.root.EnableInClassList("unit-row--selected", i == run.Selected);
            row.held.text = held.ToString();
            row.held.EnableInClassList("unit-held--none", held == 0);
            row.price.text = listing.Price.ToString();
            row.trend.style.visibility = listing.Trend == 0 ? Visibility.Hidden : Visibility.Visible;
            row.trend.Shape = listing.Trend < 0 ? HudShape.Kind.TriangleDown : HudShape.Kind.TriangleUp;
            row.trend.MarkDirtyRepaint();
        }
        description.text = run.Unit(run.Selected).description;

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
        key.root.EnableInClassList("key--closed", run.AtBattle);
        key.count.text = order.side == TradeRun.Side.Buy ? "+" + order.count : "−" + order.count;
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
        for (int i = shown.Count - 1; i >= 0; i--)
        {
            var notice = shown[i];
            // a notice stays up while its event still moves the market
            if (run.Market.Strength(notice.marketEvent) <= 0f || notice.place == null)
            {
                notice.root.RemoveFromHierarchy();
                notice.ping.RemoveFromHierarchy();
                shown.RemoveAt(i);
                continue;
            }
            var point = RuntimePanelUtils.CameraTransformWorldToPanel(panel, notice.place.transform.position, cam);
            bool above = point.y < noticeBand.x, below = point.y > noticeBand.y;
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
                notice.root.style.left = left ? point.x + 6f - (width + 23f) : point.x - 6f;
                notice.root.style.top = point.y - (float.IsNaN(height) ? 19f : height * 0.5f);
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
