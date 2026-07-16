/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal sealed partial class OcsDisplay
    {
        private void RenderSprites(Span<uint> bgra)
        {
            for (var spriteGroup = (_sprites.Length / 2) - 1; spriteGroup >= 0; spriteGroup--)
            {
                var spriteIndex = spriteGroup * 2;
                var evenSprites = GetSpriteFrameCommands(spriteIndex);
                var oddSprites = GetSpriteFrameCommands(spriteIndex + 1);
                Array.Clear(_evenSpriteAttached, 0, Math.Min(evenSprites.Count, _evenSpriteAttached.Length));
                Array.Clear(_oddSpriteAttached, 0, Math.Min(oddSprites.Count, _oddSpriteAttached.Length));

                for (var oddIndex = 0; oddIndex < oddSprites.Count; oddIndex++)
                {
                    var oddSprite = oddSprites[oddIndex];
                    var evenIndex = FindAttachedEvenSprite(evenSprites, _evenSpriteAttached, oddSprite);
                    if (evenIndex < 0)
                    {
                        if (oddSprite.Descriptor.Attached)
                        {
                            _oddSpriteAttached[oddIndex] = true;
                        }

                        continue;
                    }

                    _evenSpriteAttached[evenIndex] = true;
                    _oddSpriteAttached[oddIndex] = true;
                    RenderAttachedSpritePair(bgra, spriteIndex, evenSprites[evenIndex], oddSprite);
                }

                for (var i = 0; i < oddSprites.Count; i++)
                {
                    if (!_oddSpriteAttached[i] && !oddSprites[i].Descriptor.Attached)
                    {
                        RenderSprite(bgra, spriteIndex + 1, oddSprites[i]);
                    }
                }

                for (var i = 0; i < evenSprites.Count; i++)
                {
                    if (!_evenSpriteAttached[i])
                    {
                        RenderSprite(bgra, spriteIndex, evenSprites[i]);
                    }
                }
            }
        }

        private void RenderTimelineSprites(Span<uint> bgra, DisplayFrameTimeline timeline)
        {
            for (var spriteGroup = (_sprites.Length / 2) - 1; spriteGroup >= 0; spriteGroup--)
            {
                var spriteIndex = spriteGroup * 2;
                var evenSprites = GetTimelineSpriteFrameCommands(spriteIndex, timeline);
                var oddSprites = GetTimelineSpriteFrameCommands(spriteIndex + 1, timeline);
                Array.Clear(_evenSpriteAttached, 0, Math.Min(evenSprites.Count, _evenSpriteAttached.Length));
                Array.Clear(_oddSpriteAttached, 0, Math.Min(oddSprites.Count, _oddSpriteAttached.Length));

                for (var oddIndex = 0; oddIndex < oddSprites.Count; oddIndex++)
                {
                    var oddSprite = oddSprites[oddIndex];
                    var evenIndex = FindAttachedEvenSprite(evenSprites, _evenSpriteAttached, oddSprite);
                    if (evenIndex < 0)
                    {
                        if (oddSprite.Descriptor.Attached)
                        {
                            _oddSpriteAttached[oddIndex] = true;
                        }

                        continue;
                    }

                    _evenSpriteAttached[evenIndex] = true;
                    _oddSpriteAttached[oddIndex] = true;
                    RenderTimelineAttachedSpritePair(bgra, spriteIndex, evenSprites[evenIndex], oddSprite, timeline);
                }

                for (var i = 0; i < oddSprites.Count; i++)
                {
                    if (!_oddSpriteAttached[i] && !oddSprites[i].Descriptor.Attached)
                    {
                        RenderTimelineSprite(bgra, spriteIndex + 1, oddSprites[i], timeline);
                    }
                }

                for (var i = 0; i < evenSprites.Count; i++)
                {
                    if (!_evenSpriteAttached[i])
                    {
                        RenderTimelineSprite(bgra, spriteIndex, evenSprites[i], timeline);
                    }
                }
            }
        }

        private List<SpriteFrameCommand> GetTimelineSpriteFrameCommands(int spriteIndex, DisplayFrameTimeline timeline)
        {
            var commands = _spriteCommandScratch[spriteIndex];
            timeline.CopySpriteFrameCommands(spriteIndex, commands);
            return commands;
        }

        private List<SpriteFrameCommand> GetSpriteFrameCommands(int spriteIndex)
        {
            var commands = _spriteCommandScratch[spriteIndex];
            commands.Clear();
            for (var i = 0; i < _spriteFrameCommands.Count; i++)
            {
                var command = _spriteFrameCommands[i];
                if (command.SpriteIndex == spriteIndex)
                {
                    AppendUniqueSpriteFrameCommand(commands, command);
                }
            }

            if (commands.Count == 0 &&
                _previousLiveSpriteFrameStartCycle == _renderFrameStartCycle)
            {
                for (var i = 0; i < _previousLiveSpriteFrameCommands.Count; i++)
                {
                    var command = _previousLiveSpriteFrameCommands[i];
                    if (command.SpriteIndex == spriteIndex)
                    {
                        AppendUniqueSpriteFrameCommand(commands, command);
                    }
                }

                if (commands.Count > 0)
                {
                    return commands;
                }
            }

            var sprite = _sprites[spriteIndex];
            var allowStateFallback = !_renderingLiveCapture &&
                (!_useTimedPresentationReads ||
                    !_bus.LiveAgnusDmaEnabled ||
                    HasArchivedLiveSpriteFrameCommands(_renderFrameStartCycle));
            if (commands.Count == 0 && allowStateFallback && IsSpriteDmaEnabled())
            {
                if (IsSpriteDmaChannelAvailable(spriteIndex))
                {
                    AppendDmaSpriteFrameCommands(commands, spriteIndex, sprite.Pointer, 0);
                }
                else if (_useTimedPresentationReads)
                {
                    _lastMissedSpriteDmaSlots++;
                }
            }
            else if (commands.Count == 0 &&
                allowStateFallback &&
                TryGetManualSpriteDescriptor(spriteIndex, out var descriptor))
            {
                AppendUniqueSpriteFrameCommand(
                    commands,
                    new SpriteFrameCommand(spriteIndex, 0, descriptor));
            }

            return commands;
        }

        private bool HasArchivedLiveSpriteFrameCommands(long frameStartCycle)
        {
            return _previousLiveSpriteFrameStartCycle == frameStartCycle &&
                _previousLiveSpriteFrameCommands.Count > 0;
        }

        private static void AppendUniqueSpriteFrameCommand(List<SpriteFrameCommand> commands, SpriteFrameCommand command)
        {
            if (commands.Count >= commands.Capacity)
            {
                return;
            }

            for (var i = commands.Count - 1; i >= 0; i--)
            {
                if (IsPendingSpriteControlReplacement(commands[i], command))
                {
                    commands[i] = command;
                    return;
                }

                if (IsPendingSpriteControlReplacement(command, commands[i]))
                {
                    return;
                }

                if (IsOverlappingRestartOfSameSpriteImage(commands[i], command))
                {
                    return;
                }

                if (IsOverlappingRestartOfSameSpriteImage(command, commands[i]))
                {
                    commands[i] = command;
                    return;
                }

                if (commands[i].SpriteIndex == command.SpriteIndex &&
                    commands[i].Descriptor.HasSameRenderingAs(command.Descriptor))
                {
                    if (commands[i].Row <= command.Row)
                    {
                        return;
                    }

                    commands[i] = command;
                    return;
                }

                if (commands[i].HasSameRenderingAs(command))
                {
                    return;
                }
            }

            commands.Add(command);
        }

        private static bool IsPendingSpriteControlReplacement(SpriteFrameCommand earlier, SpriteFrameCommand later)
        {
            if (earlier.SpriteIndex != later.SpriteIndex ||
                earlier.Row >= later.Row)
            {
                return false;
            }

            var first = earlier.Descriptor;
            var second = later.Descriptor;
            return first.IsDma &&
                second.IsDma &&
                first.DataAddress == second.DataAddress &&
                first.YStart == second.YStart &&
                first.YStop == second.YStop &&
                first.Attached == second.Attached &&
                SpriteDescriptorDataMatches(first, second) &&
                later.Row <= first.YStart;
        }

        private static bool IsOverlappingRestartOfSameSpriteImage(SpriteFrameCommand earlier, SpriteFrameCommand later)
        {
            if (earlier.SpriteIndex != later.SpriteIndex)
            {
                return false;
            }

            var first = earlier.Descriptor;
            var second = later.Descriptor;
            return first.X == second.X &&
                first.YStart <= second.YStart &&
                second.YStart < first.YStop &&
                first.YStop == second.YStop &&
                first.Attached == second.Attached &&
                first.DataAddress == second.DataAddress &&
                first.IsDma == second.IsDma &&
                SpriteDescriptorDataMatches(first, second) &&
                earlier.Row <= later.Row;
        }

        private static bool SpriteDescriptorDataMatches(SpriteDescriptor left, SpriteDescriptor right)
        {
            return left.IsDma == right.IsDma &&
                (left.IsDma ||
                    (left.ManualDataA == right.ManualDataA &&
                        left.ManualDataB == right.ManualDataB));
        }

        private static int FindAttachedEvenSprite(
            IReadOnlyList<SpriteFrameCommand> evenSprites,
            bool[] evenAttached,
            SpriteFrameCommand oddSprite)
        {
            var bestIndex = -1;
            var bestDistance = int.MaxValue;
            for (var i = 0; i < evenSprites.Count; i++)
            {
                if (evenAttached[i] ||
                    !IsAttachedSpritePair(evenSprites[i], oddSprite) ||
                    !SpritesOverlapVertically(evenSprites[i].Descriptor, oddSprite.Descriptor))
                {
                    continue;
                }

                var distance = Math.Abs(evenSprites[i].Row - oddSprite.Row);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool SpritesOverlapVertically(SpriteDescriptor left, SpriteDescriptor right)
        {
            return Math.Max(left.YStart, right.YStart) < Math.Min(left.YStop, right.YStop);
        }

        private static bool IsAttachedSpritePair(SpriteFrameCommand evenSprite, SpriteFrameCommand oddSprite)
        {
            return oddSprite.Descriptor.Attached;
        }

        private bool TryGetManualSpriteDescriptor(int spriteIndex, out SpriteDescriptor descriptor)
        {
            var sprite = _sprites[spriteIndex];
            if (!sprite.ManualArmed || (sprite.Pos | sprite.Ctl | sprite.DataA | sprite.DataB) == 0)
            {
                descriptor = default;
                return false;
            }

            descriptor = CreateManualSpriteDescriptor(sprite);
            return true;
        }

        private void CaptureDmaSpriteFrameCommand(int spriteIndex)
        {
            if (!_captureSpriteFrameCommands ||
                _advancingLiveDma ||
                _useTimedPresentationReads ||
                !IsSpriteDmaEnabled() ||
                !IsSpriteDmaChannelAvailable(spriteIndex))
            {
                return;
            }

            var sprite = _sprites[spriteIndex];
            if (!TryGetCurrentOutputRow(out var row))
            {
                row = 0;
            }

            AppendDmaSpriteFrameCommands(_spriteFrameCommands, spriteIndex, sprite.Pointer, row);
        }

        private void CaptureManualSpriteFrameCommandIfArmed(int spriteIndex, long cycle = long.MinValue)
        {
            if (!_captureSpriteFrameCommands && !_advancingLiveDma)
            {
                return;
            }

            var sprite = _sprites[spriteIndex];
            if (!sprite.ManualArmed || (sprite.Pos | sprite.Ctl | sprite.DataA | sprite.DataB) == 0)
            {
                return;
            }

            var descriptor = CreateManualSpriteDescriptor(sprite);
            if (_captureSpriteFrameCommands || _advancingLiveDma)
            {
                AddSpriteFrameCommand(spriteIndex, descriptor, cycle);
                return;
            }
        }

        private void CaptureInitialManualSpriteFrameCommands()
        {
            for (var spriteIndex = 0; spriteIndex < _sprites.Length; spriteIndex++)
            {
                var sprite = _sprites[spriteIndex];
                if (!sprite.ManualArmed || (sprite.Pos | sprite.Ctl | sprite.DataA | sprite.DataB) == 0)
                {
                    continue;
                }

                var command = new SpriteFrameCommand(spriteIndex, 0, CreateManualSpriteDescriptor(sprite));
                AppendUniqueSpriteFrameCommand(_spriteFrameCommands, command);
                _displayTimeline.AddSpriteFrameCommand(command);
            }
        }

        private static SpriteDescriptor CreateManualSpriteDescriptor(SpriteState sprite)
        {
            var baseDescriptor = CreateSpriteDescriptor(sprite.Pos, sprite.Ctl, 0, isDma: false, sprite.DataA, sprite.DataB);
            if (baseDescriptor.YStop <= baseDescriptor.YStart)
            {
                return new SpriteDescriptor(
                    baseDescriptor.X,
                    baseDescriptor.YStart,
                    baseDescriptor.YStart,
                    baseDescriptor.Attached,
                    baseDescriptor.DataAddress,
                    baseDescriptor.IsDma,
                    baseDescriptor.ManualDataA,
                    baseDescriptor.ManualDataB);
            }

            return new SpriteDescriptor(
                baseDescriptor.X,
                baseDescriptor.YStart,
                LowResOutputHeight,
                baseDescriptor.Attached,
                baseDescriptor.DataAddress,
                baseDescriptor.IsDma,
                baseDescriptor.ManualDataA,
                baseDescriptor.ManualDataB);
        }

        private void StopManualSpriteFrameCommands(int spriteIndex, long cycle = long.MinValue)
        {
            if (!_captureSpriteFrameCommands && !_advancingLiveDma)
            {
                return;
            }

            var row = GetCurrentManualSpriteCommandRow(_sprites[spriteIndex], cycle);

            for (var i = _spriteFrameCommands.Count - 1; i >= 0; i--)
            {
                var command = _spriteFrameCommands[i];
                if (command.SpriteIndex != spriteIndex || command.Descriptor.IsDma)
                {
                    continue;
                }

                if (command.Descriptor.YStop <= row)
                {
                    continue;
                }

                var yStop = Math.Max(command.Descriptor.YStart, row);
                _spriteFrameCommands[i] = new SpriteFrameCommand(
                    command.SpriteIndex,
                    command.Row,
                    command.Descriptor.WithYStop(yStop));
            }

            StopTimelineManualSpriteFrameCommands(spriteIndex, row);
        }

        private void AddSpriteFrameCommand(int spriteIndex, SpriteDescriptor descriptor, long cycle = long.MinValue)
        {
            if (_spriteFrameCommands.Count >= MaxSpriteFrameCommands * _sprites.Length)
            {
                return;
            }

            var row = descriptor.IsDma
                ? (TryGetCurrentOutputRow(out var currentRow) ? currentRow : 0)
                : GetCurrentManualSpriteCommandRow(_sprites[spriteIndex], cycle);

            var command = new SpriteFrameCommand(spriteIndex, row, descriptor);
            if (_spriteFrameCommands.Count > 0 &&
                _spriteFrameCommands[_spriteFrameCommands.Count - 1].HasSameRenderingAs(command))
            {
                return;
            }

            _spriteFrameCommands.Add(command);
            RecordTimelineSpriteFrameCommand(command);
        }

        private void RecordTimelineSpriteFrameCommand(SpriteFrameCommand command)
        {
            if (!_advancingLiveDma ||
                !_liveFrameValid ||
                _liveTimelineUnsafeForFrame ||
                !_displayTimeline.IsValidForFrame(_liveFrameStartCycle))
            {
                return;
            }

            _displayTimeline.AddSpriteFrameCommand(command);
        }

        private void StopTimelineManualSpriteFrameCommands(int spriteIndex, int row)
        {
            if (!_advancingLiveDma ||
                !_liveFrameValid ||
                _liveTimelineUnsafeForFrame ||
                !_displayTimeline.IsValidForFrame(_liveFrameStartCycle))
            {
                return;
            }

            _displayTimeline.StopManualSpriteFrameCommands(spriteIndex, row);
        }

        private void RecordTimelineSpriteDataFetch(int row, int spriteIndex, int word, ushort value, bool granted)
        {
            if (!_advancingLiveDma ||
                !_liveFrameValid ||
                _liveTimelineUnsafeForFrame ||
                !_displayTimeline.IsValidForFrame(_liveFrameStartCycle))
            {
                return;
            }

            _displayTimeline.RecordSpriteDataFetch(row, spriteIndex, word, value, granted);
        }

        private void AppendDmaSpriteFrameCommands(
            List<SpriteFrameCommand> commands,
            int spriteIndex,
            uint pointer,
            int row)
        {
            var controlAddress = pointer;
            var controlRow = Math.Clamp(row, 0, LowResOutputHeight - 1);
            var lastVisibleStop = -1;
            for (var controlBlock = 0; controlBlock < 128; controlBlock++)
            {
                if (!TryReadSpriteWordForPresentation(controlAddress, controlRow, spriteIndex, 0, out var pos) ||
                    !TryReadSpriteWordForPresentation(AddDmaPointerOffset(controlAddress, 2), controlRow, spriteIndex, 1, out var ctl))
                {
                    return;
                }

                if ((pos | ctl) == 0)
                {
                    return;
                }

                var descriptor = CreateSpriteDescriptor(
                    pos,
                    ctl,
                    AddDmaPointerOffset(controlAddress, 4),
                    isDma: true,
                    _sprites[spriteIndex].DataA,
                    _sprites[spriteIndex].DataB);
                var rawHeight = Math.Max(0, descriptor.YStop - descriptor.YStart);
                var nextControlAddress = AddDmaPointerOffset(descriptor.DataAddress, rawHeight * 4);

                if (lastVisibleStop >= 0 && descriptor.YStart <= lastVisibleStop)
                {
                    descriptor = descriptor.WithYStart(Math.Min(LowResOutputHeight, lastVisibleStop + 1));
                }

                var height = Math.Max(0, descriptor.YStop - descriptor.YStart);
                if (height == 0)
                {
                    return;
                }

                if (descriptor.YStart >= row)
                {
                    AppendUniqueSpriteFrameCommand(commands, new SpriteFrameCommand(spriteIndex, row, descriptor));
                }

                lastVisibleStop = Math.Max(lastVisibleStop, descriptor.YStop);
                controlRow = Math.Clamp(descriptor.YStop + 1, 0, LowResOutputHeight - 1);
                controlAddress = nextControlAddress;
            }
        }

        private bool IsSpriteDmaEnabled()
        {
            return (_dmacon & (DmaconMasterEnable | DmaconSpriteEnable)) == (DmaconMasterEnable | DmaconSpriteEnable);
        }

        private static bool IsSpriteDmaEnabled(ushort dmacon)
        {
            return (dmacon & (DmaconMasterEnable | DmaconSpriteEnable)) == (DmaconMasterEnable | DmaconSpriteEnable);
        }

        private bool IsSpriteDmaChannelAvailable(int spriteIndex)
        {
            return spriteIndex < GetUsableSpriteDmaChannelCount();
        }

        private bool IsSpriteDmaSlotAvailable(int spriteIndex, int word)
        {
            if (GetAgnusBitplaneFetchPlaneCount() == 0 || !IsBitplaneDmaEnabledForRendering())
            {
                return true;
            }

            var ddfStart = GetDataFetchStartValue();
            if (!IsHighResolutionEnabled() && ddfStart <= 0x0018 && spriteIndex == 0)
            {
                return word == 0;
            }

            return IsSpriteDmaChannelAvailable(spriteIndex);
        }

        private static bool IsSpriteDmaChannelAvailable(DisplayTimelineState state, int spriteIndex)
        {
            return spriteIndex < GetUsableSpriteDmaChannelCount(state);
        }

        private static bool IsSpriteDmaSlotAvailable(DisplayTimelineState state, int spriteIndex, int word)
        {
            if (state.PlaneCount == 0 || !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return true;
            }

            if (!IsHighResolutionEnabled(state.Bplcon0) && state.DataFetchStart <= 0x0018 && spriteIndex == 0)
            {
                return word == 0;
            }

            return IsSpriteDmaChannelAvailable(state, spriteIndex);
        }

        private static bool IsSpriteDmaSlotAvailable(LiveLineState state, int spriteIndex, int word)
        {
            if (state.PlaneCount == 0 || !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return true;
            }

            if (!IsHighResolutionEnabled(state.Bplcon0) &&
                state.DataFetchStart <= 0x0018 &&
                spriteIndex == 0)
            {
                return word == 0;
            }

            return spriteIndex < GetUsableSpriteDmaChannelCount(state);
        }

        private int GetUsableSpriteDmaChannelCount()
        {
            if (((_bplcon0 >> 12) & 0x7) == 0 || !IsBitplaneDmaEnabledForRendering())
            {
                return _sprites.Length;
            }

            var ddfStart = GetDataFetchStartValue();
            var standardStart = IsHighResolutionEnabled() ? DefaultHighResDdfStart : DefaultDdfStart;
            if (ddfStart >= standardStart)
            {
                return _sprites.Length;
            }

            if (ddfStart <= 0x0018)
            {
                return 0;
            }

            if (ddfStart <= 0x001C)
            {
                return 1;
            }

            if (ddfStart >= 0x0030)
            {
                return 7;
            }

            return Math.Clamp(((ddfStart - 0x001C) / 4) + 1, 1, 7);
        }

        private static int GetUsableSpriteDmaChannelCount(DisplayTimelineState state)
        {
            if (state.PlaneCount == 0 || !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return LiveSpriteChannelCount;
            }

            var ddfStart = state.DataFetchStart;
            var standardStart = IsHighResolutionEnabled(state.Bplcon0) ? DefaultHighResDdfStart : DefaultDdfStart;
            if (ddfStart >= standardStart)
            {
                return LiveSpriteChannelCount;
            }

            if (ddfStart <= 0x0018)
            {
                return 0;
            }

            if (ddfStart <= 0x001C)
            {
                return 1;
            }

            if (ddfStart >= 0x0030)
            {
                return 7;
            }

            return Math.Clamp(((ddfStart - 0x001C) / 4) + 1, 1, 7);
        }

        private static int GetUsableSpriteDmaChannelCount(LiveLineState state)
        {
            if (state.PlaneCount == 0 || !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return LiveSpriteChannelCount;
            }

            var ddfStart = state.DataFetchStart;
            var standardStart = IsHighResolutionEnabled(state.Bplcon0) ? DefaultHighResDdfStart : DefaultDdfStart;
            if (ddfStart >= standardStart)
            {
                return LiveSpriteChannelCount;
            }

            if (ddfStart <= 0x0018)
            {
                return 0;
            }

            if (ddfStart <= 0x001C)
            {
                return 1;
            }

            if (ddfStart >= 0x0030)
            {
                return 7;
            }

            return Math.Clamp(((ddfStart - 0x001C) / 4) + 1, 1, 7);
        }

        private bool IsBitplaneDmaEnabledForRendering()
        {
            return !_enforceDmaForFrame || IsBitplaneDmaEnabled(_dmacon);
        }

        private bool IsLiveBitplaneDmaEnabled()
        {
            return GetAgnusBitplaneFetchPlaneCount() > 0 && IsBitplaneDmaEnabled(_dmacon);
        }

        private bool IsCopperDmaEnabled()
        {
            return !_enforceDmaForFrame ||
                (_dmacon & (DmaconMasterEnable | DmaconCopperEnable)) == (DmaconMasterEnable | DmaconCopperEnable);
        }

        private bool IsLiveCopperDmaEnabled()
        {
            return (_dmacon & (DmaconMasterEnable | DmaconCopperEnable)) == (DmaconMasterEnable | DmaconCopperEnable);
        }

        private static SpriteDescriptor CreateSpriteDescriptor(
            ushort pos,
            ushort ctl,
            uint dataAddress,
            bool isDma,
            ushort manualDataA,
            ushort manualDataB)
        {
            var hStart = ((pos & 0x00FF) << 1) | (ctl & 0x0001);
            var vStart = ((pos >> 8) & 0x00FF) | ((ctl & 0x0004) != 0 ? 0x100 : 0);
            var vStop = ((ctl >> 8) & 0x00FF) | ((ctl & 0x0002) != 0 ? 0x100 : 0);
            return new SpriteDescriptor(
                hStart - StandardSpriteHorizontalOffset,
                vStart - StandardVStart,
                vStop - StandardVStart,
                (ctl & 0x0080) != 0,
                dataAddress,
                isDma,
                manualDataA,
                manualDataB);
        }

        private void RenderSprite(Span<uint> bgra, int spriteIndex, SpriteFrameCommand command)
        {
            var sprite = command.Descriptor;
            if (!sprite.IsDma)
            {
                var yStart = Math.Max(sprite.YStart, command.Row);
                var yStop = Math.Min(sprite.YStop, LowResOutputHeight);
                for (var y = yStart; y < yStop; y++)
                {
                    RenderSpriteLine(bgra, spriteIndex, sprite.X, y, sprite.ManualDataA, sprite.ManualDataB);
                }

                return;
            }

            var address = sprite.DataAddress;
            for (var y = sprite.YStart; y < sprite.YStop; y++)
            {
                if (TryReadSpriteWordForPresentation(
                        address,
                        y,
                        spriteIndex,
                        0,
                        out var dataA,
                        recordLiveCapture: false) &&
                    TryReadSpriteWordForPresentation(
                        AddDmaPointerOffset(address, 2),
                        y,
                        spriteIndex,
                        1,
                        out var dataB,
                        recordLiveCapture: false))
                {
                    RenderSpriteLine(bgra, spriteIndex, sprite.X, y, dataA, dataB);
                }

                address = AddDmaPointerOffset(address, 4);
            }
        }

        private void RenderTimelineSprite(
            Span<uint> bgra,
            int spriteIndex,
            SpriteFrameCommand command,
            DisplayFrameTimeline timeline)
        {
            var sprite = command.Descriptor;
            if (!sprite.IsDma)
            {
                var yStart = Math.Max(sprite.YStart, command.Row);
                var yStop = Math.Min(sprite.YStop, LowResOutputHeight);
                for (var y = yStart; y < yStop; y++)
                {
                    RenderSpriteLine(bgra, spriteIndex, sprite.X, y, sprite.ManualDataA, sprite.ManualDataB);
                }

                return;
            }

            for (var y = sprite.YStart; y < sprite.YStop; y++)
            {
                if (TryReadTimelineSpriteLine(command, y, timeline, out var dataA, out var dataB))
                {
                    RenderSpriteLine(bgra, spriteIndex, sprite.X, y, dataA, dataB);
                }
            }
        }

        private void RenderAttachedSpritePair(Span<uint> bgra, int spriteIndex, SpriteFrameCommand evenCommand, SpriteFrameCommand oddCommand)
        {
            var evenSprite = evenCommand.Descriptor;
            var oddSprite = oddCommand.Descriptor;
            var yStart = Math.Min(evenSprite.YStart, oddSprite.YStart);
            var yStop = Math.Max(evenSprite.YStop, oddSprite.YStop);
            for (var y = yStart; y < yStop; y++)
            {
                var evenData = ReadSpriteLine(evenCommand, y);
                var oddData = ReadSpriteLine(oddCommand, y);
                RenderAttachedSpriteLine(
                    bgra,
                    spriteIndex,
                    evenSprite.X,
                    oddSprite.X,
                    y,
                    evenData.DataA,
                    evenData.DataB,
                    oddData.DataA,
                    oddData.DataB);
            }
        }

        private void RenderTimelineAttachedSpritePair(
            Span<uint> bgra,
            int spriteIndex,
            SpriteFrameCommand evenCommand,
            SpriteFrameCommand oddCommand,
            DisplayFrameTimeline timeline)
        {
            var evenSprite = evenCommand.Descriptor;
            var oddSprite = oddCommand.Descriptor;
            var yStart = Math.Min(evenSprite.YStart, oddSprite.YStart);
            var yStop = Math.Max(evenSprite.YStop, oddSprite.YStop);
            for (var y = yStart; y < yStop; y++)
            {
                if (!TryReadTimelineSpriteLine(evenCommand, y, timeline, out var evenDataA, out var evenDataB) ||
                    !TryReadTimelineSpriteLine(oddCommand, y, timeline, out var oddDataA, out var oddDataB))
                {
                    continue;
                }

                RenderAttachedSpriteLine(
                    bgra,
                    spriteIndex,
                    evenSprite.X,
                    oddSprite.X,
                    y,
                    evenDataA,
                    evenDataB,
                    oddDataA,
                    oddDataB);
            }
        }

        private void RenderAttachedOddSpriteWithoutEvenPartner(Span<uint> bgra, int spriteIndex, SpriteFrameCommand oddCommand)
        {
            var oddSprite = oddCommand.Descriptor;
            for (var y = oddSprite.YStart; y < oddSprite.YStop; y++)
            {
                var oddData = ReadSpriteLine(oddCommand, y);
                RenderAttachedSpriteLine(
                    bgra,
                    spriteIndex,
                    oddSprite.X,
                    oddSprite.X,
                    y,
                    0,
                    0,
                    oddData.DataA,
                    oddData.DataB);
            }
        }

        private void RenderTimelineAttachedOddSpriteWithoutEvenPartner(
            Span<uint> bgra,
            int spriteIndex,
            SpriteFrameCommand oddCommand,
            DisplayFrameTimeline timeline)
        {
            var oddSprite = oddCommand.Descriptor;
            for (var y = oddSprite.YStart; y < oddSprite.YStop; y++)
            {
                if (!TryReadTimelineSpriteLine(oddCommand, y, timeline, out var oddDataA, out var oddDataB))
                {
                    continue;
                }

                RenderAttachedSpriteLine(
                    bgra,
                    spriteIndex,
                    oddSprite.X,
                    oddSprite.X,
                    y,
                    0,
                    0,
                    oddDataA,
                    oddDataB);
            }
        }

        private (ushort DataA, ushort DataB) ReadSpriteLine(SpriteFrameCommand command, int y)
        {
            var sprite = command.Descriptor;
            if (y < Math.Max(sprite.YStart, command.Row) || y >= sprite.YStop)
            {
                return ((ushort)0, (ushort)0);
            }

            if (!sprite.IsDma)
            {
                return (sprite.ManualDataA, sprite.ManualDataB);
            }

            var address = AddDmaPointerOffset(sprite.DataAddress, (y - sprite.YStart) * 4);
            if (!TryReadSpriteWordForPresentation(
                    address,
                    y,
                    command.SpriteIndex,
                    0,
                    out var dataA,
                    recordLiveCapture: false) ||
                !TryReadSpriteWordForPresentation(
                    AddDmaPointerOffset(address, 2),
                    y,
                    command.SpriteIndex,
                    1,
                    out var dataB,
                    recordLiveCapture: false))
            {
                return ((ushort)0, (ushort)0);
            }

            return (dataA, dataB);
        }

        private static bool TryReadTimelineSpriteLine(
            SpriteFrameCommand command,
            int y,
            DisplayFrameTimeline timeline,
            out ushort dataA,
            out ushort dataB)
        {
            dataA = 0;
            dataB = 0;
            var sprite = command.Descriptor;
            if (y < Math.Max(sprite.YStart, command.Row) || y >= sprite.YStop)
            {
                return true;
            }

            if (!sprite.IsDma)
            {
                dataA = sprite.ManualDataA;
                dataB = sprite.ManualDataB;
                return true;
            }

            var statusA = timeline.GetSpriteFetchStatus(y, command.SpriteIndex, 0);
            var statusB = timeline.GetSpriteFetchStatus(y, command.SpriteIndex, 1);
            if (statusA == TimelineFetchStatus.NotAttempted || statusB == TimelineFetchStatus.NotAttempted)
            {
                return false;
            }

            var hasPriorDatb = HasPriorTimelineSpriteDatb(timeline, command, y, command.SpriteIndex);
            if (statusB == TimelineFetchStatus.Denied && !hasPriorDatb)
            {
                return true;
            }

            dataA = statusA == TimelineFetchStatus.Granted
                ? timeline.GetSpriteWord(y, command.SpriteIndex, 0)
                : (ushort)0;
            dataB = statusB == TimelineFetchStatus.Granted ||
                (statusB == TimelineFetchStatus.Denied && hasPriorDatb)
                ? timeline.GetSpriteWord(y, command.SpriteIndex, 1)
                : (ushort)0;
            return true;
        }

        private void RenderSpriteLine(Span<uint> bgra, int spriteIndex, int x, int y, ushort dataA, ushort dataB)
        {
            if (y < 0 || y >= LowResOutputHeight)
            {
                return;
            }

            for (var bit = 15; bit >= 0; bit--)
            {
                var pixel = (((dataB >> bit) & 1) << 1) | ((dataA >> bit) & 1);
                if (pixel == 0)
                {
                    continue;
                }

                var px = x + (15 - bit);
                if (px < 0 || px >= AmigaConstants.PalLowResWidth)
                {
                    continue;
                }

                if (!ShouldSpritePixelDrawOverPlayfields(spriteIndex, px, y))
                {
                    continue;
                }

                WriteSpritePixel(bgra, px, y, ConvertSpriteColorIndex(GetSpriteColorIndex(spriteIndex, pixel), px, y));
            }
        }

        private void RenderAttachedSpriteLine(
            Span<uint> bgra,
            int spriteIndex,
            int evenX,
            int oddX,
            int y,
            ushort evenDataA,
            ushort evenDataB,
            ushort oddDataA,
            ushort oddDataB)
        {
            if (y < 0 || y >= LowResOutputHeight)
            {
                return;
            }

            var xStart = Math.Min(evenX, oddX);
            var xStop = Math.Max(evenX, oddX) + 16;
            for (var px = xStart; px < xStop; px++)
            {
                var evenPixel = GetSpritePixelAt(evenDataA, evenDataB, px - evenX);
                var oddPixel = GetSpritePixelAt(oddDataA, oddDataB, px - oddX);
                var pixel = (oddPixel << 2) | evenPixel;
                if (pixel == 0)
                {
                    continue;
                }

                if (px < 0 || px >= AmigaConstants.PalLowResWidth)
                {
                    continue;
                }

                if (!ShouldSpritePixelDrawOverPlayfields(spriteIndex, px, y))
                {
                    continue;
                }

                WriteSpritePixel(bgra, px, y, ConvertSpriteColorIndex(16 + pixel, px, y));
            }
        }

        private static int GetSpritePixelAt(ushort dataA, ushort dataB, int offset)
        {
            if ((uint)offset >= 16)
            {
                return 0;
            }

            var bit = 15 - offset;
            return (((dataB >> bit) & 1) << 1) | ((dataA >> bit) & 1);
        }

        private void WriteSpritePixel(Span<uint> bgra, int x, int y, uint pixel)
        {
            _lastSpriteNonZeroPixels++;
            _lastSpriteMinX = Math.Min(_lastSpriteMinX, x);
            _lastSpriteMinY = Math.Min(_lastSpriteMinY, y);
            _lastSpriteMaxX = Math.Max(_lastSpriteMaxX, x);
            _lastSpriteMaxY = Math.Max(_lastSpriteMaxY, y);
            WriteLowResolutionOutputPixel(bgra, x, y, pixel);
        }

        private bool ShouldSpritePixelDrawOverPlayfields(int spriteIndex, int x, int y)
        {
            if ((uint)x >= (uint)AmigaConstants.PalLowResWidth || (uint)y >= (uint)LowResOutputHeight)
            {
                return false;
            }

            if (!IsSpritePixelInsideDisplayWindow(x, y))
            {
                return false;
            }

            if (!IsSpritePastDeniseOutputEnable(x, y))
            {
                return false;
            }

            var mask = _playfieldPriorityMasks[(y * AmigaConstants.PalLowResWidth) + x];
            if (mask == 0)
            {
                return true;
            }

            var bplcon2 = GetSpritePriorityRegister(x, y);
            var spriteGroup = spriteIndex / 2;
            if ((mask & NormalPlayfieldPriorityMask) != 0)
            {
                return spriteGroup < GetNormalPlayfieldPriorityPlacement(bplcon2);
            }

            if ((mask & Playfield1PriorityMask) != 0 &&
                spriteGroup >= GetPlayfield1PriorityPlacement(bplcon2))
            {
                return false;
            }

            if ((mask & Playfield2PriorityMask) != 0 &&
                spriteGroup >= GetPlayfield2PriorityPlacement(bplcon2))
            {
                return false;
            }

            return true;
        }

        private bool IsSpritePixelInsideDisplayWindow(int x, int y)
        {
            var window = GetSpriteDisplayWindow(x, y);
            return x >= window.X &&
                x < window.X + window.Width &&
                y >= window.Y &&
                y < window.Y + window.Height;
        }

        private bool IsSpritePastDeniseOutputEnable(int x, int y)
        {
            if (GetRequestedBitplaneCount() <= 0)
            {
                return true;
            }

            var dataFetchStartX = GetDataFetchStartX(GetSpriteDisplayWindow(x, y));
            if (x >= dataFetchStartX)
            {
                return true;
            }

            for (var i = _bitplaneDataSpans.Count - 1; i >= 0; i--)
            {
                var span = _bitplaneDataSpans[i];
                if (span.Row == y && span.XStart <= x)
                {
                    return true;
                }
            }

            return false;
        }

        private DisplayWindow GetSpriteDisplayWindow(int x, int y)
        {
            for (var i = _paletteFrameSpans.Count - 1; i >= 0; i--)
            {
                var span = _paletteFrameSpans[i];
                if (span.Contains(x, y))
                {
                    return span.Window;
                }
            }

            return GetDisplayWindow();
        }

        private ushort GetSpritePriorityRegister(int x, int y)
        {
            for (var i = _paletteFrameSpans.Count - 1; i >= 0; i--)
            {
                var span = _paletteFrameSpans[i];
                if (span.Contains(x, y))
                {
                    return span.Bplcon2;
                }
            }

            return _bplcon2;
        }

        private static int GetPlayfield1PriorityPlacement(ushort bplcon2)
        {
            return Math.Min(bplcon2 & 0x0007, 4);
        }

        private static int GetPlayfield2PriorityPlacement(ushort bplcon2)
        {
            return Math.Min((bplcon2 >> 3) & 0x0007, 4);
        }

        private static int GetNormalPlayfieldPriorityPlacement(ushort bplcon2)
        {
            return GetPlayfield2PriorityPlacement(bplcon2);
        }

        private static int GetSpriteColorIndex(int spriteIndex, int pixel)
        {
            return 16 + ((spriteIndex / 2) * 4) + pixel;
        }

        private uint ConvertSpriteColorIndex(int colorIndex, int x, int y)
        {
            for (var i = _paletteFrameSpans.Count - 1; i >= 0; i--)
            {
                var span = _paletteFrameSpans[i];
                if (span.Contains(x, y) && (uint)colorIndex < PaletteColorCount)
                {
                    return _paletteFrameSpanColors[span.ColorOffset + colorIndex];
                }
            }

            return ConvertColorIndex(colorIndex);
        }

        private static uint ConvertColor(ushort amigaColor)
        {
            var r = (uint)(((amigaColor >> 8) & 0x0F) * 17);
            var g = (uint)(((amigaColor >> 4) & 0x0F) * 17);
            var b = (uint)((amigaColor & 0x0F) * 17);
            return 0xFF00_0000u | (r << 16) | (g << 8) | b;
        }

        private static uint AveragePixels(uint left, uint right)
        {
            if (left == right)
            {
                return left;
            }

            var r = (((left >> 16) & 0xFF) + ((right >> 16) & 0xFF)) >> 1;
            var g = (((left >> 8) & 0xFF) + ((right >> 8) & 0xFF)) >> 1;
            var b = ((left & 0xFF) + (right & 0xFF)) >> 1;
            return 0xFF00_0000u | (r << 16) | (g << 8) | b;
        }

        private uint ConvertColorIndex(int colorIndex)
        {
            if ((uint)colorIndex < (uint)_convertedColors.Length)
            {
                return _convertedColors[colorIndex];
            }

            var baseColor = _colors[colorIndex & 0x1F];
            var r = (uint)((((baseColor >> 8) & 0x0F) * 17) / 2);
            var g = (uint)((((baseColor >> 4) & 0x0F) * 17) / 2);
            var b = (uint)(((baseColor & 0x0F) * 17) / 2);
            return 0xFF00_0000u | (r << 16) | (g << 8) | b;
        }

        private void UpdateConvertedPalette()
        {
            for (var colorIndex = 0; colorIndex < _colors.Length; colorIndex++)
            {
                UpdateConvertedColor(colorIndex);
            }
        }

        private void UpdateConvertedColor(int colorIndex)
        {
            var color = _colors[colorIndex];
            _convertedColors[colorIndex] = ConvertColor(color);
            var r = (uint)((((color >> 8) & 0x0F) * 17) / 2);
            var g = (uint)((((color >> 4) & 0x0F) * 17) / 2);
            var b = (uint)(((color & 0x0F) * 17) / 2);
            _convertedColors[32 + colorIndex] = 0xFF00_0000u | (r << 16) | (g << 8) | b;
        }

        private bool IsRenderingHighResolutionWidth()
        {
            return _renderWidth >= AmigaConstants.PalHighResWidth;
        }

        private bool IsRenderingHighResolutionHeight()
        {
            return _renderHeight >= AmigaConstants.PalHighResHeight;
        }

        private OutputRows EnumerateOutputRows(int y)
        {
            if (!IsRenderingHighResolutionHeight())
            {
                return new OutputRows(y, y);
            }

            var first = (y * 2) + (InterlaceEnabled ? _renderInterlaceField : 0);
            var second = InterlaceEnabled ? first : first + 1;
            return new OutputRows(first, second);
        }

        private void WriteLowResolutionOutputPixel(Span<uint> bgra, int x, int y, uint pixel)
        {
            WriteLowResolutionOutputPixel(
                bgra,
                x,
                y,
                pixel,
                IsRenderingHighResolutionWidth(),
                IsRenderingHighResolutionHeight(),
                InterlaceEnabled,
                _renderInterlaceField);
        }

        private void WriteLowResolutionOutputPixel(
            Span<uint> bgra,
            int x,
            int y,
            uint pixel,
            bool highResolutionWidth,
            bool highResolutionHeight,
            bool interlace,
            int interlaceField)
        {
            if (!highResolutionHeight)
            {
                if (highResolutionWidth)
                {
                    var offset = (y * _renderWidth) + (x * 2);
                    bgra[offset] = pixel;
                    bgra[offset + 1] = pixel;
                }
                else
                {
                    bgra[(y * _renderWidth) + x] = pixel;
                }

                return;
            }

            var firstOutputY = (y * 2) + (interlace ? interlaceField : 0);
            WriteLowResolutionOutputPixelRow(bgra, x, firstOutputY, pixel, highResolutionWidth);
            if (!interlace)
            {
                WriteLowResolutionOutputPixelRow(bgra, x, firstOutputY + 1, pixel, highResolutionWidth);
            }
        }

        private void WriteHighResolutionOutputPixelPair(Span<uint> bgra, int x, int y, uint left, uint right)
        {
            WriteHighResolutionOutputPixelPair(
                bgra,
                x,
                y,
                left,
                right,
                IsRenderingHighResolutionWidth(),
                IsRenderingHighResolutionHeight(),
                InterlaceEnabled,
                _renderInterlaceField);
        }

        private void WriteHighResolutionOutputPixelPair(
            Span<uint> bgra,
            int x,
            int y,
            uint left,
            uint right,
            bool highResolutionWidth,
            bool highResolutionHeight,
            bool interlace,
            int interlaceField)
        {
            if (!highResolutionHeight)
            {
                if (highResolutionWidth)
                {
                    var offset = (y * _renderWidth) + (x * 2);
                    bgra[offset] = left;
                    bgra[offset + 1] = right;
                }
                else
                {
                    bgra[(y * _renderWidth) + x] = AveragePixels(left, right);
                }

                return;
            }

            var firstOutputY = (y * 2) + (interlace ? interlaceField : 0);
            WriteHighResolutionOutputPixelRow(bgra, x, firstOutputY, left, right, highResolutionWidth);
            if (!interlace)
            {
                WriteHighResolutionOutputPixelRow(bgra, x, firstOutputY + 1, left, right, highResolutionWidth);
            }
        }

        private void WriteLowResolutionOutputPixelRow(Span<uint> bgra, int x, int outputY, uint pixel, bool highResolutionWidth)
        {
            if (highResolutionWidth)
            {
                var offset = (outputY * _renderWidth) + (x * 2);
                bgra[offset] = pixel;
                bgra[offset + 1] = pixel;
            }
            else
            {
                bgra[(outputY * _renderWidth) + x] = pixel;
            }
        }

        private void WriteHighResolutionOutputPixelRow(
            Span<uint> bgra,
            int x,
            int outputY,
            uint left,
            uint right,
            bool highResolutionWidth)
        {
            if (highResolutionWidth)
            {
                var offset = (outputY * _renderWidth) + (x * 2);
                bgra[offset] = left;
                bgra[offset + 1] = right;
            }
            else
            {
                bgra[(outputY * _renderWidth) + x] = AveragePixels(left, right);
            }
        }

        private uint ConvertHamPixel(int colorIndex, ref ushort previousColor)
        {
            var data = (ushort)(colorIndex & 0x0F);
            switch ((colorIndex >> 4) & 0x03)
            {
                case 0:
                    previousColor = _colors[data];
                    break;
                case 1:
                    previousColor = (ushort)((previousColor & 0x0FF0) | data);
                    break;
                case 2:
                    previousColor = (ushort)((previousColor & 0x00FF) | (data << 8));
                    break;
                case 3:
                    previousColor = (ushort)((previousColor & 0x0F0F) | (data << 4));
                    break;
            }

            return ConvertColor(previousColor);
        }


    }
}
