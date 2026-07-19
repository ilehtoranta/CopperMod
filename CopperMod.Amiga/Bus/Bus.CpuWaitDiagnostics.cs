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
        private const int DeferredCpuWaitSlotShadowMaxSamples = 20000;
        private const int DeferredCpuWaitSlotShadowLiveMaxSamples = 256;
        private const int DeferredCpuWaitFixedImageMaxSamples = 20000;
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
        private long _deferredCpuWaitSlotShadowAttempts;
        private long _deferredCpuWaitSlotShadowMatches;
        private long _deferredCpuWaitSlotShadowMismatches;
        private long _deferredCpuWaitSlotShadowUnsupported;
        private long _deferredCpuWaitSlotShadowGrantMismatches;
        private long _deferredCpuWaitSlotShadowCompletionMismatches;
        private long _deferredCpuWaitSlotShadowSlotOwnerMismatches;
        private long _deferredCpuWaitSlotShadowBlitterStateMismatches;
        private long _deferredCpuWaitSlotShadowPaulaMismatches;
        private long _deferredCpuWaitSlotShadowDiskMismatches;
        private long _deferredCpuWaitSlotShadowDisplayMismatches;
        private long _deferredCpuWaitSlotShadowCopperMismatches;
        private long _deferredCpuWaitSlotShadowLiveAttempts;
        private long _deferredCpuWaitSlotShadowLiveSupported;
        private long _deferredCpuWaitSlotShadowLiveUnsupported;
        private long _deferredCpuWaitSlotShadowLiveUnsupportedPendingWrite;
        private long _deferredCpuWaitSlotShadowLiveUnsupportedBitplaneWindow;
        private long _deferredCpuWaitSlotShadowLiveUnsupportedCopperWaitWindow;
        private long _deferredCpuWaitSlotShadowLiveUnsupportedRasterlinePlan;
        private long _deferredCpuWaitSlotShadowLiveUnsupportedCpuPredict;
        private long _deferredCpuWaitSlotShadowLiveUnsupportedUnstable;
        private long _deferredCpuWaitSlotShadowLiveUnsupportedScratchWrite;
        private long _deferredCpuWaitSlotShadowLiveUnsupportedLongWrite;
        private long _deferredCpuWaitSlotShadowLiveUnsupportedOther;
        private long _deferredCpuWaitSlotShadowLiveLongAccesses;
        private long _deferredCpuWaitSlotShadowLiveBitplaneFetches;
        private long _deferredCpuWaitSlotShadowLiveSpriteFetches;
        private long _deferredCpuWaitSlotShadowLiveCopperSteps;
        private long _deferredCpuWaitSlotShadowBlitterScratchAttempts;
        private long _deferredCpuWaitSlotShadowBlitterScratchSupported;
        private long _deferredCpuWaitSlotShadowBlitterScratchUnsupported;
        private long _deferredCpuWaitSlotShadowBlitterScratchMatches;
        private long _deferredCpuWaitSlotShadowBlitterScratchMismatches;
        private long _deferredCpuWaitSlotShadowBlitterScratchPartial;
        private long _deferredCpuWaitSlotShadowBlitterScratchMicroOps;
        private string _deferredCpuWaitSlotShadowFirstMismatch = string.Empty;
        private long _deferredCpuWaitFixedImageAttempts;
        private long _deferredCpuWaitFixedImageSupported;
        private long _deferredCpuWaitFixedImageMatches;
        private long _deferredCpuWaitFixedImageMismatches;
        private long _deferredCpuWaitFixedImageUnsupported;
        private string _deferredCpuWaitFixedImageFirstMismatch = string.Empty;
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
        private long _deferredCpuWaitFixedImageProductionVerificationMatches;
        private long _deferredCpuWaitFixedImageProductionVerificationMismatches;
        private string _deferredCpuWaitFixedImageProductionFirstMismatch = string.Empty;



        private enum CpuWaitSlotShadowReason : byte
        {
            Grant,
            Completion,
            SlotOwner,
            BlitterState,
            Paula,
            Disk,
            Display,
            Copper
        }

        private struct DeferredCpuWaitScratchAudit
        {
            public bool LiveAttempted;
            public bool LiveSupported;
            public OcsLiveDmaScratchResult Live;
            public bool BlitterAttempted;
            public bool BlitterSupported;
            public BlitterCpuWaitScratchResult Blitter;
            public bool FixedImageAttempted;
            public bool FixedImageSupported;
            public long FixedImageGrant;
            public long FixedImageCompletion;
            public CpuWaitFixedSlotImageUnsupported FixedImageUnsupported;
            public CpuWaitFixedSlotTimelineSignature FixedImageTimeline;

            public readonly bool HasSupportedScratch
                => (LiveAttempted && LiveSupported) ||
                    (BlitterAttempted && BlitterSupported) ||
                    (FixedImageAttempted && FixedImageSupported);
        }


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

        internal long DeferredCpuWaitSlotShadowAttempts => _deferredCpuWaitSlotShadowAttempts;

        internal long DeferredCpuWaitSlotShadowMatches => _deferredCpuWaitSlotShadowMatches;

        internal long DeferredCpuWaitSlotShadowMismatches => _deferredCpuWaitSlotShadowMismatches;

        internal long DeferredCpuWaitSlotShadowUnsupported => _deferredCpuWaitSlotShadowUnsupported;

        internal long DeferredCpuWaitSlotShadowGrantMismatches => _deferredCpuWaitSlotShadowGrantMismatches;

        internal long DeferredCpuWaitSlotShadowCompletionMismatches => _deferredCpuWaitSlotShadowCompletionMismatches;

        internal long DeferredCpuWaitSlotShadowSlotOwnerMismatches => _deferredCpuWaitSlotShadowSlotOwnerMismatches;

        internal long DeferredCpuWaitSlotShadowBlitterStateMismatches => _deferredCpuWaitSlotShadowBlitterStateMismatches;

        internal long DeferredCpuWaitSlotShadowPaulaMismatches => _deferredCpuWaitSlotShadowPaulaMismatches;

        internal long DeferredCpuWaitSlotShadowDiskMismatches => _deferredCpuWaitSlotShadowDiskMismatches;

        internal long DeferredCpuWaitSlotShadowDisplayMismatches => _deferredCpuWaitSlotShadowDisplayMismatches;

        internal long DeferredCpuWaitSlotShadowCopperMismatches => _deferredCpuWaitSlotShadowCopperMismatches;

        internal long DeferredCpuWaitSlotShadowLiveAttempts => _deferredCpuWaitSlotShadowLiveAttempts;

        internal long DeferredCpuWaitSlotShadowLiveSupported => _deferredCpuWaitSlotShadowLiveSupported;

        internal long DeferredCpuWaitSlotShadowLiveUnsupported => _deferredCpuWaitSlotShadowLiveUnsupported;

        internal long DeferredCpuWaitSlotShadowLiveUnsupportedPendingWrite => _deferredCpuWaitSlotShadowLiveUnsupportedPendingWrite;

        internal long DeferredCpuWaitSlotShadowLiveUnsupportedBitplaneWindow => _deferredCpuWaitSlotShadowLiveUnsupportedBitplaneWindow;

        internal long DeferredCpuWaitSlotShadowLiveUnsupportedCopperWaitWindow => _deferredCpuWaitSlotShadowLiveUnsupportedCopperWaitWindow;

        internal long DeferredCpuWaitSlotShadowLiveUnsupportedRasterlinePlan => _deferredCpuWaitSlotShadowLiveUnsupportedRasterlinePlan;

        internal long DeferredCpuWaitSlotShadowLiveUnsupportedCpuPredict => _deferredCpuWaitSlotShadowLiveUnsupportedCpuPredict;

        internal long DeferredCpuWaitSlotShadowLiveUnsupportedUnstable => _deferredCpuWaitSlotShadowLiveUnsupportedUnstable;

        internal long DeferredCpuWaitSlotShadowLiveUnsupportedScratchWrite => _deferredCpuWaitSlotShadowLiveUnsupportedScratchWrite;

        internal long DeferredCpuWaitSlotShadowLiveUnsupportedLongWrite => _deferredCpuWaitSlotShadowLiveUnsupportedLongWrite;

        internal long DeferredCpuWaitSlotShadowLiveUnsupportedOther => _deferredCpuWaitSlotShadowLiveUnsupportedOther;

        internal long DeferredCpuWaitSlotShadowLiveLongAccesses => _deferredCpuWaitSlotShadowLiveLongAccesses;

        internal long DeferredCpuWaitSlotShadowLiveBitplaneFetches => _deferredCpuWaitSlotShadowLiveBitplaneFetches;

        internal long DeferredCpuWaitSlotShadowLiveSpriteFetches => _deferredCpuWaitSlotShadowLiveSpriteFetches;

        internal long DeferredCpuWaitSlotShadowLiveCopperSteps => _deferredCpuWaitSlotShadowLiveCopperSteps;

        internal long DeferredCpuWaitSlotShadowBlitterScratchAttempts => _deferredCpuWaitSlotShadowBlitterScratchAttempts;

        internal long DeferredCpuWaitSlotShadowBlitterScratchSupported => _deferredCpuWaitSlotShadowBlitterScratchSupported;

        internal long DeferredCpuWaitSlotShadowBlitterScratchUnsupported => _deferredCpuWaitSlotShadowBlitterScratchUnsupported;

        internal long DeferredCpuWaitSlotShadowBlitterScratchMatches => _deferredCpuWaitSlotShadowBlitterScratchMatches;

        internal long DeferredCpuWaitSlotShadowBlitterScratchMismatches => _deferredCpuWaitSlotShadowBlitterScratchMismatches;

        internal long DeferredCpuWaitSlotShadowBlitterScratchPartial => _deferredCpuWaitSlotShadowBlitterScratchPartial;

        internal long DeferredCpuWaitSlotShadowBlitterScratchMicroOps => _deferredCpuWaitSlotShadowBlitterScratchMicroOps;

        internal string DeferredCpuWaitSlotShadowFirstMismatch => _deferredCpuWaitSlotShadowFirstMismatch;
        internal long DeferredCpuWaitFixedImageAttempts => _deferredCpuWaitFixedImageAttempts;
        internal long DeferredCpuWaitFixedImageSupported => _deferredCpuWaitFixedImageSupported;
        internal long DeferredCpuWaitFixedImageMatches => _deferredCpuWaitFixedImageMatches;
        internal long DeferredCpuWaitFixedImageMismatches => _deferredCpuWaitFixedImageMismatches;
        internal long DeferredCpuWaitFixedImageUnsupported => _deferredCpuWaitFixedImageUnsupported;
        internal string DeferredCpuWaitFixedImageFirstMismatch => _deferredCpuWaitFixedImageFirstMismatch;
        internal bool DeferredCpuWaitFixedImageProductionDisabled
            => !DeferredCpuWaitFastPathEnabled || _deferredCpuWaitFixedImageProductionDisabled;
        internal bool ShouldVerifyProductionCpuWaitFixedSlotImage
            => _deferredCpuBusBatchVerifyEnabled &&
                !DeferredCpuWaitFixedImageProductionDisabled;
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
        internal long DeferredCpuWaitFixedImageProductionVerificationMatches => _deferredCpuWaitFixedImageProductionVerificationMatches;
        internal long DeferredCpuWaitFixedImageProductionVerificationMismatches => _deferredCpuWaitFixedImageProductionVerificationMismatches;
        internal string DeferredCpuWaitFixedImageProductionFirstMismatch => _deferredCpuWaitFixedImageProductionFirstMismatch;



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
            _deferredCpuWaitSlotShadowAttempts = 0;
            _deferredCpuWaitSlotShadowMatches = 0;
            _deferredCpuWaitSlotShadowMismatches = 0;
            _deferredCpuWaitSlotShadowUnsupported = 0;
            _deferredCpuWaitSlotShadowGrantMismatches = 0;
            _deferredCpuWaitSlotShadowCompletionMismatches = 0;
            _deferredCpuWaitSlotShadowSlotOwnerMismatches = 0;
            _deferredCpuWaitSlotShadowBlitterStateMismatches = 0;
            _deferredCpuWaitSlotShadowPaulaMismatches = 0;
            _deferredCpuWaitSlotShadowDiskMismatches = 0;
            _deferredCpuWaitSlotShadowDisplayMismatches = 0;
            _deferredCpuWaitSlotShadowCopperMismatches = 0;
            _deferredCpuWaitSlotShadowLiveAttempts = 0;
            _deferredCpuWaitSlotShadowLiveSupported = 0;
            _deferredCpuWaitSlotShadowLiveUnsupported = 0;
            _deferredCpuWaitSlotShadowLiveUnsupportedPendingWrite = 0;
            _deferredCpuWaitSlotShadowLiveUnsupportedBitplaneWindow = 0;
            _deferredCpuWaitSlotShadowLiveUnsupportedCopperWaitWindow = 0;
            _deferredCpuWaitSlotShadowLiveUnsupportedRasterlinePlan = 0;
            _deferredCpuWaitSlotShadowLiveUnsupportedCpuPredict = 0;
            _deferredCpuWaitSlotShadowLiveUnsupportedUnstable = 0;
            _deferredCpuWaitSlotShadowLiveUnsupportedScratchWrite = 0;
            _deferredCpuWaitSlotShadowLiveUnsupportedLongWrite = 0;
            _deferredCpuWaitSlotShadowLiveUnsupportedOther = 0;
            _deferredCpuWaitSlotShadowLiveLongAccesses = 0;
            _deferredCpuWaitSlotShadowLiveBitplaneFetches = 0;
            _deferredCpuWaitSlotShadowLiveSpriteFetches = 0;
            _deferredCpuWaitSlotShadowLiveCopperSteps = 0;
            _deferredCpuWaitSlotShadowBlitterScratchAttempts = 0;
            _deferredCpuWaitSlotShadowBlitterScratchSupported = 0;
            _deferredCpuWaitSlotShadowBlitterScratchUnsupported = 0;
            _deferredCpuWaitSlotShadowBlitterScratchMatches = 0;
            _deferredCpuWaitSlotShadowBlitterScratchMismatches = 0;
            _deferredCpuWaitSlotShadowBlitterScratchPartial = 0;
            _deferredCpuWaitSlotShadowBlitterScratchMicroOps = 0;
            _deferredCpuWaitSlotShadowFirstMismatch = string.Empty;
            _deferredCpuWaitFixedImageAttempts = 0;
            _deferredCpuWaitFixedImageSupported = 0;
            _deferredCpuWaitFixedImageMatches = 0;
            _deferredCpuWaitFixedImageMismatches = 0;
            _deferredCpuWaitFixedImageUnsupported = 0;
            _deferredCpuWaitFixedImageFirstMismatch = string.Empty;
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
            _deferredCpuWaitFixedImageProductionVerificationMatches = 0;
            _deferredCpuWaitFixedImageProductionVerificationMismatches = 0;
            _deferredCpuWaitFixedImageProductionFirstMismatch = string.Empty;
            _deferredCpuWaitFixedImageProductionDisabled = false;
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
        private void VerifyProductionCpuWaitFixedSlotImage(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            long grantedCycle,
            long completedCycle,
            CpuWaitFixedSlotTimelineSignature predictedTimeline)
        {
            _ = TryCaptureCpuWaitFixedSlotTimeline(
                requestedCycle,
                completedCycle,
                grantedCycle,
                predicted: false,
                out var committedTimeline,
                out _);
            if (predictedTimeline.Equals(committedTimeline))
            {
                _deferredCpuWaitFixedImageProductionVerificationMatches++;
                return;
            }

            _deferredCpuWaitFixedImageProductionVerificationMismatches++;
            _deferredCpuWaitFixedImageProductionDisabled = true;
            if (_deferredCpuWaitFixedImageProductionFirstMismatch.Length == 0)
            {
                _deferredCpuWaitFixedImageProductionFirstMismatch =
                    $"production/{kind}/{target}/{size}/write={isWrite}/addr=0x{address:X6}/req={requestedCycle}/grant={grantedCycle}->{completedCycle}/image={predictedTimeline}/committed={committedTimeline}";
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void VerifyDeferredDmaReadOwnership(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long grantedCycle,
            long completedCycle,
            long predictedGrantedCycle,
            long predictedCompletedCycle,
            CpuWaitFixedSlotTimelineSignature predictedTimeline)
        {
            _ = TryCaptureCpuWaitFixedSlotTimeline(
                requestedCycle,
                completedCycle,
                grantedCycle,
                predicted: false,
                out var committedTimeline,
                out _);
            if (predictedGrantedCycle == grantedCycle &&
                predictedCompletedCycle == completedCycle &&
                predictedTimeline.Equals(committedTimeline))
            {
                _deferredCpuWaitFixedImageProductionVerificationMatches++;
                return;
            }

            _deferredCpuWaitFixedImageProductionVerificationMismatches++;
            _deferredCpuWaitFixedImageProductionDisabled = true;
            if (_deferredCpuWaitFixedImageProductionFirstMismatch.Length == 0)
            {
                _deferredCpuWaitFixedImageProductionFirstMismatch =
                    $"deferred-read/{kind}/{target}/{size}/addr=0x{address:X6}/req={requestedCycle}/" +
                    $"grant={predictedGrantedCycle}->{predictedCompletedCycle}/{grantedCycle}->{completedCycle}/" +
                    $"image={predictedTimeline}/committed={committedTimeline}";
            }
        }

        internal void VerifyProductionCpuWaitFixedSlotImageForTest(
            CpuWaitFixedSlotTimelineSignature predictedTimeline,
            CpuWaitFixedSlotTimelineSignature committedTimeline)
        {
            if (predictedTimeline.Equals(committedTimeline))
            {
                _deferredCpuWaitFixedImageProductionVerificationMatches++;
                return;
            }

            _deferredCpuWaitFixedImageProductionVerificationMismatches++;
            _deferredCpuWaitFixedImageProductionDisabled = true;
            _deferredCpuWaitFixedImageProductionFirstMismatch = "test-injected-fixed-image-mismatch";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void BeginDeferredCpuWaitScratchAudit(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long grantRequestCycle,
            bool isWrite,
            OcsLiveDmaScratchCpuWrite scratchWrite,
            ref DeferredCpuWaitScratchAudit audit)
        {
            var searchHorizon = grantRequestCycle + LineCycles;
            if (_deferredCpuWaitFixedImageAttempts < DeferredCpuWaitFixedImageMaxSamples &&
                LiveAgnusDmaEnabled &&
                Display.HasLiveDisplayWork() &&
                size is AmigaBusAccessSize.Byte or AmigaBusAccessSize.Word &&
                target is AmigaBusAccessTarget.ChipRam or AmigaBusAccessTarget.ExpansionRam or AmigaBusAccessTarget.RealTimeClock &&
                !Blitter.Busy &&
                !Disk.ActiveDma &&
                !Paula.HasDmaWorkThrough(searchHorizon))
            {
                audit.FixedImageAttempted = true;
                _deferredCpuWaitFixedImageAttempts++;
                audit.FixedImageSupported = TryPredictCpuWaitFixedSlotGrant(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out audit.FixedImageGrant,
                    out audit.FixedImageCompletion,
                    out audit.FixedImageUnsupported,
                    out audit.FixedImageTimeline);
                if (audit.FixedImageSupported)
                {
                    _deferredCpuWaitFixedImageSupported++;
                }
                else
                {
                    _deferredCpuWaitFixedImageUnsupported++;
                }
            }

            if (!Display.HasLiveDisplayWork() &&
                Blitter.Busy &&
                !Disk.ActiveDma &&
                !Paula.HasDmaWorkThrough(searchHorizon) &&
                IsDeferredCpuWaitSlotShadowGrantSupported(target, size, searchHorizon))
            {
                audit.BlitterAttempted = true;
                var scratchSlots = _hrmSlotEngine.CreateShadowCopy();
                audit.BlitterSupported = Blitter.TryRunCpuWaitSlotScratch(
                    scratchSlots,
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out audit.Blitter);
                RecordDeferredCpuWaitSlotShadowBlitterScratchAttempt(audit.Blitter);
                if (!audit.BlitterSupported)
                {
                    RecordDeferredCpuWaitSlotShadowUnsupported(
                        kind,
                        target,
                        address,
                        size,
                        isWrite,
                        requestedCycle,
                        grantRequestCycle,
                        CpuWaitSlotShadowReason.BlitterState,
                        audit.Blitter.ToDetailString());
                }
            }

            if (_deferredCpuWaitSlotShadowLiveAttempts >= DeferredCpuWaitSlotShadowLiveMaxSamples ||
                (audit.FixedImageAttempted && audit.FixedImageSupported) ||
                !LiveAgnusDmaEnabled ||
                !Display.HasLiveDisplayWork() ||
                !IsDeferredCpuWaitSlotShadowGrantSupported(target, size, searchHorizon))
            {
                return;
            }

            if (Blitter.Busy)
            {
                RecordDeferredCpuWaitSlotShadowUnsupported(
                    kind,
                    target,
                    address,
                    size,
                    isWrite,
                    requestedCycle,
                    grantRequestCycle,
                    CpuWaitSlotShadowReason.BlitterState,
                    "live-blitter-combined");
                return;
            }

            if (!TryPredictCpuGrant(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out var initialGrant,
                    out var initialSecondWord,
                    out _) ||
                !Display.HasLiveDmaSlotWorkThrough(
                    size == AmigaBusAccessSize.Long ? initialSecondWord : initialGrant))
            {
                return;
            }

            audit.LiveAttempted = true;
            var liveScratchSlots = _hrmSlotEngine.CreateShadowCopy();
            _deferredCpuWaitSlotShadowLiveAttempts++;
            audit.LiveSupported = Display.TryRunCpuWaitLiveDmaScratch(
                liveScratchSlots,
                kind,
                target,
                address,
                size,
                grantRequestCycle,
                isWrite,
                scratchWrite,
                out audit.Live);
            RecordDeferredCpuWaitSlotShadowLiveCoverage(size, audit.Live);
            if (!audit.LiveSupported)
            {
                RecordDeferredCpuWaitSlotShadowUnsupported(
                    kind,
                    target,
                    address,
                    size,
                    isWrite,
                    requestedCycle,
                    grantRequestCycle,
                    CpuWaitSlotShadowReason.Display,
                    audit.Live.ToDetailString());
            }
            else if (!audit.Live.HasLiveDmaCoverage && size != AmigaBusAccessSize.Long)
            {
                audit.LiveSupported = false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CompleteDeferredCpuWaitScratchAudit(
            in DeferredCpuWaitScratchAudit audit,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long grantRequestCycle,
            bool isWrite,
            long grantedCycle,
            long secondWordCycle,
            long completedCycle,
            AgnusSlotTimelineSignature referenceTimeline)
        {
            if (audit.FixedImageAttempted && audit.FixedImageSupported)
            {
                _ = TryCaptureCpuWaitFixedSlotTimeline(
                    grantRequestCycle,
                    completedCycle,
                    grantedCycle,
                    predicted: false,
                    out var fixedReferenceTimeline,
                    out _);
                if (audit.FixedImageGrant == grantedCycle &&
                    audit.FixedImageCompletion == completedCycle &&
                    audit.FixedImageTimeline.Equals(fixedReferenceTimeline))
                {
                    _deferredCpuWaitFixedImageMatches++;
                }
                else
                {
                    _deferredCpuWaitFixedImageMismatches++;
                    if (_deferredCpuWaitFixedImageFirstMismatch.Length == 0)
                    {
                        _deferredCpuWaitFixedImageFirstMismatch =
                            $"{kind}/{target}/{size}/write={isWrite}/addr=0x{address:X6}/req={grantRequestCycle}/image={audit.FixedImageGrant}->{audit.FixedImageCompletion}/{audit.FixedImageTimeline}/reference={grantedCycle}->{completedCycle}/{fixedReferenceTimeline}";
                    }
                }
            }

            if (audit.LiveAttempted && audit.LiveSupported)
            {
                RecordDeferredCpuWaitSlotShadowAudit(
                    kind,
                    target,
                    address,
                    size,
                    isWrite,
                    requestedCycle,
                    grantRequestCycle,
                    audit.Live.GrantedCycle,
                    audit.Live.SecondWordCycle,
                    audit.Live.CompletedCycle,
                    grantedCycle,
                    secondWordCycle,
                    completedCycle,
                    audit.Live.Timeline,
                    referenceTimeline,
                    audit.Live.ToDetailString());
            }

            if (!audit.BlitterAttempted || !audit.BlitterSupported)
            {
                return;
            }

            var blitterReferenceTimeline = _hrmSlotEngine.CaptureOwnerTimelineSignature(
                grantRequestCycle,
                completedCycle);
            RecordDeferredCpuWaitSlotShadowBlitterScratchComparison(
                audit.Blitter,
                grantedCycle,
                secondWordCycle,
                completedCycle,
                blitterReferenceTimeline);
            RecordDeferredCpuWaitSlotShadowAudit(
                kind,
                target,
                address,
                size,
                isWrite,
                requestedCycle,
                grantRequestCycle,
                audit.Blitter.GrantedCycle,
                audit.Blitter.SecondWordCycle,
                audit.Blitter.CompletedCycle,
                grantedCycle,
                secondWordCycle,
                completedCycle,
                audit.Blitter.Timeline,
                blitterReferenceTimeline,
                audit.Blitter.ToDetailString());
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

            if (!ShouldRunDeferredCpuWaitSlotShadowAudit)
            {
                return;
            }

            var timeline = _hrmSlotEngine.CaptureTimelineSignature(grantRequestCycle, completedCycle);
            RecordDeferredCpuWaitSlotShadowAudit(
                kind,
                target,
                address,
                size,
                isWrite,
                requestedCycle,
                grantRequestCycle,
                grantedCycle,
                secondWordCycle,
                completedCycle,
                grantedCycle,
                secondWordCycle,
                completedCycle,
                timeline,
                timeline);
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



        private void RecordDeferredCpuWaitDynamicRejectShadow(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite,
            long requestedCycle)
        {
            if (!ShouldRunDeferredCpuWaitSlotShadowAudit)
            {
                return;
            }

            if (Blitter.Busy &&
                !Display.HasLiveDisplayWork() &&
                !Disk.ActiveDma &&
                !Paula.HasDmaWorkThrough(requestedCycle + LineCycles))
            {
                var scratchSlots = _hrmSlotEngine.CreateShadowCopy();
                _ = Blitter.TryRunCpuWaitSlotScratch(
                    scratchSlots,
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite,
                    out var blitterScratch);
                RecordDeferredCpuWaitSlotShadowBlitterScratchAttempt(blitterScratch);
                RecordDeferredCpuWaitSlotShadowUnsupported(
                    kind,
                    target,
                    address,
                    size,
                    isWrite,
                    requestedCycle,
                    requestedCycle,
                    CpuWaitSlotShadowReason.BlitterState,
                    blitterScratch.ToDetailString());
                return;
            }

            if (!Display.HasLiveDisplayWork())
            {
                RecordDeferredCpuWaitSlotShadowUnsupported(
                    kind,
                    target,
                    address,
                    size,
                    isWrite,
                    requestedCycle,
                    requestedCycle,
                    Disk.ActiveDma ? CpuWaitSlotShadowReason.Disk : CpuWaitSlotShadowReason.Paula);
            }
        }


        private bool ShouldRunDeferredCpuWaitSlotShadowAudit
            => _deferredCpuBusBatchVerifyEnabled &&
               !_forceCpuWaitSlotReference &&
               _deferredCpuWaitSlotShadowAttempts < DeferredCpuWaitSlotShadowMaxSamples;

        private bool IsDeferredCpuWaitSlotShadowGrantSupported(
            AmigaBusAccessTarget target,
            AmigaBusAccessSize size,
            long referenceCompletion)
        {
            if (target is not (AmigaBusAccessTarget.ChipRam or
                AmigaBusAccessTarget.ExpansionRam or
                AmigaBusAccessTarget.RealTimeClock or
                AmigaBusAccessTarget.CustomRegisters))
            {
                return false;
            }

            if (Disk.ActiveDma)
            {
                return false;
            }

            return !Paula.HasDmaWorkThrough(referenceCompletion);
        }

        private void RecordDeferredCpuWaitSlotShadowUnsupported(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite,
            long requestedCycle,
            long grantRequestCycle,
            CpuWaitSlotShadowReason reason,
            string extraDetail = "")
        {
            if (!ShouldRunDeferredCpuWaitSlotShadowAudit)
            {
                return;
            }

            if (reason == CpuWaitSlotShadowReason.Display &&
                !HasActionableLiveDisplayWaitSlotAuditContext())
            {
                return;
            }

            _deferredCpuWaitSlotShadowAttempts++;
            _deferredCpuWaitSlotShadowUnsupported++;
            IncrementDeferredCpuWaitSlotShadowReason(reason);
            RecordDeferredCpuWaitSlotShadowFirstDetail(
                kind,
                target,
                address,
                size,
                isWrite,
                requestedCycle,
                grantRequestCycle,
                reason,
                "unsupported",
                -1,
                -1,
                -1,
                -1,
                -1,
                -1,
                default,
                default,
                extraDetail);
        }

        private bool HasActionableLiveDisplayWaitSlotAuditContext()
        {
            if (!Display.HasLiveDisplayWork())
            {
                return false;
            }

            var display = Display.CaptureSnapshot();
            return display.Bplcon0 != 0 ||
                display.LastFirstDisplayDmaCycle >= 0 ||
                display.LastBitplaneDmaFetches != 0 ||
                display.LastSpriteDmaFetches != 0 ||
                Display.LiveCopperStepCount != 0;
        }

        private void RecordDeferredCpuWaitSlotShadowAudit(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite,
            long requestedCycle,
            long grantRequestCycle,
            long shadowGrant,
            long shadowSecondWord,
            long shadowCompletion,
            long referenceGrant,
            long referenceSecondWord,
            long referenceCompletion,
            AgnusSlotTimelineSignature shadowTimeline,
            AgnusSlotTimelineSignature referenceTimeline,
            string extraDetail = "")
        {
            if (!ShouldRunDeferredCpuWaitSlotShadowAudit)
            {
                return;
            }

            _deferredCpuWaitSlotShadowAttempts++;
            if (target is not (AmigaBusAccessTarget.ChipRam or
                AmigaBusAccessTarget.ExpansionRam or
                AmigaBusAccessTarget.RealTimeClock or
                AmigaBusAccessTarget.CustomRegisters))
            {
                _deferredCpuWaitSlotShadowUnsupported++;
                IncrementDeferredCpuWaitSlotShadowReason(CpuWaitSlotShadowReason.SlotOwner);
                return;
            }

            if (Disk.ActiveDma)
            {
                _deferredCpuWaitSlotShadowUnsupported++;
                IncrementDeferredCpuWaitSlotShadowReason(CpuWaitSlotShadowReason.Disk);
                return;
            }

            if (Paula.HasDmaWorkThrough(referenceCompletion))
            {
                _deferredCpuWaitSlotShadowUnsupported++;
                IncrementDeferredCpuWaitSlotShadowReason(CpuWaitSlotShadowReason.Paula);
                return;
            }

            if (shadowGrant == referenceGrant &&
                shadowSecondWord == referenceSecondWord &&
                shadowCompletion == referenceCompletion &&
                shadowTimeline.Equals(referenceTimeline))
            {
                _deferredCpuWaitSlotShadowMatches++;
                return;
            }

            var reason = shadowGrant != referenceGrant
                ? CpuWaitSlotShadowReason.Grant
                : shadowCompletion != referenceCompletion || shadowSecondWord != referenceSecondWord
                    ? CpuWaitSlotShadowReason.Completion
                    : CpuWaitSlotShadowReason.SlotOwner;
            _deferredCpuWaitSlotShadowMismatches++;
            IncrementDeferredCpuWaitSlotShadowReason(reason);
            RecordDeferredCpuWaitSlotShadowFirstDetail(
                kind,
                target,
                address,
                size,
                isWrite,
                requestedCycle,
                grantRequestCycle,
                reason,
                "mismatch",
                shadowGrant,
                shadowSecondWord,
                shadowCompletion,
                referenceGrant,
                referenceSecondWord,
                referenceCompletion,
                shadowTimeline,
                referenceTimeline,
                extraDetail);
        }

        private void RunDeferredCpuWaitSlotShadowGrant(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long grantRequestCycle,
            bool isWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle,
            out AgnusSlotTimelineSignature timeline)
        {
            var shadow = _hrmSlotEngine.CreateShadowCopy();
            if (size == AmigaBusAccessSize.Long)
            {
                shadow.GrantCpuDataLongSlots(
                    kind,
                    target,
                    address,
                    grantRequestCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }
            else
            {
                shadow.GrantCpuDataSingleSlot(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out grantedCycle,
                    out completedCycle);
                secondWordCycle = grantedCycle;
            }

            timeline = shadow.CaptureTimelineSignature(grantRequestCycle, completedCycle);
        }

        private void IncrementDeferredCpuWaitSlotShadowReason(CpuWaitSlotShadowReason reason)
        {
            switch (reason)
            {
                case CpuWaitSlotShadowReason.Grant:
                    _deferredCpuWaitSlotShadowGrantMismatches++;
                    break;
                case CpuWaitSlotShadowReason.Completion:
                    _deferredCpuWaitSlotShadowCompletionMismatches++;
                    break;
                case CpuWaitSlotShadowReason.SlotOwner:
                    _deferredCpuWaitSlotShadowSlotOwnerMismatches++;
                    break;
                case CpuWaitSlotShadowReason.BlitterState:
                    _deferredCpuWaitSlotShadowBlitterStateMismatches++;
                    break;
                case CpuWaitSlotShadowReason.Paula:
                    _deferredCpuWaitSlotShadowPaulaMismatches++;
                    break;
                case CpuWaitSlotShadowReason.Disk:
                    _deferredCpuWaitSlotShadowDiskMismatches++;
                    break;
                case CpuWaitSlotShadowReason.Display:
                    _deferredCpuWaitSlotShadowDisplayMismatches++;
                    break;
                case CpuWaitSlotShadowReason.Copper:
                    _deferredCpuWaitSlotShadowCopperMismatches++;
                    break;
            }
        }

        private void RecordDeferredCpuWaitSlotShadowLiveCoverage(
            AmigaBusAccessSize size,
            in OcsLiveDmaScratchResult result)
        {
            if (size == AmigaBusAccessSize.Long)
            {
                _deferredCpuWaitSlotShadowLiveLongAccesses++;
            }

            if (!result.Supported)
            {
                _deferredCpuWaitSlotShadowLiveUnsupported++;
                IncrementDeferredCpuWaitSlotShadowLiveUnsupportedReason(result.UnsupportedReason);
                return;
            }

            _deferredCpuWaitSlotShadowLiveSupported++;
            _deferredCpuWaitSlotShadowLiveBitplaneFetches += result.BitplaneFetches;
            _deferredCpuWaitSlotShadowLiveSpriteFetches += result.SpriteFetches;
            _deferredCpuWaitSlotShadowLiveCopperSteps += result.CopperSteps;
        }

        private void IncrementDeferredCpuWaitSlotShadowLiveUnsupportedReason(string reason)
        {
            switch (reason)
            {
                case "pending-write":
                    _deferredCpuWaitSlotShadowLiveUnsupportedPendingWrite++;
                    break;
                case "bitplane-window":
                    _deferredCpuWaitSlotShadowLiveUnsupportedBitplaneWindow++;
                    break;
                case "copper-wait-window":
                    _deferredCpuWaitSlotShadowLiveUnsupportedCopperWaitWindow++;
                    break;
                case "rasterline-plan":
                    _deferredCpuWaitSlotShadowLiveUnsupportedRasterlinePlan++;
                    break;
                case "cpu-predict":
                    _deferredCpuWaitSlotShadowLiveUnsupportedCpuPredict++;
                    break;
                case "unstable":
                    _deferredCpuWaitSlotShadowLiveUnsupportedUnstable++;
                    break;
                case "scratch-write":
                    _deferredCpuWaitSlotShadowLiveUnsupportedScratchWrite++;
                    break;
                case "size-long-write":
                    _deferredCpuWaitSlotShadowLiveUnsupportedLongWrite++;
                    break;
                default:
                    _deferredCpuWaitSlotShadowLiveUnsupportedOther++;
                    break;
            }
        }

        private void RecordDeferredCpuWaitSlotShadowBlitterScratchAttempt(
            in BlitterCpuWaitScratchResult result)
        {
            _deferredCpuWaitSlotShadowBlitterScratchAttempts++;
            if (!result.Supported)
            {
                _deferredCpuWaitSlotShadowBlitterScratchUnsupported++;
                return;
            }

            _deferredCpuWaitSlotShadowBlitterScratchSupported++;
            _deferredCpuWaitSlotShadowBlitterScratchMicroOps += result.MicroOps;
            if (result.StartedFromPartial)
            {
                _deferredCpuWaitSlotShadowBlitterScratchPartial++;
            }
        }

        private void RecordDeferredCpuWaitSlotShadowBlitterScratchComparison(
            in BlitterCpuWaitScratchResult result,
            long referenceGrant,
            long referenceSecondWord,
            long referenceCompletion,
            AgnusSlotTimelineSignature referenceTimeline)
        {
            if (result.GrantedCycle == referenceGrant &&
                result.SecondWordCycle == referenceSecondWord &&
                result.CompletedCycle == referenceCompletion &&
                result.Timeline.Equals(referenceTimeline))
            {
                _deferredCpuWaitSlotShadowBlitterScratchMatches++;
            }
            else
            {
                _deferredCpuWaitSlotShadowBlitterScratchMismatches++;
            }
        }

        private void RecordDeferredCpuWaitSlotShadowFirstDetail(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite,
            long requestedCycle,
            long grantRequestCycle,
            CpuWaitSlotShadowReason reason,
            string outcome,
            long shadowGrant,
            long shadowSecondWord,
            long shadowCompletion,
            long referenceGrant,
            long referenceSecondWord,
            long referenceCompletion,
            AgnusSlotTimelineSignature shadowTimeline,
            AgnusSlotTimelineSignature referenceTimeline,
            string extraDetail = "")
        {
            if (_deferredCpuWaitSlotShadowFirstMismatch.Length != 0 &&
                (outcome != "mismatch" || !_deferredCpuWaitSlotShadowFirstMismatch.StartsWith("unsupported/", StringComparison.Ordinal)))
            {
                return;
            }

            var blitter = Blitter.CaptureSnapshot();
            var displayDetail = string.Empty;
            if (reason is CpuWaitSlotShadowReason.Display or CpuWaitSlotShadowReason.Copper)
            {
                var display = Display.CaptureSnapshot();
                var nextCopper = Display.GetNextLiveCopperWakeCandidateCycle(
                    grantRequestCycle,
                    grantRequestCycle + LineCycles);
                displayDetail =
                    $"/display=live:{Display.HasLiveDisplayWork()},copSteps:{Display.LiveCopperStepCount},nextCop:{(nextCopper.HasValue ? nextCopper.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")},dma:{display.LastBitplaneDmaFetches}/{display.LastSpriteDmaFetches}/{display.LastMissedSpriteDmaSlots},firstLast:{display.LastFirstDisplayDmaCycle}->{display.LastLastDisplayDmaCycle},rowPlan:{display.LastRowDmaPlansBuilt}/{display.LastRowDmaPlannedRowsExecuted}/{display.LastRowDmaBitplaneEntriesExecuted}/{display.LastRowDmaSpriteEntriesExecuted}/{display.LastRowDmaScalarFallbackRows}/{display.LastRowDmaPlanInvalidationRows}/{display.LastRowDmaPlanMismatchRows},bplcon:{display.Bplcon0:X4}/{display.Bplcon1:X4}/{display.Bplcon2:X4}";
            }

            _deferredCpuWaitSlotShadowFirstMismatch =
                $"{outcome}/{reason}/{kind}/{target}/{size}/write={isWrite}/addr=0x{address:X6}/req={requestedCycle}/grantreq={grantRequestCycle}/shadow={shadowGrant},{shadowSecondWord}->{shadowCompletion}/ref={referenceGrant},{referenceSecondWord}->{referenceCompletion}/slots={shadowTimeline}|{referenceTimeline}/bltBusy={blitter.Busy}/bltCycle={blitter.CurrentCycle}{displayDetail}{(extraDetail.Length == 0 ? string.Empty : "/" + extraDetail)}";
        }


    }
}
