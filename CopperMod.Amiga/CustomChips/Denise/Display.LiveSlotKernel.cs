/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        /// <summary>
        /// Commits at most one display-local transition through the exact G6
        /// candidate. The caller owns repetition; this boundary never batches
        /// fetches or advances the display through a range.
        /// </summary>
        internal bool TryCommitNextLiveSlotKernelTransition(long targetCycle)
        {
            if (!_liveDmaEnabled ||
                !_bus.LiveAgnusDmaEnabled ||
                !_liveFrameValid ||
                !HasLiveDisplayWork())
            {
                return false;
            }

            targetCycle = Math.Max(_liveFrameStartCycle, targetCycle);
            var frameStopCycle = GetLiveFrameStopCycle();
            var effectiveTarget = Math.Min(targetCycle, frameStopCycle - 1);
            var savedAdvancingLiveDma = BeginLiveDmaCapture();
            try
            {
                SkipLiveRowsWithoutFetches();
                SkipLiveSpriteSlotsWithoutFetches();

                var nextLineStateCycle = GetNextLiveLineStateCycle();
                var nextBitplaneFetchCycle = GetNextLiveBitplaneFetchCycle();
                var nextSpriteFetchCycle = GetNextLiveSpriteFetchCycle();
                var nextPendingWriteCycle = GetNextLivePendingWriteCycle();
                var nextCopperCycle = GetNextLiveCopperCycle(frameStopCycle);
                var nextDisplayEventCycle = Math.Min(
                    nextPendingWriteCycle,
                    nextCopperCycle);
                var nextFixedCycle = Math.Min(
                    nextLineStateCycle,
                    Math.Min(nextSpriteFetchCycle, nextBitplaneFetchCycle));

                if (nextDisplayEventCycle <= effectiveTarget &&
                    nextDisplayEventCycle <= nextFixedCycle)
                {
                    if (nextPendingWriteCycle <= nextCopperCycle)
                    {
                        ApplyPendingWritesForLiveDma(nextPendingWriteCycle);
                        _liveCycle = Math.Max(_liveCycle, nextPendingWriteCycle);
                        _liveDisplayEventCount++;
                        _livePendingWriteEventCount++;
                    }
                    else
                    {
                        if (_liveCopperRequesterEnabled)
                        {
                            var requester = RequireLiveCopperRequester();
                            if (!TryCommitUnsatisfiedLiveCopperRequesterWaitComparison(
                                    requester,
                                    nextCopperCycle,
                                    effectiveTarget))
                            {
                                StepLiveCopperRequester(effectiveTarget);
                            }
                        }
                        else
                        {
                            StepLiveCopper(effectiveTarget);
                            _liveCopperStepCount++;
                        }

                        _liveCycle = Math.Max(_liveCycle, _liveCopper.Cycle);
                        _liveDisplayEventCount++;
                    }

                    InvalidateLiveDisplayEventCycle();
                    return true;
                }

                if (nextFixedCycle <= effectiveTarget)
                {
                    _liveCycle = Math.Max(_liveCycle, nextFixedCycle);
                    if (nextLineStateCycle == nextFixedCycle)
                    {
                        CaptureLiveLineState(_liveNextLineStateRow);
                        TryBuildPredictedRasterlinePlanForCapturedLine(
                            _liveNextLineStateRow);
                        _liveNextLineStateRow++;
                        InvalidateLiveWorkCycle();
                        return true;
                    }

                    if (nextSpriteFetchCycle == nextFixedCycle)
                    {
                        _ = TryCaptureKnownLiveSpriteDmaSlot(
                            _liveNextSpriteRow,
                            _liveNextSpriteIndex,
                            _liveNextSpriteWord,
                            nextSpriteFetchCycle);
                        AdvanceLiveSpriteFetchCursor();
                        InvalidateLiveWorkCycle();
                        return true;
                    }

                    CaptureLiveBitplaneFetch(nextBitplaneFetchCycle);
                    AdvanceLiveFetchCursor();
                    InvalidateLiveWorkCycle();
                    return true;
                }

                _liveCycle = Math.Max(_liveCycle, effectiveTarget);
                _liveCapturedThroughCycle = Math.Max(
                    _liveCapturedThroughCycle,
                    effectiveTarget);
                InvalidateLiveWakeCandidateQueryCache();
                if (targetCycle < frameStopCycle)
                {
                    return false;
                }

                StartLiveFrame(frameStopCycle);
                return true;
            }
            finally
            {
                EndLiveDmaCapture(savedAdvancingLiveDma);
            }
        }
    }
}
