using Avalonia.Threading;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class MainWindowPresentationTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 64)]
    public void ContinuousPresentationYieldsToInput(bool legacyRenderPriority, int expectedFramesBeforeInput)
    {
        // Real dispatcher ordering with a bounded, continuously replenished frame
        // queue. No window, native input injection, sleeps or emulator are needed.
        var dispatcher = Dispatcher.CurrentDispatcher;
        var priority = legacyRenderPriority ? DispatcherPriority.Render : MainWindow.FramePresentationPriority;
        var presented = 0;
        var framesBeforeInput = -1;
        void Present()
        {
            presented++;
            if (presented == 1)
                dispatcher.Post(() => framesBeforeInput = presented, DispatcherPriority.Input);
            if (presented < 64)
                dispatcher.Post(Present, priority);
        }

        dispatcher.Post(Present, priority);
        dispatcher.RunJobs();

        Assert.Equal(64, presented); // Yielding must not discard the remaining work.
        Assert.Equal(expectedFramesBeforeInput, framesBeforeInput);
    }
}
