using System;
using System.Collections.Generic;
using System.IO;
using Runix.Core.Models.Chart;
using SkiaSharp;

namespace Runix.Infrastructure.Chart;

/// <summary>
/// Pacote de resultado de renderização contendo o bitmap SkiaSharp, dimensões e lista de geometrias para hit-testing.
/// </summary>
public class ChartRenderResult : IDisposable
{
    public SKBitmap Bitmap { get; set; }
    public List<ChartHitTestResult> HitTestResults { get; set; } = new();
    public int Width { get; set; }
    public int Height { get; set; }
    public float Scale { get; set; } = 1.0f;

    public ChartRenderResult(SKBitmap bitmap, List<ChartHitTestResult> hitTestResults, int width, int height, float scale = 1.0f)
    {
        Bitmap = bitmap;
        HitTestResults = hitTestResults;
        Width = width;
        Height = height;
        Scale = scale;
    }

    public byte[] ToPngByteArray()
    {
        using var image = SKImage.FromBitmap(Bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void SavePng(string filePath)
    {
        var bytes = ToPngByteArray();
        File.WriteAllBytes(filePath, bytes);
    }

    public void Dispose()
    {
        Bitmap?.Dispose();
    }
}
