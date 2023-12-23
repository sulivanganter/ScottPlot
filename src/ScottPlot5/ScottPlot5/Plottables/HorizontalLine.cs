﻿namespace ScottPlot.Plottables;

/// <summary>
/// A line at a defined Y position that spans the entire horizontal space of the data area
/// </summary>
public class HorizontalLine : AxisLine
{
    public double Y
    {
        get => Position;
        set => Position = value;
    }

    public HorizontalLine()
    {
        Label.Rotation = -90;
        Label.Alignment = Alignment.LowerCenter;
        Label.FontSize = 14;
        Label.Bold = true;
        Label.ForeColor = Colors.White;
        Label.Padding = 5;
    }

    public override AxisLimits GetAxisLimits()
    {
        return AxisLimits.VerticalOnly(Y, Y);
    }

    public override void Render(RenderPack rp)
    {
        if (!IsVisible)
            return;

        // determine location
        float x1 = rp.DataRect.Left;
        float x2 = rp.DataRect.Right;
        float y = Axes.GetPixelY(Y);
        if (!rp.DataRect.ContainsY(y))
            return;

        // draw line inside the data area
        using SKPaint paint = new();
        LineStyle.ApplyToPaint(paint);
        rp.Canvas.DrawLine(x1, y, x2, y, paint);
    }

    public override void RenderLast(RenderPack rp)
    {
        // determine location
        float y = Axes.GetPixelY(Y);
        if (!rp.DataRect.ContainsY(y))
            return;

        // draw label outside the data area
        rp.DisableClipping();
        using SKPaint paint = new();
        Label.BackColor = LineStyle.Color;
        Label.Render(rp.Canvas, rp.DataRect.Left - Label.Padding, y, paint);
    }
}
