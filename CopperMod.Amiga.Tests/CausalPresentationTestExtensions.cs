using System;

namespace CopperMod.Amiga.Tests;

internal static class CausalPresentationTestExtensions
{
    public static void RenderFrame(this OcsDisplay display, uint[] pixels)
        => RenderFrame(display, pixels, 0, long.MaxValue, enableDefaultDma: true);

    public static void RenderFrame(
        this OcsDisplay display,
        uint[] pixels,
        long frameStartCycle,
        long frameStopCycle,
        bool enableDefaultDma = true)
    {
        if (!display.AttachedBus.LiveAgnusDmaEnabled &&
            display.AttachedBus.Agnus.CurrentCycle == 0)
        {
            display.AttachedBus.EnableLiveAgnusDma();
        }

        // Old renderer tests configured a static display without explicitly
        // enabling DMA. Preserve that setup convenience only at the causal frame
        // origin; tests that have advanced hardware must bind before doing so.
        // Sprite DMA is deliberately excluded because a zero sprite pointer is a
        // real DMA terminator and would disarm manually latched sprite data.
        if (enableDefaultDma && display.AttachedBus.Agnus.CurrentCycle == 0)
        {
            display.AttachedBus.WriteWord(0x00DFF096, 0x8380);
        }

        display.BeginPresentationFrame(
            new PresentationFrameTarget(pixels),
            frameStartCycle,
            frameStopCycle);
        try
        {
            display.CompletePresentationFrame(frameStopCycle);
        }
        catch
        {
            display.AbortPresentationFrame();
            throw;
        }
    }
}
