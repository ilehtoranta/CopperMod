namespace CopperMod.Amiga
{
    internal sealed class AmigaM68kCoreFactory : IM68kBackendCoreFactory
    {
        public static AmigaM68kCoreFactory Default { get; } = new AmigaM68kCoreFactory();

        public IM68kCore Create(M68kCpuModel model, IM68kBus bus)
            => M68kCoreFactory.Default.Create(model, bus);

        public IM68kCore Create(M68kBackendKind backend, IM68kBus bus)
            => M68kCoreFactory.Default.Create(backend, bus);
    }
}
