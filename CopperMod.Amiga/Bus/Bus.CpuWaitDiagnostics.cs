/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Bus
    {
        private bool _deferredCpuWaitDiagnosticsEnabled;

        private long _deferredCpuWaitWindowAttempts;
        private long _deferredCpuWaitWindowEligible;
        private long _deferredCpuWaitWindowTotalCycles;
        private long _deferredCpuWaitWindowMaxCycles;
        private long _deferredCpuWaitWindowInstructionFetch;
        private long _deferredCpuWaitWindowDataRead;
        private long _deferredCpuWaitWindowDataWrite;
        private long _deferredCpuWaitWindowCustom;
        private long _deferredCpuWaitWindowChipRam;
        private long _deferredCpuWaitWindowExpansionRam;
        private long _deferredCpuWaitWindowRealTimeClock;
        private long _deferredCpuWaitWindowCustomRegisters;
        private long _deferredCpuWaitWindowByte;
        private long _deferredCpuWaitWindowWord;
        private long _deferredCpuWaitWindowLong;
        private long _deferredCpuWaitWindowRead;
        private long _deferredCpuWaitWindowWrite;
        private long _deferredCpuWaitWindowSingleSlot;
        private long _deferredCpuWaitWindowLongSlot;
        private long _deferredCpuWaitWindowFastPathAttempts;
        private long _deferredCpuWaitWindowFastPathUsed;
        private long _deferredCpuWaitWindowFastPathRejectedUnsupported;
        private long _deferredCpuWaitWindowFastPathRejectedDynamicDma;
        private long _deferredCpuWaitWindowFastPathRejectedUnstable;
        private long _deferredCpuWaitWindowFastPathAdvancedCycles;
        private long _deferredCpuWaitWindowFastPathMaxAdvancedCycles;
        private long _deferredCpuWaitFixedImageProductionAttempts;
        private long _deferredCpuWaitFixedImageProductionUsed;
        private long _deferredCpuWaitFixedImageProductionPreGrantDrainsSkipped;
        private long _deferredCpuWaitFixedImageProductionPostGrantCatchups;
        private long _deferredCpuWaitFixedImageProductionPredictedWaitCycles;
        private long _deferredCpuWaitFixedImageProductionFallbackUnsupported;
        private long _deferredCpuWaitFixedImageProductionFallbackDynamicDma;
        private long _deferredCpuWaitFixedImageProductionFallbackFrame;
        private long _deferredCpuWaitFixedImageProductionFallbackCopper;
        private long _deferredCpuWaitFixedImageProductionFallbackPendingWrite;
        private long _deferredCpuWaitFixedImageProductionFallbackRasterlinePlan;
        private long _deferredCpuWaitFixedImageProductionFallbackSpriteState;
        private long _deferredCpuWaitFixedImageProductionFallbackUnstable;



        internal long DeferredCpuWaitWindowAttempts => _deferredCpuWaitWindowAttempts;

        internal long DeferredCpuWaitWindowEligible => _deferredCpuWaitWindowEligible;

        internal long DeferredCpuWaitWindowTotalCycles => _deferredCpuWaitWindowTotalCycles;

        internal long DeferredCpuWaitWindowMaxCycles => _deferredCpuWaitWindowMaxCycles;

        internal long DeferredCpuWaitWindowInstructionFetch => _deferredCpuWaitWindowInstructionFetch;

        internal long DeferredCpuWaitWindowDataRead => _deferredCpuWaitWindowDataRead;

        internal long DeferredCpuWaitWindowDataWrite => _deferredCpuWaitWindowDataWrite;

        internal long DeferredCpuWaitWindowCustom => _deferredCpuWaitWindowCustom;

        internal long DeferredCpuWaitWindowChipRam => _deferredCpuWaitWindowChipRam;

        internal long DeferredCpuWaitWindowExpansionRam => _deferredCpuWaitWindowExpansionRam;

        internal long DeferredCpuWaitWindowRealTimeClock => _deferredCpuWaitWindowRealTimeClock;

        internal long DeferredCpuWaitWindowCustomRegisters => _deferredCpuWaitWindowCustomRegisters;

        internal long DeferredCpuWaitWindowByte => _deferredCpuWaitWindowByte;

        internal long DeferredCpuWaitWindowWord => _deferredCpuWaitWindowWord;

        internal long DeferredCpuWaitWindowLong => _deferredCpuWaitWindowLong;

        internal long DeferredCpuWaitWindowRead => _deferredCpuWaitWindowRead;

        internal long DeferredCpuWaitWindowWrite => _deferredCpuWaitWindowWrite;

        internal long DeferredCpuWaitWindowSingleSlot => _deferredCpuWaitWindowSingleSlot;

        internal long DeferredCpuWaitWindowLongSlot => _deferredCpuWaitWindowLongSlot;

        internal long DeferredCpuWaitWindowFastPathAttempts => _deferredCpuWaitWindowFastPathAttempts;

        internal long DeferredCpuWaitWindowFastPathUsed => _deferredCpuWaitWindowFastPathUsed;

        internal long DeferredCpuWaitWindowFastPathRejectedUnsupported => _deferredCpuWaitWindowFastPathRejectedUnsupported;

        internal long DeferredCpuWaitWindowFastPathRejectedDynamicDma => _deferredCpuWaitWindowFastPathRejectedDynamicDma;

        internal long DeferredCpuWaitWindowFastPathRejectedUnstable => _deferredCpuWaitWindowFastPathRejectedUnstable;

        internal long DeferredCpuWaitWindowFastPathAdvancedCycles => _deferredCpuWaitWindowFastPathAdvancedCycles;

        internal long DeferredCpuWaitWindowFastPathMaxAdvancedCycles => _deferredCpuWaitWindowFastPathMaxAdvancedCycles;

        internal bool DeferredCpuWaitFixedImageProductionDisabled
            => !DeferredCpuWaitFastPathEnabled;
        internal long DeferredCpuWaitFixedImageProductionAttempts => _deferredCpuWaitFixedImageProductionAttempts;
        internal long DeferredCpuWaitFixedImageProductionUsed => _deferredCpuWaitFixedImageProductionUsed;
        internal long DeferredCpuWaitFixedImageProductionPreGrantDrainsSkipped => _deferredCpuWaitFixedImageProductionPreGrantDrainsSkipped;
        internal long DeferredCpuWaitFixedImageProductionPostGrantCatchups => _deferredCpuWaitFixedImageProductionPostGrantCatchups;
        internal long DeferredCpuWaitFixedImageProductionPredictedWaitCycles => _deferredCpuWaitFixedImageProductionPredictedWaitCycles;
        internal long DeferredCpuWaitFixedImageProductionFallbackUnsupported => _deferredCpuWaitFixedImageProductionFallbackUnsupported;
        internal long DeferredCpuWaitFixedImageProductionFallbackDynamicDma => _deferredCpuWaitFixedImageProductionFallbackDynamicDma;
        internal long DeferredCpuWaitFixedImageProductionFallbackFrame => _deferredCpuWaitFixedImageProductionFallbackFrame;
        internal long DeferredCpuWaitFixedImageProductionFallbackCopper => _deferredCpuWaitFixedImageProductionFallbackCopper;
        internal long DeferredCpuWaitFixedImageProductionFallbackPendingWrite => _deferredCpuWaitFixedImageProductionFallbackPendingWrite;
        internal long DeferredCpuWaitFixedImageProductionFallbackRasterlinePlan => _deferredCpuWaitFixedImageProductionFallbackRasterlinePlan;
        internal long DeferredCpuWaitFixedImageProductionFallbackSpriteState => _deferredCpuWaitFixedImageProductionFallbackSpriteState;
        internal long DeferredCpuWaitFixedImageProductionFallbackUnstable => _deferredCpuWaitFixedImageProductionFallbackUnstable;



        private void ResetDeferredCpuWaitDiagnostics()
        {
            _deferredCpuWaitWindowAttempts = 0;
            _deferredCpuWaitWindowEligible = 0;
            _deferredCpuWaitWindowTotalCycles = 0;
            _deferredCpuWaitWindowMaxCycles = 0;
            _deferredCpuWaitWindowInstructionFetch = 0;
            _deferredCpuWaitWindowDataRead = 0;
            _deferredCpuWaitWindowDataWrite = 0;
            _deferredCpuWaitWindowCustom = 0;
            _deferredCpuWaitWindowChipRam = 0;
            _deferredCpuWaitWindowExpansionRam = 0;
            _deferredCpuWaitWindowRealTimeClock = 0;
            _deferredCpuWaitWindowCustomRegisters = 0;
            _deferredCpuWaitWindowByte = 0;
            _deferredCpuWaitWindowWord = 0;
            _deferredCpuWaitWindowLong = 0;
            _deferredCpuWaitWindowRead = 0;
            _deferredCpuWaitWindowWrite = 0;
            _deferredCpuWaitWindowSingleSlot = 0;
            _deferredCpuWaitWindowLongSlot = 0;
            _deferredCpuWaitWindowFastPathAttempts = 0;
            _deferredCpuWaitWindowFastPathUsed = 0;
            _deferredCpuWaitWindowFastPathRejectedUnsupported = 0;
            _deferredCpuWaitWindowFastPathRejectedDynamicDma = 0;
            _deferredCpuWaitWindowFastPathRejectedUnstable = 0;
            _deferredCpuWaitWindowFastPathAdvancedCycles = 0;
            _deferredCpuWaitWindowFastPathMaxAdvancedCycles = 0;
            _deferredCpuWaitFixedImageProductionAttempts = 0;
            _deferredCpuWaitFixedImageProductionUsed = 0;
            _deferredCpuWaitFixedImageProductionPreGrantDrainsSkipped = 0;
            _deferredCpuWaitFixedImageProductionPostGrantCatchups = 0;
            _deferredCpuWaitFixedImageProductionPredictedWaitCycles = 0;
            _deferredCpuWaitFixedImageProductionFallbackUnsupported = 0;
            _deferredCpuWaitFixedImageProductionFallbackDynamicDma = 0;
            _deferredCpuWaitFixedImageProductionFallbackFrame = 0;
            _deferredCpuWaitFixedImageProductionFallbackCopper = 0;
            _deferredCpuWaitFixedImageProductionFallbackPendingWrite = 0;
            _deferredCpuWaitFixedImageProductionFallbackRasterlinePlan = 0;
            _deferredCpuWaitFixedImageProductionFallbackSpriteState = 0;
            _deferredCpuWaitFixedImageProductionFallbackUnstable = 0;
            Display.ResetCpuWaitFixedSlotImageDiagnostics();
        }

        internal void RecordProductionCpuWaitFixedSlotImageAttempt()
        {
            _deferredCpuWaitFixedImageProductionAttempts++;
        }

        internal void RecordProductionCpuWaitFixedSlotImageUse(long requestedCycle, long grantedCycle)
        {
            _deferredCpuWaitFixedImageProductionUsed++;
            _deferredCpuWaitFixedImageProductionPreGrantDrainsSkipped++;
            if (grantedCycle > requestedCycle)
            {
                _deferredCpuWaitFixedImageProductionPredictedWaitCycles += grantedCycle - requestedCycle;
            }
        }

        internal void RecordProductionCpuWaitFixedSlotImagePostGrantCatchup()
            => _deferredCpuWaitFixedImageProductionPostGrantCatchups++;

        internal void RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback fallback)
        {
            switch (fallback)
            {
                case CpuWaitFixedImageProductionFallback.DynamicDma: _deferredCpuWaitFixedImageProductionFallbackDynamicDma++; break;
                case CpuWaitFixedImageProductionFallback.Frame: _deferredCpuWaitFixedImageProductionFallbackFrame++; break;
                case CpuWaitFixedImageProductionFallback.Copper: _deferredCpuWaitFixedImageProductionFallbackCopper++; break;
                case CpuWaitFixedImageProductionFallback.PendingWrite: _deferredCpuWaitFixedImageProductionFallbackPendingWrite++; break;
                case CpuWaitFixedImageProductionFallback.RasterlinePlan: _deferredCpuWaitFixedImageProductionFallbackRasterlinePlan++; break;
                case CpuWaitFixedImageProductionFallback.SpriteState: _deferredCpuWaitFixedImageProductionFallbackSpriteState++; break;
                case CpuWaitFixedImageProductionFallback.Unstable: _deferredCpuWaitFixedImageProductionFallbackUnstable++; break;
                case CpuWaitFixedImageProductionFallback.Unsupported:
                default: _deferredCpuWaitFixedImageProductionFallbackUnsupported++; break;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RecordDeferredCpuWaitFastPathUse(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long grantRequestCycle,
            bool isWrite,
            long grantedCycle,
            long secondWordCycle,
            long completedCycle)
        {
            _deferredCpuWaitWindowFastPathUsed++;
            var advancedCycles = grantedCycle - grantRequestCycle;
            if (advancedCycles > 0)
            {
                _deferredCpuWaitWindowFastPathAdvancedCycles += advancedCycles;
                if (advancedCycles > _deferredCpuWaitWindowFastPathMaxAdvancedCycles)
                {
                    _deferredCpuWaitWindowFastPathMaxAdvancedCycles = advancedCycles;
                }
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordDeferredCpuWaitWindow(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            AmigaBusAccessSize size,
            bool isWrite,
            long requestedCycle,
            long grantedCycle)
        {
            if (!_deferredCpuWaitDiagnosticsEnabled)
            {
                return;
            }

            switch (target)
            {
                case AmigaBusAccessTarget.ChipRam:
                    _deferredCpuWaitWindowChipRam++;
                    break;
                case AmigaBusAccessTarget.ExpansionRam:
                    _deferredCpuWaitWindowExpansionRam++;
                    break;
                case AmigaBusAccessTarget.RealTimeClock:
                    _deferredCpuWaitWindowRealTimeClock++;
                    break;
                case AmigaBusAccessTarget.CustomRegisters:
                    _deferredCpuWaitWindowCustom++;
                    _deferredCpuWaitWindowCustomRegisters++;
                    break;
                default:
                    return;
            }

            _deferredCpuWaitWindowAttempts++;
            var waitCycles = grantedCycle - requestedCycle;
            if (waitCycles > 0)
            {
                _deferredCpuWaitWindowEligible++;
                _deferredCpuWaitWindowTotalCycles += waitCycles;
                if (waitCycles > _deferredCpuWaitWindowMaxCycles)
                {
                    _deferredCpuWaitWindowMaxCycles = waitCycles;
                }
            }

            switch (kind)
            {
                case AmigaBusAccessKind.CpuInstructionFetch:
                    _deferredCpuWaitWindowInstructionFetch++;
                    break;
                case AmigaBusAccessKind.CpuDataRead:
                    _deferredCpuWaitWindowDataRead++;
                    break;
                case AmigaBusAccessKind.CpuDataWrite:
                    _deferredCpuWaitWindowDataWrite++;
                    break;
            }

            switch (size)
            {
                case AmigaBusAccessSize.Byte:
                    _deferredCpuWaitWindowByte++;
                    _deferredCpuWaitWindowSingleSlot++;
                    break;
                case AmigaBusAccessSize.Word:
                    _deferredCpuWaitWindowWord++;
                    _deferredCpuWaitWindowSingleSlot++;
                    break;
                case AmigaBusAccessSize.Long:
                    _deferredCpuWaitWindowLong++;
                    _deferredCpuWaitWindowLongSlot++;
                    break;
            }

            if (isWrite)
            {
                _deferredCpuWaitWindowWrite++;
            }
            else
            {
                _deferredCpuWaitWindowRead++;
            }
        }

        private bool ShouldCollectDeferredCpuWaitDiagnostics
            => _deferredCpuWaitDiagnosticsEnabled;





    }
}
