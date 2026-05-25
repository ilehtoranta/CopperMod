using System;

namespace AmigaTracker.Sid
{
    internal sealed class SidChip
    {
        private readonly SidVoice[] _voices = { new SidVoice(), new SidVoice(), new SidVoice() };
        private readonly byte[] _registers = new byte[32];
        private double _filterLow;
        private double _filterBand;

        public SidChip(SidChipModel model, ushort baseAddress)
        {
            Model = model == SidChipModel.Mos8580 ? SidChipModel.Mos8580 : SidChipModel.Mos6581;
            BaseAddress = baseAddress;
        }

        public SidChipModel Model { get; }

        public ushort BaseAddress { get; }

        public byte[] Registers => _registers;

        public void Reset()
        {
            Array.Clear(_registers);
            foreach (var voice in _voices)
            {
                voice.Reset();
            }

            _filterLow = 0;
            _filterBand = 0;
        }

        public void Write(byte register, byte value)
        {
            register = (byte)(register & 0x1F);
            _registers[register] = value;
            if (register < 7)
            {
                _voices[0].Write(register, value);
            }
            else if (register < 14)
            {
                _voices[1].Write(register - 7, value);
            }
            else if (register < 21)
            {
                _voices[2].Write(register - 14, value);
            }
        }

        public double Render(double cycles)
        {
            var voice1 = _voices[0].Render(cycles, _voices[2], Model);
            var voice2 = _voices[1].Render(cycles, _voices[0], Model);
            var voice3 = _voices[2].Render(cycles, _voices[1], Model);
            var mixer = _registers[0x18];
            var volume = (mixer & 0x0F) / 15.0;
            var filterRouting = _registers[0x17] & 0x07;
            var direct = 0.0;
            var filtered = 0.0;
            var voicesInOutput = 0;
            RouteVoice(voice1, filtered: (filterRouting & 0x01) != 0, ref direct, ref filtered);
            RouteVoice(voice2, filtered: (filterRouting & 0x02) != 0, ref direct, ref filtered);
            voicesInOutput += 2;
            if ((mixer & 0x80) == 0)
            {
                RouteVoice(voice3, filtered: (filterRouting & 0x04) != 0, ref direct, ref filtered);
                voicesInOutput++;
            }

            var input = (direct + ApplyFilter(filtered)) / Math.Max(1, voicesInOutput);

            var dc = Model == SidChipModel.Mos8580 ? 0.0 : 0.08;
            return (input * Math.Max(0.02, volume)) + ((volume - 0.5) * dc);
        }

        private static void RouteVoice(double voice, bool filtered, ref double direct, ref double filterInput)
        {
            if (filtered)
            {
                filterInput += voice;
            }
            else
            {
                direct += voice;
            }
        }

        private double ApplyFilter(double input)
        {
            var cutoff = ((_registers[0x16] << 3) | (_registers[0x15] & 0x07)) / 2047.0;
            var resonance = (_registers[0x17] >> 4) / 15.0;
            var mode = _registers[0x18] & 0x70;
            if (mode == 0)
            {
                return input;
            }

            var frequency = Model == SidChipModel.Mos8580
                ? 0.02 + (cutoff * 0.55)
                : 0.01 + (cutoff * cutoff * 0.45);
            var q = 0.05 + (resonance * 0.18);
            _filterLow += frequency * _filterBand;
            var high = input - _filterLow - (q * _filterBand);
            _filterBand += frequency * high;
            var low = _filterLow;
            var band = _filterBand;
            var output = 0.0;
            if ((mode & 0x10) != 0)
            {
                output += low;
            }

            if ((mode & 0x20) != 0)
            {
                output += band;
            }

            if ((mode & 0x40) != 0)
            {
                output += high;
            }

            return output;
        }
    }
}
