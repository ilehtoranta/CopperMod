/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private BitplaneDmaReadLatch LoadLiveBitplaneDmaLatchThroughContract(
            int row,
            int plane,
            int word,
            uint address,
            long fetchCycle)
        {
            var requester = _liveBitplaneRequester ??
                throw new InvalidOperationException(
                    "The live bitplane requester is not enabled.");
            requester.Publish(row, plane, word, address, fetchCycle);
            if (!_bus.TryCommitLiveFixedDisplayRequest(
                requester,
                out var observedSlotCycle))
            {
                requester.CancelDenied(observedSlotCycle);
            }

            return requester.TakeResult();
        }

        private BitplaneDmaReadLatch LoadAcceptedBitplaneDmaLatchThroughContract(
            in BitplaneDmaPipeline issued)
        {
            var requester = _liveBitplaneRequester ??
                throw new InvalidOperationException("The live bitplane requester is not enabled.");
            var word = issued.Word;
            requester.Publish(word.Row, word.Plane, word.StorageWord,
                issued.Access.Request.Address, issued.Access.RequestedCycle, issued.OutputCycle);
            if (!_bus.TryCommitLiveFixedDisplayRequest(requester, out _))
            {
                throw new InvalidOperationException("An issued bitplane output cannot be denied after input acceptance.");
            }

            return requester.TakeResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SpriteDmaReadLatch LoadSpriteDmaReadLatchThroughContract(
            int row,
            int spriteIndex,
            int word,
            uint address,
            long fetchCycle)
        {
            var requester = _liveSpriteRequester ??
                throw new InvalidOperationException(
                    "The live sprite requester is not enabled.");
            requester.Publish(row, spriteIndex, word, address, fetchCycle);
            if (!_bus.TryCommitLiveFixedDisplayRequest(
                requester,
                out var observedSlotCycle))
            {
                requester.CancelDenied(observedSlotCycle);
            }

            return requester.TakeResult();
        }

        private sealed class LiveBitplaneRequester : IAgnusLiveSlotRequester
        {
            private AgnusLiveRequestLatch _requestLatch =
                new(AgnusChipSlotOwner.Bitplane);
            private BitplaneDmaReadLatch _result;
            private int _row;
            private int _plane;
            private int _word;
            private uint _address;
            private ulong _publishedGeneration;
            private bool _hasResult;

            public AgnusChipSlotOwner Owner => AgnusChipSlotOwner.Bitplane;

            public void Publish(
                int row,
                int plane,
                int word,
                uint address,
                long fetchCycle,
                long outputCycle = -1)
            {
                if (_hasResult)
                {
                    throw new InvalidOperationException(
                        "The previous live bitplane result was not consumed.");
                }

                _row = row;
                _plane = plane;
                _word = word;
                _address = address;
                var request = _requestLatch.Publish(
                    AmigaBusAccessKind.Bitplane,
                    address,
                    fetchCycle,
                    outputCycle >= 0 ? outputCycle : fetchCycle,
                    AgnusLiveWordTransfer.Read,
                    channel: plane);
                _publishedGeneration = request.Generation;
            }

            public bool TryPeekPendingRequest(out AgnusLiveSlotRequest request)
                => _requestLatch.TryPeek(out request);

            public bool TryPeekNextTransition(out AgnusLiveTransition transition)
            {
                transition = default;
                return false;
            }

            public void CommitGrantedSlot(in AgnusLiveSlotGrant grant)
            {
                _requestLatch.Consume(grant);
                _result = new BitplaneDmaReadLatch(
                    _row,
                    _plane,
                    _word,
                    _address,
                    addressValid: true,
                    grant.SampledValue,
                    granted: true,
                    grant.SlotCycle);
                _hasResult = true;
            }

            public void CommitTransition(in AgnusLiveTransition transition)
                => throw new InvalidOperationException(
                    "A fixed bitplane requester has no device-local transitions.");

            public void CancelDenied(long observedSlotCycle)
            {
                _requestLatch.Cancel(_publishedGeneration);
                _result = BitplaneDmaReadLatch.Denied(
                    _row,
                    _plane,
                    _word,
                    _address,
                    addressValid: true,
                    observedSlotCycle);
                _hasResult = true;
            }

            public BitplaneDmaReadLatch TakeResult()
            {
                if (!_hasResult)
                {
                    throw new InvalidOperationException(
                        "The live bitplane requester has no committed or denied result.");
                }

                var result = _result;
                _result = default;
                _hasResult = false;
                return result;
            }
        }

        private sealed class LiveSpriteRequester : IAgnusLiveSlotRequester
        {
            private AgnusLiveRequestLatch _requestLatch =
                new(AgnusChipSlotOwner.Sprite);
            private SpriteDmaReadLatch _result;
            private int _row;
            private int _spriteIndex;
            private int _word;
            private uint _address;
            private ulong _publishedGeneration;
            private bool _hasResult;

            public AgnusChipSlotOwner Owner => AgnusChipSlotOwner.Sprite;

            public void Publish(
                int row,
                int spriteIndex,
                int word,
                uint address,
                long fetchCycle)
            {
                if (_hasResult)
                {
                    throw new InvalidOperationException(
                        "The previous live sprite result was not consumed.");
                }

                _row = row;
                _spriteIndex = spriteIndex;
                _word = word;
                _address = address;
                var request = _requestLatch.Publish(
                    AmigaBusAccessKind.Sprite,
                    address,
                    fetchCycle,
                    fetchCycle,
                    AgnusLiveWordTransfer.Read,
                    channel: spriteIndex);
                _publishedGeneration = request.Generation;
            }

            public bool TryPeekPendingRequest(out AgnusLiveSlotRequest request)
                => _requestLatch.TryPeek(out request);

            public bool TryPeekNextTransition(out AgnusLiveTransition transition)
            {
                transition = default;
                return false;
            }

            public void CommitGrantedSlot(in AgnusLiveSlotGrant grant)
            {
                _requestLatch.Consume(grant);
                _result = new SpriteDmaReadLatch(
                    _row,
                    _spriteIndex,
                    _word,
                    _address,
                    addressValid: true,
                    grant.SampledValue,
                    granted: true,
                    grant.SlotCycle);
                _hasResult = true;
            }

            public void CommitTransition(in AgnusLiveTransition transition)
                => throw new InvalidOperationException(
                    "A fixed sprite requester has no device-local transitions.");

            public void CancelDenied(long observedSlotCycle)
            {
                _requestLatch.Cancel(_publishedGeneration);
                _result = SpriteDmaReadLatch.Denied(
                    _row,
                    _spriteIndex,
                    _word,
                    _address,
                    addressValid: true,
                    observedSlotCycle);
                _hasResult = true;
            }

            public SpriteDmaReadLatch TakeResult()
            {
                if (!_hasResult)
                {
                    throw new InvalidOperationException(
                        "The live sprite requester has no committed or denied result.");
                }

                var result = _result;
                _result = default;
                _hasResult = false;
                return result;
            }
        }
    }
}
