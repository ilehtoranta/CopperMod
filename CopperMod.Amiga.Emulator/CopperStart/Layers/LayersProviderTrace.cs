using System;
using System.Collections.Generic;
using System.Linq;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.CopperStart.Layers;

internal sealed partial class LayersHostServices
{
    private List<string>? _providerOperationsForTest;
    private int _lastRasterProviderGuardCountForTest;
    private ushort _lastRasterProviderPrimaryGuardDepthForTest;
    private ushort _lastRasterProviderSecondaryGuardDepthForTest;
    private ulong _rasterProviderExecutionMaskForTest;
    private int _compatibilityRasterDispatchCountForTest;

    internal const ulong CompleteRasterProviderExecutionMaskForTest =
        (1UL << 29) - 1;

    internal void EnableProviderOperationTraceForTest()
        => _providerOperationsForTest ??= new List<string>();

    internal int CaptureProviderOperationTraceForTest()
        => _providerOperationsForTest?.Count ?? 0;

    internal int LastRasterProviderGuardCountForTest
        => _lastRasterProviderGuardCountForTest;
    internal ushort LastRasterProviderPrimaryGuardDepthForTest
        => _lastRasterProviderPrimaryGuardDepthForTest;
    internal ushort LastRasterProviderSecondaryGuardDepthForTest
        => _lastRasterProviderSecondaryGuardDepthForTest;
    internal ulong RasterProviderExecutionMaskForTest
        => _rasterProviderExecutionMaskForTest;
    internal int CompatibilityRasterDispatchCountForTest
        => _compatibilityRasterDispatchCountForTest;

    internal string DescribeProviderOperationTraceForTest(int start)
    {
        var operations = _providerOperationsForTest;
        if (operations is null || start < 0 || start >= operations.Count)
            return "none";
        var slice = operations.Skip(start).ToArray();
        return string.Join(',', slice
            .GroupBy(static operation => operation, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => $"{group.Key}x{group.Count()}"));
    }

    internal string DescribeProviderOperationOrderForTest(int start)
    {
        var operations = _providerOperationsForTest;
        if (operations is null || start < 0 || start >= operations.Count)
            return "none";
        return string.Join('>', operations.Skip(start));
    }

    private void TraceProviderOperationForTest(string operation)
        => _providerOperationsForTest?.Add(operation);

    private void TraceLayeredRasterProviderOperationForTest(short graphicsLvo)
    {
        _rasterProviderExecutionMaskForTest |= RasterProviderExecutionBit(
            (GraphicsLvo)graphicsLvo);
        var operations = _providerOperationsForTest;
        if (operations is not null)
            operations.Add($"LayeredRaster({graphicsLvo})");
    }

    private void TraceCompatibilityRasterDispatchForTest()
        => _compatibilityRasterDispatchCountForTest++;

    private void ResetRasterProviderEvidenceForTest()
    {
        _lastRasterProviderGuardCountForTest = 0;
        _lastRasterProviderPrimaryGuardDepthForTest = 0;
        _lastRasterProviderSecondaryGuardDepthForTest = 0;
        _rasterProviderExecutionMaskForTest = 0;
        _compatibilityRasterDispatchCountForTest = 0;
    }

    private static ulong RasterProviderExecutionBit(GraphicsLvo lvo)
        => lvo switch
        {
            GraphicsLvo.BltTemplate => 1UL << 0,
            GraphicsLvo.ClearEOL => 1UL << 1,
            GraphicsLvo.ClearScreen => 1UL << 2,
            GraphicsLvo.Text => 1UL << 3,
            GraphicsLvo.DrawEllipse => 1UL << 4,
            GraphicsLvo.AreaEllipse => 1UL << 5,
            GraphicsLvo.SetRast => 1UL << 6,
            GraphicsLvo.Draw => 1UL << 7,
            GraphicsLvo.AreaMove => 1UL << 8,
            GraphicsLvo.AreaDraw => 1UL << 9,
            GraphicsLvo.AreaEnd => 1UL << 10,
            GraphicsLvo.RectFill => 1UL << 11,
            GraphicsLvo.BltPattern => 1UL << 12,
            GraphicsLvo.ReadPixel => 1UL << 13,
            GraphicsLvo.WritePixel => 1UL << 14,
            GraphicsLvo.Flood => 1UL << 15,
            GraphicsLvo.PolyDraw => 1UL << 16,
            GraphicsLvo.ScrollRaster => 1UL << 17,
            GraphicsLvo.ClipBlit => 1UL << 18,
            GraphicsLvo.BltBitMapRastPort => 1UL << 19,
            GraphicsLvo.BltMaskBitMapRastPort => 1UL << 20,
            GraphicsLvo.ReadPixelLine8 => 1UL << 21,
            GraphicsLvo.WritePixelLine8 => 1UL << 22,
            GraphicsLvo.ReadPixelArray8 => 1UL << 23,
            GraphicsLvo.WritePixelArray8 => 1UL << 24,
            GraphicsLvo.EraseRect => 1UL << 25,
            GraphicsLvo.ScrollRasterBF => 1UL << 26,
            GraphicsLvo.GetRPAttrsA => 1UL << 27,
            GraphicsLvo.WriteChunkyPixels => 1UL << 28,
            _ => 0
        };
}
