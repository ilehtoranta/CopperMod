namespace CopperMod.Sid
{
    internal sealed class C64SpriteRegisterSnapshot
    {
        public C64SpriteRegisterSnapshot(byte[] registers, byte[] pointers, int vicBankBase)
        {
            Registers = registers;
            Pointers = pointers;
            VicBankBase = vicBankBase;
        }

        public byte[] Registers { get; }

        public byte[] Pointers { get; }

        public int VicBankBase { get; }
    }
}
