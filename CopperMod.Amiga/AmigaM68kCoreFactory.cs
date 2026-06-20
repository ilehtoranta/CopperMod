namespace CopperMod.Amiga
{
    internal sealed class AmigaM68kCoreFactory : IM68kCoreFactory
    {
        public static AmigaM68kCoreFactory Default { get; } = new AmigaM68kCoreFactory();

        public IM68kCore Create(M68kBackendKind backend, IM68kBus bus)
        {
            return backend switch
            {
                M68kBackendKind.JitM68000 => new M68kJitCore(bus),
                M68kBackendKind.JitM68040 => M68kJitCore.CreateM68040(bus),
                _ => M68kCoreFactory.Default.Create(backend, bus)
            };
        }
    }
}
