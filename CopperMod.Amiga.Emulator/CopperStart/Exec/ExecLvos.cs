namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Exec vector offsets owned by CopperStart; keep vector knowledge out of boot lifecycle code.</summary>
internal static class ExecLvos
{
    public const int Disable = -120, Enable = -126, Forbid = -132, Permit = -138, SuperState = -150, UserState = -156;
    public const int AddIntServer = -168, RemIntServer = -174, Allocate = -186, Deallocate = -192;
    public const int AllocEntry = -222, FreeEntry = -228, Insert = -234, AddHead = -240, AddTail = -246, Remove = -252, RemHead = -258, RemTail = -264, Enqueue = -270, FindName = -276;
    public const int AddTask = -282, RemTask = -288, FindTask = -294, SetTaskPri = -300, SetSignal = -306, Wait = -318, Signal = -324, AllocSignal = -330, FreeSignal = -336;
    public const int AllocTrap = -342, FreeTrap = -348, AddPort = -354, RemPort = -360, PutMsg = -366, GetMsg = -372, ReplyMsg = -378, WaitPort = -384, FindPort = -390;
    public const int RawDoFmt = -522, TypeOfMem = -534, CopyMem = -624, CopyMemQuick = -630, AllocVec = -684, FreeVec = -690, CreatePool = -696, DeletePool = -702, AllocPooled = -708, FreePooled = -714;
}
