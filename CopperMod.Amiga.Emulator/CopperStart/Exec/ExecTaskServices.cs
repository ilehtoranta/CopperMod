using Copper68k;

namespace CopperMod.Amiga;

// Kept nested so that the service can use the controller's boot-only adapters
// without exposing those adapters as a public library API.
internal sealed partial class AmigaBootController
{
    private sealed class ExecTaskServices
    {
        private readonly AmigaBootController _owner;
        public ExecTaskServices(AmigaBootController owner) => _owner = owner;

        public uint AddTask(M68kCpuState state)
        {
            var task = state.A[1]; var initial = state.A[2]; var final = state.A[3];
            if (!_owner.IsValidExecNode(task) || initial == 0 || !_owner._machine.Bus.IsCpuPhysicalAddressMapped(initial, 2, AmigaBusAccessKind.CpuInstructionFetch)) return 0;
            var context = new M68kCpuState(); context.CopyFrom(state); context.ProgramCounter = initial;
            context.A[7] = _owner._machine.Bus.ReadLong(task + TaskStackPointerOffset);
            if (context.A[7] >= 4 && final != 0) { context.A[7] -= 4; _owner._machine.Bus.WriteLong(context.A[7], final); }
            _owner._taskScheduler.Register(task, context);
            _owner.MoveTaskToList(task, _owner.GetActiveExecBase() + ExecTaskReadyOffset, state);
            _owner._taskScheduler.RequestDispatch();
            return task;
        }

        public uint RemTask(M68kCpuState state)
        {
            var task = state.A[1] != 0 ? state.A[1] : _owner.GetCurrentTaskAddress();
            if (_owner.IsValidExecNode(task)) _owner.RemoveExecNode(task);
            _owner._taskScheduler.Remove(task);
            return 0;
        }

        public uint SetTaskPri(M68kCpuState state)
        {
            var task = state.A[1]; if (!_owner.IsValidExecNode(task)) return 0;
            var old = unchecked((sbyte)_owner._machine.Bus.ReadByte(task + 9));
            _owner._machine.Bus.WriteByte(task + 9, unchecked((byte)(sbyte)state.D[0]), state.Cycles);
            _owner._taskScheduler.RequestDispatch();
            return unchecked((uint)old);
        }

        public uint Disable(M68kCpuState state) => ChangeNesting(state, 0x126, 1);
        public uint Enable(M68kCpuState state) => ChangeNesting(state, 0x126, -1);
        public uint Forbid(M68kCpuState state) => ChangeNesting(state, 0x127, 1);
        public uint Permit(M68kCpuState state) => ChangeNesting(state, 0x127, -1);

        private uint ChangeNesting(M68kCpuState state, int offset, int delta)
        {
            var address = _owner.GetActiveExecBase() + (uint)offset;
            var value = _owner._machine.Bus.ReadByte(address);
            if (delta < 0 && value == 0) return 0;
            _owner._machine.Bus.WriteByte(address, unchecked((byte)(value + delta)), state.Cycles);
            return 0;
        }
    }
}
