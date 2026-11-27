using UnityEngine;
using UnityEngine.UIElements;

/// <summary>A small drawn shape in the HUD's text colour: the Buy and Sell arrows, price trends and map pings.</summary>
/// <remarks>Drawn rather than typed, since the HUD's fonts don't all have arrow and triangle glyphs.</remarks>
public class HudShape : VisualElement
{
    public enum Kind { ArrowUp, ArrowDown, TriangleUp, TriangleDown, ChevronUp, ChevronDown }

    Kind kind;
    Color colour;
    bool fixedColour;

    public HudShape(Kind kind)
    {
        this.kind = kind;
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
        RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
    }

    public Kind Shape
    {
        get => kind;
        set
        {
            if (kind == value) return;
            kind = value;
            MarkDirtyRepaint();
        }
    }

    /// <summary>Draws in this colour instead of the inherited text colour.</summary>
    public void SetColour(Color value)
    {
        colour = value;
        fixedColour = true;
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        var rect = contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;
        var painter = context.painter2D;
        var paint = fixedColour ? colour : resolvedStyle.color;
        float s = Mathf.Min(rect.width, rect.height) / 24f;
        var origin = new Vector2(rect.x + (rect.width - 24f * s) * 0.5f, rect.y + (rect.height - 24f * s) * 0.5f);
        bool down = kind is Kind.ArrowDown or Kind.TriangleDown or Kind.ChevronDown;
        // shapes are drawn pointing up in a 24 by 24 box, then flipped for down
        Vector2 P(float x, float y) => origin + new Vector2(x, down ? 24f - y : y) * s;

        painter.BeginPath();
        switch (kind)
        {
            case Kind.ArrowUp:
            case Kind.ArrowDown:
                painter.fillColor = paint;
                painter.MoveTo(P(12, 3));
                painter.LineTo(P(21, 13));
                painter.LineTo(P(16, 13));
                painter.LineTo(P(16, 21));
                painter.LineTo(P(8, 21));
                painter.LineTo(P(8, 13));
                painter.LineTo(P(3, 13));
                painter.ClosePath();
                painter.Fill();
                break;
            case Kind.TriangleUp:
            case Kind.TriangleDown:
                painter.fillColor = paint;
                painter.MoveTo(P(12, 4));
                painter.LineTo(P(22, 20));
                painter.LineTo(P(2, 20));
                painter.ClosePath();
                painter.Fill();
                break;
            default:
                painter.strokeColor = paint;
                painter.lineWidth = 2.6f * s;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                painter.MoveTo(P(5, 15));
                painter.LineTo(P(12, 8));
                painter.LineTo(P(19, 15));
                painter.Stroke();
                break;
        }
    }
}
