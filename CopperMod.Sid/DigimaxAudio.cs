using System;
using System.Collections.Generic;

namespace CopperMod.Sid
{
    internal readonly struct DigimaxWrite
    {
        public DigimaxWrite(long cycle, byte register, byte value)
        {
            Cycle = cycle;
            Register = register;
            Value = value;
        }

        public long Cycle { get; }

        public byte Register { get; }

        public byte Value { get; }
    }

    internal sealed class DigimaxAudio
    {
        private readonly byte[] _channels = { 0x80, 0x80, 0x80, 0x80 };
        private readonly List<DigimaxWrite> _writes = new List<DigimaxWrite>(4096);

        public IReadOnlyList<DigimaxWrite> Writes => _writes;

        public void Reset()
        {
            Array.Fill(_channels, (byte)0x80);
            _writes.Clear();
        }

        public bool TryWrite(ushort address, byte value, long cycle)
        {
            if (address < 0xDF00 || address > 0xDF03)
            {
                return false;
            }

            var register = (byte)(address & 0x03);
            _channels[register] = value;
            _writes.Add(new DigimaxWrite(cycle, register, value));
            return true;
        }

        public float RenderSample()
        {
            var mixed = 0.0;
            for (var i = 0; i < _channels.Length; i++)
            {
                mixed += (_channels[i] - 128.0) / 128.0;
            }

            return (float)Math.Clamp(mixed * 0.25, -0.999, 0.999);
        }
    }
}
