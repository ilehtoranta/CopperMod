namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Exec vector offsets owned by CopperStart; keep vector knowledge out of boot lifecycle code.</summary>
internal static class ExecLvos
{
    // These are genuine, private Exec scheduler vectors.  CopperStart never
    // overlays them in ROM mode: blocking host calls enter KS task switching so
    // it remains the authority for every task, including tasks created before
    // the overlay became active.
    public const int Schedule = -42, Switch = -54;
    public const int Disable = -120, Enable = -126, Forbid = -132, Permit = -138, SuperState = -150, UserState = -156;
    public const int InitStruct = -78, MakeLibrary = -84, MakeFunctions = -90, FindResident = -96, InitResident = -102;
    public const int AddIntServer = -168, RemIntServer = -174, Allocate = -186, Deallocate = -192;
    public const int AllocEntry = -222, FreeEntry = -228, Insert = -234, AddHead = -240, AddTail = -246, Remove = -252, RemHead = -258, RemTail = -264, Enqueue = -270, FindName = -276;
    public const int AddTask = -282, RemTask = -288, FindTask = -294, SetTaskPri = -300, SetSignal = -306, SetExcept = -312, Wait = -318, Signal = -324, AllocSignal = -330, FreeSignal = -336;
    public const int AllocTrap = -342, FreeTrap = -348, AddPort = -354, RemPort = -360, PutMsg = -366, GetMsg = -372, ReplyMsg = -378, WaitPort = -384, FindPort = -390;
    public const int RawDoFmt = -522, TypeOfMem = -534, CopyMem = -624, CopyMemQuick = -630, AllocVec = -684, FreeVec = -690, CreatePool = -696, DeletePool = -702, AllocPooled = -708, FreePooled = -714;
    public const int InitSemaphore = -558, ObtainSemaphore = -564, ReleaseSemaphore = -570, AttemptSemaphore = -576;
    public const int ObtainSemaphoreShared = -678, AttemptSemaphoreShared = -720;
    // Emulator-private compatibility calls.  They deliberately live below the
    // synthetic catch-all table and are registered directly, never ROM-overlaid.
    public const int PrivateWait = -1206, PrivateReschedule = -1212;
}
