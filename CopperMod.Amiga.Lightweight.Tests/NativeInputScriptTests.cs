using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class NativeInputScriptTests
{
    [Fact]
    public void MediaIsReadBeforeExecutionAndAppliedAtItsScriptedFrame()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                [{"frame":2,"adfPath":"second.adf"},{"frame":4,"ejectAdf":true}]
                """);
            var reads = 0;
            var script = new NativeInputScript(path, mediaPath =>
            {
                reads++;
                Assert.Equal(Path.Combine(Path.GetDirectoryName(path)!, "second.adf"), mediaPath);
                return new byte[80 * 2 * 11 * 512];
            });
            Assert.Equal(1, reads);
            using var machine = new LightweightA500Machine();
            script.Apply(machine, 0);
            Assert.False(machine.IsAdfMounted);
            script.Apply(machine, 2);
            Assert.True(machine.IsAdfMounted);
            script.Apply(machine, 3);
            Assert.True(machine.IsAdfMounted);
            script.Apply(machine, 4);
            Assert.False(machine.IsAdfMounted);
            Assert.Equal(1, reads);
            Assert.False(script.ChangesMediaBetween(0, 2));
            Assert.True(script.ChangesMediaBetween(2, 3));
            Assert.True(script.ChangesMediaBetween(4, 5));
            Assert.False(script.ChangesMediaBetween(5, 10));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("[{\"frame\":0,\"adfPath\":\"disk.adf\",\"ejectAdf\":true}]")]
    [InlineData("[{\"frame\":0,\"adfPath\":\" \"}]")]
    [InlineData("[{\"frame\":2},{\"frame\":1}]")]
    public void InvalidActionsAreRejectedBeforeMediaReads(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<ArgumentException>(() => new NativeInputScript(path,
                _ => throw new InvalidOperationException("Must not read media")));
        }
        finally { File.Delete(path); }
    }
}
