using Copper68k;

namespace CopperMod.Amiga;

/// <summary>ROM Exec message-port LVOs backed exclusively by guest links.</summary>
internal sealed partial class AmigaBootController
{
    private sealed class ExecPortServices
    {
        private readonly AmigaBootController _owner;
        public ExecPortServices(AmigaBootController owner) => _owner = owner;

        public uint AddPort(M68kCpuState state)
        {
            var port = state.A[1];
            if (!_owner.IsValidExecPort(port)) return 0;
            _owner.EnsureExecList(port + MsgPortMsgListOffset);
            var ports = _owner.GetActiveExecBase() + ExecPortListOffset;
            _owner.EnsureExecList(ports);
            if (!_owner.ContainsExecNode(ports, port)) _owner.AddTailExecList(ports, port);
            return 0;
        }

        public uint RemPort(M68kCpuState state)
        {
            var port = state.A[1];
            var ports = _owner.GetActiveExecBase() + ExecPortListOffset;
            if (_owner.IsValidExecPort(port) && _owner.ContainsExecNode(ports, port)) _owner.RemoveExecNode(port);
            return 0;
        }

        public uint PutMsg(M68kCpuState state)
        {
            var port = state.A[0]; var message = state.A[1];
            if (!_owner.IsValidExecPort(port) || !_owner.IsValidExecMessage(message) ||
                !_owner.ContainsExecNode(_owner.GetActiveExecBase() + ExecPortListOffset, port)) return 0;
            _owner.EnsureExecList(port + MsgPortMsgListOffset);
            _owner.AddTailExecList(port + MsgPortMsgListOffset, message);
            var bit = _owner._machine.Bus.ReadByte(port + MsgPortSigBitOffset);
            if (bit < 32) _owner.SignalExecTask(_owner._machine.Bus.ReadLong(port + MsgPortSigTaskOffset), 1u << bit);
            return 0;
        }

        public uint GetMsg(M68kCpuState state)
        {
            var message = _owner.RemoveFirstPortMessage(state.A[0]);
            _owner.ClearPortSignalIfQueueEmpty(state.A[0]);
            return message;
        }

        public uint ReplyMsg(M68kCpuState state)
        {
            var message = state.A[1];
            if (!_owner.IsValidExecMessage(message)) return 0;
            var replyPort = _owner._machine.Bus.ReadLong(message + MessageReplyPortOffset);
            if (replyPort != 0) { state.A[0] = replyPort; PutMsg(state); }
            return 0;
        }

        public uint FindPort(M68kCpuState state)
            => _owner.FindNameInExecList(_owner.GetActiveExecBase() + ExecPortListOffset, state.A[1]);

        public uint WaitPort(M68kCpuState state)
        {
            var port = state.A[0]; var list = port + MsgPortMsgListOffset;
            if (_owner.IsValidExecPort(port) && _owner._machine.Bus.ReadLong(list) != list + 4) return _owner._machine.Bus.ReadLong(list);
            if (_owner.IsValidExecPort(port))
            {
                var bit = _owner._machine.Bus.ReadByte(port + MsgPortSigBitOffset);
                if (bit < 32) _owner.WriteTaskSignals(_owner.GetCurrentTaskAddress(), TaskSigWaitOffset, 1u << bit);
            }
            return 0;
        }
    }
}
