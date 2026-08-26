using CopperMod.Abstractions;

namespace CopperMod.Ahx.Tests;

public sealed class AhxFormatTests
{
    [Fact] public void RecognizesAhxSignatures(){var f=new AhxFormat();Assert.True(f.CanLoad(Header(0)));Assert.True(f.CanLoad(Header(1)));Assert.False(f.CanLoad("HVL"u8));}
    [Fact] public void ValidModuleRequiresConfiguredReferencePlayer(){var ex=Assert.Throws<ModuleLoadException>(()=>new AhxFormat(()=>throw new ModuleLoadException("reference player not configured")).Load(Minimal()));Assert.Contains("not configured",ex.Message);}
    [Fact] public void RejectsReferencePlayerWithWrongIdentity(){var ex=Assert.Throws<ModuleLoadException>(()=>AhxReferencePlayerBinary.Validate(new byte[40]));Assert.Contains("expected 11580",ex.Message);}
    [Fact] public void UsesSharedBinaryReplayerDirectoryConvention(){Assert.Equal(Path.Combine("Replayers","AHX","AHX-Replayer000.BIN"),AhxReferencePlayerBinary.DefaultRelativePath);}
    [Fact] public void FindsAndVerifiesRepositoryBinaryWhenLocallyInstalled(){var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));var path=Path.Combine(root,AhxReferencePlayerBinary.DefaultRelativePath);if(!File.Exists(path))return;var player=AhxReferencePlayerBinary.LoadConfigured();Assert.Equal(AhxReferencePlayerBinary.ExpectedSize,player.Length);Assert.Equal(AhxReferencePlayerBinary.ExpectedSha256,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(player)));}
    [Fact] public void RejectsTruncatedRecognizedFile(){var x=Header(1);Assert.Throws<ModuleLoadException>(()=>new AhxFormat().Load(x));}
    private static byte[] Header(int v)=>[(byte)'T',(byte)'H',(byte)'X',(byte)v,0,0,0,1,0,0,1,0,0,0];
    private static byte[] Minimal()=>[(byte)'T',(byte)'H',(byte)'X',1,0,25,0x80,1,0,0,1,0,0,0, 0,0,0,0,0,0,0,0, 0];
}
