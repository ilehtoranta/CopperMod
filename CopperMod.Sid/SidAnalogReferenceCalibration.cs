namespace CopperMod.Sid
{
    internal sealed class SidAnalogReferenceCalibration
    {
        public double? Mos6581TransientLimitScale { get; init; }

        public double? Mos8580TransientLimitScale { get; init; }

        public double? Mos6581TransientAttackScale { get; init; }

        public double? Mos8580TransientAttackScale { get; init; }

        public double? Mos6581TransientDecayScale { get; init; }

        public double? Mos8580TransientDecayScale { get; init; }

        public double? Mos6581OutputLowPassCutoffHz { get; init; }

        public double? Mos8580OutputLowPassCutoffHz { get; init; }

        public double? Mos6581TransitionScale { get; init; }

        public double? Mos8580TransitionScale { get; init; }
    }
}
