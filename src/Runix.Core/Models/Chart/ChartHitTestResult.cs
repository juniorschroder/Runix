using System;

namespace Runix.Core.Models.Chart;

public enum ChartHitShape
{
    Rectangle,
    Circle,
    DonutSlice
}

/// <summary>
/// Metadados geométricos gerados na renderização para detecção de hover e exibição de tooltip interativo.
/// </summary>
public class ChartHitTestResult
{
    public ChartDataPoint DataPoint { get; set; } = new();
    public ChartHitShape ShapeType { get; set; } = ChartHitShape.Rectangle;

    // Rectangle
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    // Circle & Donut
    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public float Radius { get; set; }
    public float InnerRadius { get; set; }
    public float OuterRadius { get; set; }
    public float StartAngle { get; set; }
    public float SweepAngle { get; set; }

    public bool Contains(float px, float py)
    {
        switch (ShapeType)
        {
            case ChartHitShape.Rectangle:
                return px >= X && px <= X + Width && py >= Y && py <= Y + Height;

            case ChartHitShape.Circle:
                var dx = px - CenterX;
                var dy = py - CenterY;
                return (dx * dx + dy * dy) <= (Radius * Radius);

            case ChartHitShape.DonutSlice:
                var distSq = (px - CenterX) * (px - CenterX) + (py - CenterY) * (py - CenterY);
                var innerSq = InnerRadius * InnerRadius;
                var outerSq = OuterRadius * OuterRadius;

                if (distSq < innerSq || distSq > outerSq)
                    return false;

                // Angle calculation in degrees [0, 360)
                var angleRad = Math.Atan2(py - CenterY, px - CenterX);
                var angleDeg = (float)(angleRad * 180.0 / Math.PI);
                if (angleDeg < 0) angleDeg += 360f;

                var normStart = (StartAngle % 360f + 360f) % 360f;
                var normEnd = normStart + SweepAngle;

                if (normEnd <= 360f)
                {
                    return angleDeg >= normStart && angleDeg <= normEnd;
                }
                else
                {
                    return angleDeg >= normStart || angleDeg <= (normEnd - 360f);
                }

            default:
                return false;
        }
    }
}
