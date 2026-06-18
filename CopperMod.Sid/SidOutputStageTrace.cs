namespace CopperMod.Sid
{
    internal sealed class SidOutputStageTrace
    {
        private double _d418GenericStepImpulse;
        private double _d418MatrixImpulse;
        private double _filterModeImpulse;
        private double _voiceSignal;
        private double _volumeOffset;
        private double _volumeTransientTarget;
        private double _volumeTransientCurrent;
        private double _preSoftClipSample;
        private double _postSoftClipSample;
        private double _analogMixedVoltage;
        private double _analogOutputVoltage;
        private double _analogLowPassVoltage;
        private double _finalSample;
        private int _cycles;
        private int _d418Writes;

        public bool IsCapturing { get; private set; }

        public void BeginFrame()
        {
            _d418GenericStepImpulse = 0.0;
            _d418MatrixImpulse = 0.0;
            _filterModeImpulse = 0.0;
            _voiceSignal = 0.0;
            _volumeOffset = 0.0;
            _volumeTransientTarget = 0.0;
            _volumeTransientCurrent = 0.0;
            _preSoftClipSample = 0.0;
            _postSoftClipSample = 0.0;
            _analogMixedVoltage = 0.0;
            _analogOutputVoltage = 0.0;
            _analogLowPassVoltage = 0.0;
            _finalSample = 0.0;
            _cycles = 0;
            _d418Writes = 0;
            IsCapturing = true;
        }

        public SidOutputStageFrame EndFrame()
        {
            IsCapturing = false;
            if (_cycles <= 0)
            {
                return default;
            }

            var scale = 1.0 / _cycles;
            return new SidOutputStageFrame(
                _cycles,
                _d418Writes,
                _d418GenericStepImpulse,
                _d418MatrixImpulse,
                _filterModeImpulse,
                _voiceSignal * scale,
                _volumeOffset * scale,
                _volumeTransientTarget * scale,
                _volumeTransientCurrent * scale,
                _preSoftClipSample * scale,
                _postSoftClipSample * scale,
                _analogMixedVoltage * scale,
                _analogOutputVoltage * scale,
                _analogLowPassVoltage * scale,
                _finalSample * scale);
        }

        internal void AddD418Write(
            double genericStepImpulse,
            double matrixImpulse,
            double filterModeImpulse)
        {
            if (!IsCapturing)
            {
                return;
            }

            _d418Writes++;
            _d418GenericStepImpulse += genericStepImpulse;
            _d418MatrixImpulse += matrixImpulse;
            _filterModeImpulse += filterModeImpulse;
        }

        internal void AddOutputCycle(in SidOutputStageCycle cycle)
        {
            if (!IsCapturing)
            {
                return;
            }

            _cycles++;
            _voiceSignal += cycle.VoiceSignal;
            _volumeOffset += cycle.VolumeOffset;
            _volumeTransientTarget += cycle.VolumeTransientTarget;
            _volumeTransientCurrent += cycle.VolumeTransientCurrent;
            _preSoftClipSample += cycle.PreSoftClipSample;
            _postSoftClipSample += cycle.PostSoftClipSample;
            _analogMixedVoltage += cycle.AnalogMixedVoltage;
            _analogOutputVoltage += cycle.AnalogOutputVoltage;
            _analogLowPassVoltage += cycle.AnalogLowPassVoltage;
            _finalSample += cycle.FinalSample;
        }
    }

    internal readonly struct SidOutputStageCycle
    {
        public SidOutputStageCycle(
            double voiceSignal,
            double volumeOffset,
            double volumeTransientTarget,
            double volumeTransientCurrent,
            double preSoftClipSample,
            double postSoftClipSample,
            double analogMixedVoltage,
            double analogOutputVoltage,
            double analogLowPassVoltage,
            double finalSample)
        {
            VoiceSignal = voiceSignal;
            VolumeOffset = volumeOffset;
            VolumeTransientTarget = volumeTransientTarget;
            VolumeTransientCurrent = volumeTransientCurrent;
            PreSoftClipSample = preSoftClipSample;
            PostSoftClipSample = postSoftClipSample;
            AnalogMixedVoltage = analogMixedVoltage;
            AnalogOutputVoltage = analogOutputVoltage;
            AnalogLowPassVoltage = analogLowPassVoltage;
            FinalSample = finalSample;
        }

        public double VoiceSignal { get; }

        public double VolumeOffset { get; }

        public double VolumeTransientTarget { get; }

        public double VolumeTransientCurrent { get; }

        public double PreSoftClipSample { get; }

        public double PostSoftClipSample { get; }

        public double AnalogMixedVoltage { get; }

        public double AnalogOutputVoltage { get; }

        public double AnalogLowPassVoltage { get; }

        public double FinalSample { get; }
    }

    internal readonly struct SidOutputStageFrame
    {
        public SidOutputStageFrame(
            int cycles,
            int d418Writes,
            double d418GenericStepImpulse,
            double d418MatrixImpulse,
            double filterModeImpulse,
            double voiceSignal,
            double volumeOffset,
            double volumeTransientTarget,
            double volumeTransientCurrent,
            double preSoftClipSample,
            double postSoftClipSample,
            double analogMixedVoltage,
            double analogOutputVoltage,
            double analogLowPassVoltage,
            double finalSample)
        {
            Cycles = cycles;
            D418Writes = d418Writes;
            D418GenericStepImpulse = d418GenericStepImpulse;
            D418MatrixImpulse = d418MatrixImpulse;
            FilterModeImpulse = filterModeImpulse;
            VoiceSignal = voiceSignal;
            VolumeOffset = volumeOffset;
            VolumeTransientTarget = volumeTransientTarget;
            VolumeTransientCurrent = volumeTransientCurrent;
            PreSoftClipSample = preSoftClipSample;
            PostSoftClipSample = postSoftClipSample;
            AnalogMixedVoltage = analogMixedVoltage;
            AnalogOutputVoltage = analogOutputVoltage;
            AnalogLowPassVoltage = analogLowPassVoltage;
            FinalSample = finalSample;
        }

        public int Cycles { get; }

        public int D418Writes { get; }

        public double D418GenericStepImpulse { get; }

        public double D418MatrixImpulse { get; }

        public double FilterModeImpulse { get; }

        public double VoiceSignal { get; }

        public double VolumeOffset { get; }

        public double VolumeTransientTarget { get; }

        public double VolumeTransientCurrent { get; }

        public double PreSoftClipSample { get; }

        public double PostSoftClipSample { get; }

        public double AnalogMixedVoltage { get; }

        public double AnalogOutputVoltage { get; }

        public double AnalogLowPassVoltage { get; }

        public double FinalSample { get; }
    }
}
