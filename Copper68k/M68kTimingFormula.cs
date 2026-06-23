using System;

namespace Copper68k
{
    internal enum M68kTimingOperation
    {
        Legacy,
        Idle,
        Nop,
        Exception,
        Control,
        StatusImmediate,
        Return,
        Link,
        Extend,
        Test,
        Moveq,
        Negate,
        Not,
        Clear,
        LoadEffectiveAddress,
        Move,
        Arithmetic,
        AddressArithmetic,
        Multiply,
        Divide,
        Bcd,
        Compare,
        Swap,
        ShiftRotate,
        Jump,
        Subroutine,
        Movem,
        Bit,
        SetCondition,
        Branch,
        DecrementBranch
    }

    internal enum M68kTimingOperand
    {
        None,
        DataRegister,
        AddressRegister,
        AddressIndirect,
        PostIncrement,
        Predecrement,
        AddressDisplacement,
        BriefIndexed,
        AbsoluteWord,
        AbsoluteLong,
        Immediate,
        RegisterList,
        ConditionCodeRegister,
        StatusRegister,
        ControlRegister
    }

    internal enum M68kTimingOutcome
    {
        None,
        Taken,
        NotTaken,
        ConditionTrue,
        Expired
    }

    internal readonly record struct M68kTimingDescriptor(
        M68kInstructionTimingKey Key,
        M68kTimingOperation Operation,
        M68kOperandSize Size,
        M68kTimingOperand Source,
        M68kTimingOperand Destination,
        M68kTimingOutcome Outcome,
        int RegisterCount,
        bool RegisterToMemory,
        string Name,
        int NativeCycles,
        M68kTimingBarrier Barriers)
    {
        public static M68kTimingDescriptor FromLegacyKey(M68kInstructionTimingKey key)
            => M68kTimingDescriptorCatalog.FromLegacyKey(key);

        public static M68kTimingDescriptor MovemLong(
            M68kInstructionTimingKey key,
            string name,
            int registerCount,
            bool registerToMemory)
        {
            if (registerCount < 0 || registerCount > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(registerCount));
            }

            return new M68kTimingDescriptor(
                key,
                M68kTimingOperation.Movem,
                M68kOperandSize.Long,
                registerToMemory ? M68kTimingOperand.RegisterList : M68kTimingOperand.PostIncrement,
                registerToMemory ? M68kTimingOperand.Predecrement : M68kTimingOperand.RegisterList,
                M68kTimingOutcome.None,
                registerCount,
                registerToMemory,
                name,
                NativeCycles: 0,
                M68kTimingBarrier.None);
        }
    }

    internal static class M68kTimingFormula
    {
        public static M68kInstructionPlan GetPlan(M68kTimingDescriptor descriptor, M68kAcceleratorModel model)
        {
            var nativeCycles = GetNativeCycles(descriptor, model);
            var barriers = GetBarriers(descriptor, model);
            if (model == M68kAcceleratorModel.M68020 ||
                descriptor.Operation == M68kTimingOperation.Movem && model == M68kAcceleratorModel.M68040)
            {
                return M68kInstructionPlan.CreateFlat(descriptor.Key, descriptor.Name, nativeCycles, barriers);
            }

            var (headCycles, tailCycles) = GetHeadTailCycles(descriptor, barriers);
            return M68kInstructionPlan.CreateHeadTail(
                descriptor.Key,
                descriptor.Name,
                nativeCycles,
                headCycles,
                tailCycles,
                barriers);
        }

        private static int GetNativeCycles(M68kTimingDescriptor descriptor, M68kAcceleratorModel model)
        {
            if (descriptor.Operation != M68kTimingOperation.Movem)
            {
                return descriptor.NativeCycles;
            }

            const int registerListImmediateAddressCycles = 4;
            if (model == M68kAcceleratorModel.M68030)
            {
                return descriptor.RegisterToMemory
                    ? 4 + (2 * descriptor.RegisterCount) + registerListImmediateAddressCycles
                    : 8 + (4 * descriptor.RegisterCount) + registerListImmediateAddressCycles;
            }

            return descriptor.RegisterToMemory
                ? 4 + (3 * descriptor.RegisterCount) + registerListImmediateAddressCycles
                : 8 + (4 * descriptor.RegisterCount) + registerListImmediateAddressCycles;
        }

        private static M68kTimingBarrier GetBarriers(M68kTimingDescriptor descriptor, M68kAcceleratorModel model)
        {
            var barriers = descriptor.Barriers;
            if (descriptor.Key == M68kInstructionTimingKey.Movec &&
                model is M68kAcceleratorModel.M68030 or M68kAcceleratorModel.M68040)
            {
                barriers |= M68kTimingBarrier.SynchronizeBus;
            }

            return barriers;
        }

        private static (int HeadCycles, int TailCycles) GetHeadTailCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingBarrier barriers)
        {
            if ((barriers & (M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Exception | M68kTimingBarrier.Branch | M68kTimingBarrier.SynchronizeBus | M68kTimingBarrier.CacheControl)) != 0 ||
                descriptor.Operation is M68kTimingOperation.Idle or M68kTimingOperation.Nop)
            {
                return (0, 0);
            }

            if (descriptor.Operation == M68kTimingOperation.Movem)
            {
                return (2, 0);
            }

            if (descriptor.Key is M68kInstructionTimingKey.LinkLong or M68kInstructionTimingKey.ExtbLong)
            {
                return (2, 2);
            }

            return (1, 1);
        }
    }

    internal static class M68kTimingDescriptorCatalog
    {
        private static readonly M68kTimingDescriptor[] LegacyDescriptors = new M68kTimingDescriptor[Enum.GetValues<M68kInstructionTimingKey>().Length];
        private static readonly bool[] LegacyDescriptorSupported = InitializeLegacyDescriptors();

        public static M68kTimingDescriptor FromLegacyKey(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)LegacyDescriptors.Length && LegacyDescriptorSupported[index])
            {
                return LegacyDescriptors[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68020);
        }

        private static bool[] InitializeLegacyDescriptors()
        {
            var supported = new bool[LegacyDescriptors.Length];
            foreach (var key in Enum.GetValues<M68kInstructionTimingKey>())
            {
                if (key is M68kInstructionTimingKey.MovemLongRegistersToPredecrement or
                    M68kInstructionTimingKey.MovemLongPostIncrementToRegisters)
                {
                    continue;
                }

                var index = (int)key;
                LegacyDescriptors[index] = CreateLegacyDescriptor(key);
                supported[index] = true;
            }

            return supported;
        }

        private static M68kTimingDescriptor CreateLegacyDescriptor(M68kInstructionTimingKey key)
        {
            var baseline = M68020TimingModel.GetLegacyFlatPlan(key);
            return new M68kTimingDescriptor(
                key,
                GetOperation(key),
                GetSize(key),
                GetSourceOperand(key),
                GetDestinationOperand(key),
                GetOutcome(key),
                RegisterCount: 0,
                RegisterToMemory: false,
                baseline.Name,
                baseline.NativeCycles,
                baseline.Barriers);
        }

        private static M68kTimingOperation GetOperation(M68kInstructionTimingKey key)
        {
            return key switch
            {
                M68kInstructionTimingKey.Idle => M68kTimingOperation.Idle,
                M68kInstructionTimingKey.Nop => M68kTimingOperation.Nop,
                >= M68kInstructionTimingKey.LineAException and <= M68kInstructionTimingKey.InterruptAcknowledge => M68kTimingOperation.Exception,
                M68kInstructionTimingKey.Movec => M68kTimingOperation.Control,
                M68kInstructionTimingKey.ImmediateWordToConditionCodeRegister or
                M68kInstructionTimingKey.ImmediateWordToStatusRegister => M68kTimingOperation.StatusImmediate,
                M68kInstructionTimingKey.Rte or
                M68kInstructionTimingKey.Rtd or
                M68kInstructionTimingKey.Rts => M68kTimingOperation.Return,
                M68kInstructionTimingKey.LinkLong => M68kTimingOperation.Link,
                M68kInstructionTimingKey.ExtbLong or
                M68kInstructionTimingKey.ExtWordData => M68kTimingOperation.Extend,
                M68kInstructionTimingKey.TstWordData => M68kTimingOperation.Test,
                M68kInstructionTimingKey.Moveq => M68kTimingOperation.Moveq,
                M68kInstructionTimingKey.NegLongData => M68kTimingOperation.Negate,
                M68kInstructionTimingKey.NotByteData => M68kTimingOperation.Not,
                >= M68kInstructionTimingKey.ClrDataLong and <= M68kInstructionTimingKey.ClrLongPostIncrement => M68kTimingOperation.Clear,
                M68kInstructionTimingKey.LeaAbsoluteLong or
                M68kInstructionTimingKey.LeaAddressDisplacement or
                M68kInstructionTimingKey.LeaAbsoluteWord => M68kTimingOperation.LoadEffectiveAddress,
                >= M68kInstructionTimingKey.MoveByteImmediateToAbsoluteLong and <= M68kInstructionTimingKey.MoveWordAbsoluteLongToAddressDisplacement => M68kTimingOperation.Move,
                >= M68kInstructionTimingKey.AddiByteImmediateToData and <= M68kInstructionTimingKey.SubaLongImmediateToAddress => GetArithmeticOperation(key),
                >= M68kInstructionTimingKey.DivuWordImmediateToData and <= M68kInstructionTimingKey.DivsWordImmediateToData => M68kTimingOperation.Divide,
                M68kInstructionTimingKey.MuluLong or
                M68kInstructionTimingKey.MulsLong or
                M68kInstructionTimingKey.MuluWordImmediateToData => M68kTimingOperation.Multiply,
                M68kInstructionTimingKey.DivuLong or
                M68kInstructionTimingKey.DivsLong => M68kTimingOperation.Divide,
                >= M68kInstructionTimingKey.AbcdByteDataToData and <= M68kInstructionTimingKey.SbcdBytePredecrementMemory => M68kTimingOperation.Bcd,
                >= M68kInstructionTimingKey.NbcdByteData and <= M68kInstructionTimingKey.NbcdByteAbsoluteLong => M68kTimingOperation.Bcd,
                M68kInstructionTimingKey.AndByteDataToData or
                M68kInstructionTimingKey.AndWordImmediateToData or
                M68kInstructionTimingKey.AndLongImmediateToData or
                M68kInstructionTimingKey.EoriWordImmediateToData or
                M68kInstructionTimingKey.EoriLongImmediateToData or
                M68kInstructionTimingKey.OriByteImmediateToData or
                M68kInstructionTimingKey.ImmediateLogicalByteToAbsoluteLong => M68kTimingOperation.Arithmetic,
                >= M68kInstructionTimingKey.CmpiLongImmediateToData and <= M68kInstructionTimingKey.CmpWordAddressDisplacementToData => M68kTimingOperation.Compare,
                M68kInstructionTimingKey.SwapData => M68kTimingOperation.Swap,
                >= M68kInstructionTimingKey.AsrLongImmediateData and <= M68kInstructionTimingKey.RolWordImmediateData => M68kTimingOperation.ShiftRotate,
                M68kInstructionTimingKey.JmpAddressIndirect or
                M68kInstructionTimingKey.JmpAbsoluteLong => M68kTimingOperation.Jump,
                M68kInstructionTimingKey.JsrAbsoluteLong => M68kTimingOperation.Subroutine,
                >= M68kInstructionTimingKey.BtstByteImmediateAbsoluteLong and <= M68kInstructionTimingKey.BclrDynamicData => M68kTimingOperation.Bit,
                M68kInstructionTimingKey.SccAbsoluteLong => M68kTimingOperation.SetCondition,
                M68kInstructionTimingKey.BranchByteTaken or
                M68kInstructionTimingKey.BranchByteNotTaken or
                M68kInstructionTimingKey.BranchWordTaken or
                M68kInstructionTimingKey.BranchWordNotTaken or
                M68kInstructionTimingKey.BranchLongTaken or
                M68kInstructionTimingKey.BranchLongNotTaken => M68kTimingOperation.Branch,
                M68kInstructionTimingKey.BsrByte or
                M68kInstructionTimingKey.BsrWord or
                M68kInstructionTimingKey.BsrLong => M68kTimingOperation.Subroutine,
                >= M68kInstructionTimingKey.DbccConditionTrue and <= M68kInstructionTimingKey.DbccExpired => M68kTimingOperation.DecrementBranch,
                _ => M68kTimingOperation.Legacy
            };
        }

        private static M68kTimingOperation GetArithmeticOperation(M68kInstructionTimingKey key)
        {
            return key switch
            {
                M68kInstructionTimingKey.AddaLongImmediateToAddress or
                M68kInstructionTimingKey.AddaLongDataToAddress or
                M68kInstructionTimingKey.AddaLongAddressDisplacementToAddress or
                M68kInstructionTimingKey.SubaLongImmediateToAddress => M68kTimingOperation.AddressArithmetic,
                _ => M68kTimingOperation.Arithmetic
            };
        }

        private static M68kOperandSize GetSize(M68kInstructionTimingKey key)
        {
            return key switch
            {
                M68kInstructionTimingKey.NotByteData or
                M68kInstructionTimingKey.ClrByteAddressIndirect or
                M68kInstructionTimingKey.ClrByteAddressDisplacement or
                M68kInstructionTimingKey.MoveByteImmediateToAbsoluteLong or
                >= M68kInstructionTimingKey.MoveByteDataToData and <= M68kInstructionTimingKey.MoveByteAbsoluteLongToAbsoluteLong or
                M68kInstructionTimingKey.ImmediateLogicalByteToAbsoluteLong or
                M68kInstructionTimingKey.AddiByteImmediateToData or
                M68kInstructionTimingKey.AddiByteImmediateToAddressIndirect or
                M68kInstructionTimingKey.AddiByteImmediateToAddressDisplacement or
                M68kInstructionTimingKey.SubiByteImmediateToData or
                M68kInstructionTimingKey.SubiByteImmediateToAddressDisplacement or
                M68kInstructionTimingKey.SubByteDataToData or
                >= M68kInstructionTimingKey.AbcdByteDataToData and <= M68kInstructionTimingKey.NbcdByteAbsoluteLong or
                M68kInstructionTimingKey.AndByteDataToData or
                >= M68kInstructionTimingKey.CmpiByteImmediateToData and <= M68kInstructionTimingKey.CmpiByteImmediateToAddressDisplacement or
                >= M68kInstructionTimingKey.CmpByteDataToData and <= M68kInstructionTimingKey.CmpByteAbsoluteLongToData or
                M68kInstructionTimingKey.RorByteImmediateData or
                M68kInstructionTimingKey.OriByteImmediateToData or
                >= M68kInstructionTimingKey.BtstByteImmediateAbsoluteLong and <= M68kInstructionTimingKey.BsetByteImmediateAddressDisplacement or
                M68kInstructionTimingKey.BranchByteTaken or
                M68kInstructionTimingKey.BranchByteNotTaken or
                M68kInstructionTimingKey.BsrByte => M68kOperandSize.Byte,

                M68kInstructionTimingKey.ExtWordData or
                M68kInstructionTimingKey.TstWordData or
                M68kInstructionTimingKey.ClrDataWord or
                M68kInstructionTimingKey.ClrWordAddressDisplacement or
                M68kInstructionTimingKey.MoveWordImmediateToAbsoluteLong or
                >= M68kInstructionTimingKey.MoveWordAbsoluteLongToData and <= M68kInstructionTimingKey.MoveWordAbsoluteLongToAddressDisplacement or
                M68kInstructionTimingKey.AddiWordImmediateToData or
                M68kInstructionTimingKey.AddWordDataToData or
                M68kInstructionTimingKey.AddWordDataToAddressDisplacement or
                M68kInstructionTimingKey.DivuWordImmediateToData or
                M68kInstructionTimingKey.DivsWordImmediateToData or
                M68kInstructionTimingKey.AndWordImmediateToData or
                M68kInstructionTimingKey.MuluWordImmediateToData or
                M68kInstructionTimingKey.EoriWordImmediateToData or
                >= M68kInstructionTimingKey.CmpiWordImmediateToData and <= M68kInstructionTimingKey.CmpiWordImmediateToAbsoluteLong or
                M68kInstructionTimingKey.CmpWordDataToData or
                M68kInstructionTimingKey.CmpWordAddressDisplacementToData or
                M68kInstructionTimingKey.LeaAbsoluteWord or
                M68kInstructionTimingKey.SwapData or
                M68kInstructionTimingKey.AsrWordImmediateData or
                M68kInstructionTimingKey.AslWordImmediateData or
                M68kInstructionTimingKey.RorWordImmediateData or
                M68kInstructionTimingKey.RolWordImmediateData or
                M68kInstructionTimingKey.BranchWordTaken or
                M68kInstructionTimingKey.BranchWordNotTaken or
                M68kInstructionTimingKey.BsrWord or
                >= M68kInstructionTimingKey.DbccConditionTrue and <= M68kInstructionTimingKey.DbccExpired => M68kOperandSize.Word,

                _ => M68kOperandSize.Long
            };
        }

        private static M68kTimingOperand GetSourceOperand(M68kInstructionTimingKey key)
        {
            return key switch
            {
                M68kInstructionTimingKey.Movec => M68kTimingOperand.ControlRegister,
                M68kInstructionTimingKey.ImmediateWordToConditionCodeRegister or
                M68kInstructionTimingKey.ImmediateWordToStatusRegister or
                M68kInstructionTimingKey.Moveq or
                M68kInstructionTimingKey.MoveByteImmediateToAbsoluteLong or
                M68kInstructionTimingKey.MoveWordImmediateToAbsoluteLong or
                >= M68kInstructionTimingKey.MoveLongImmediateToAbsoluteLong and <= M68kInstructionTimingKey.MoveLongImmediateToAddress or
                M68kInstructionTimingKey.MoveByteImmediateToData or
                M68kInstructionTimingKey.MoveByteImmediateToAddressIndirect or
                M68kInstructionTimingKey.MoveByteImmediateToAddressDisplacement or
                M68kInstructionTimingKey.MoveByteImmediateToBriefIndexed or
                M68kInstructionTimingKey.MoveWordImmediateToData or
                M68kInstructionTimingKey.MoveWordImmediateToAddressDisplacement or
                >= M68kInstructionTimingKey.AddiByteImmediateToData and <= M68kInstructionTimingKey.SubaLongImmediateToAddress or
                M68kInstructionTimingKey.AndWordImmediateToData or
                M68kInstructionTimingKey.AndLongImmediateToData or
                M68kInstructionTimingKey.EoriWordImmediateToData or
                M68kInstructionTimingKey.EoriLongImmediateToData or
                M68kInstructionTimingKey.OriByteImmediateToData or
                >= M68kInstructionTimingKey.CmpiLongImmediateToData and <= M68kInstructionTimingKey.CmpiWordImmediateToAbsoluteLong or
                >= M68kInstructionTimingKey.BtstByteImmediateAbsoluteLong and <= M68kInstructionTimingKey.BsetByteImmediateAddressDisplacement or
                M68kInstructionTimingKey.BtstImmediateData or
                M68kInstructionTimingKey.BclrImmediateData or
                M68kInstructionTimingKey.BsetImmediateData => M68kTimingOperand.Immediate,

                M68kInstructionTimingKey.MoveLongDataToData or
                M68kInstructionTimingKey.MoveLongDataToAddress or
                M68kInstructionTimingKey.MoveLongDataToAddressIndirect or
                M68kInstructionTimingKey.MoveLongDataToAddressDisplacement or
                M68kInstructionTimingKey.MoveLongDataToAbsoluteLong or
                M68kInstructionTimingKey.MoveByteDataToData or
                M68kInstructionTimingKey.MoveByteDataToAbsoluteLong or
                M68kInstructionTimingKey.MoveByteDataToAddressIndirect or
                M68kInstructionTimingKey.MoveByteDataToAddressDisplacement or
                M68kInstructionTimingKey.MoveByteDataToBriefIndexed or
                M68kInstructionTimingKey.MoveByteDataToPostIncrement or
                M68kInstructionTimingKey.MoveByteDataToPredecrement or
                M68kInstructionTimingKey.MoveWordDataToAddressDisplacement or
                M68kInstructionTimingKey.MoveWordDataToAbsoluteLong or
                M68kInstructionTimingKey.AddaLongDataToAddress or
                M68kInstructionTimingKey.CmpaLongDataToAddress => M68kTimingOperand.DataRegister,

                M68kInstructionTimingKey.MoveLongAddressToData or
                M68kInstructionTimingKey.MoveLongAddressToAddress or
                M68kInstructionTimingKey.MoveLongAddressToAddressIndirect or
                M68kInstructionTimingKey.MoveLongAddressToAddressDisplacement or
                M68kInstructionTimingKey.MoveLongAddressToPostIncrement or
                M68kInstructionTimingKey.MoveLongAddressToAbsoluteLong or
                M68kInstructionTimingKey.SubLongAddressToData or
                M68kInstructionTimingKey.CmpaLongAddressToAddress or
                M68kInstructionTimingKey.CmpLongAddressToData => M68kTimingOperand.AddressRegister,

                M68kInstructionTimingKey.MoveLongAddressIndirectToData or
                M68kInstructionTimingKey.MoveLongAddressIndirectToAddress or
                M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect or
                M68kInstructionTimingKey.MoveByteAddressIndirectToData or
                M68kInstructionTimingKey.MoveByteAddressIndirectToAbsoluteLong or
                M68kInstructionTimingKey.CmpaLongAddressIndirectToAddress or
                M68kInstructionTimingKey.CmpLongAddressIndirectToData or
                M68kInstructionTimingKey.CmpByteAddressIndirectToData => M68kTimingOperand.AddressIndirect,

                M68kInstructionTimingKey.MoveLongPostIncrementToData or
                M68kInstructionTimingKey.MoveLongPostIncrementToAddress or
                M68kInstructionTimingKey.MoveBytePostIncrementToData or
                M68kInstructionTimingKey.MoveBytePostIncrementToPostIncrement or
                M68kInstructionTimingKey.AddLongPostIncrementToData or
                M68kInstructionTimingKey.CmpLongPostIncrementToData => M68kTimingOperand.PostIncrement,

                M68kInstructionTimingKey.MoveLongAddressDisplacementToData or
                M68kInstructionTimingKey.MoveLongAddressDisplacementToAddress or
                M68kInstructionTimingKey.MoveLongAddressDisplacementToPostIncrement or
                M68kInstructionTimingKey.MoveByteAddressDisplacementToData or
                M68kInstructionTimingKey.MoveWordAddressDisplacementToData or
                M68kInstructionTimingKey.SubLongAddressDisplacementToData or
                M68kInstructionTimingKey.AddaLongAddressDisplacementToAddress or
                M68kInstructionTimingKey.CmpByteAddressDisplacementToData or
                M68kInstructionTimingKey.CmpWordAddressDisplacementToData => M68kTimingOperand.AddressDisplacement,

                M68kInstructionTimingKey.MoveLongBriefIndexedToData or
                M68kInstructionTimingKey.MoveLongBriefIndexedToAddress or
                M68kInstructionTimingKey.MoveByteBriefIndexedToData or
                M68kInstructionTimingKey.MoveByteBriefIndexedToPredecrement => M68kTimingOperand.BriefIndexed,

                M68kInstructionTimingKey.LeaAbsoluteWord or
                M68kInstructionTimingKey.MoveLongAbsoluteWordToAddressDisplacement => M68kTimingOperand.AbsoluteWord,

                M68kInstructionTimingKey.LeaAbsoluteLong or
                M68kInstructionTimingKey.MoveLongAbsoluteLongToData or
                M68kInstructionTimingKey.MoveLongAbsoluteLongToAddressDisplacement or
                M68kInstructionTimingKey.MoveByteAbsoluteLongToData or
                M68kInstructionTimingKey.MoveByteAbsoluteLongToAbsoluteLong or
                M68kInstructionTimingKey.MoveWordAbsoluteLongToData or
                M68kInstructionTimingKey.MoveWordAbsoluteLongToAbsoluteLong or
                M68kInstructionTimingKey.MoveWordAbsoluteLongToAddressDisplacement or
                M68kInstructionTimingKey.CmpByteAbsoluteLongToData => M68kTimingOperand.AbsoluteLong,

                _ => M68kTimingOperand.None
            };
        }

        private static M68kTimingOperand GetDestinationOperand(M68kInstructionTimingKey key)
        {
            return key switch
            {
                M68kInstructionTimingKey.ImmediateWordToConditionCodeRegister => M68kTimingOperand.ConditionCodeRegister,
                M68kInstructionTimingKey.ImmediateWordToStatusRegister => M68kTimingOperand.StatusRegister,
                M68kInstructionTimingKey.MoveLongImmediateToAddress or
                M68kInstructionTimingKey.MoveLongDataToAddress or
                M68kInstructionTimingKey.MoveLongAddressToAddress or
                M68kInstructionTimingKey.MoveLongAddressIndirectToAddress or
                M68kInstructionTimingKey.MoveLongPostIncrementToAddress or
                M68kInstructionTimingKey.MoveLongAddressDisplacementToAddress or
                M68kInstructionTimingKey.MoveLongBriefIndexedToAddress or
                M68kInstructionTimingKey.LeaAbsoluteLong or
                M68kInstructionTimingKey.LeaAddressDisplacement or
                M68kInstructionTimingKey.LeaAbsoluteWord or
                >= M68kInstructionTimingKey.AddaLongImmediateToAddress and <= M68kInstructionTimingKey.SubaLongImmediateToAddress or
                >= M68kInstructionTimingKey.CmpaLongImmediateToAddress and <= M68kInstructionTimingKey.CmpaLongAddressIndirectToAddress => M68kTimingOperand.AddressRegister,

                M68kInstructionTimingKey.MoveLongImmediateToData or
                M68kInstructionTimingKey.MoveLongDataToData or
                M68kInstructionTimingKey.MoveLongAddressToData or
                M68kInstructionTimingKey.MoveLongAddressIndirectToData or
                M68kInstructionTimingKey.MoveLongPostIncrementToData or
                M68kInstructionTimingKey.MoveLongAddressDisplacementToData or
                M68kInstructionTimingKey.MoveLongBriefIndexedToData or
                M68kInstructionTimingKey.MoveLongAbsoluteLongToData or
                M68kInstructionTimingKey.MoveByteDataToData or
                M68kInstructionTimingKey.MoveByteImmediateToData or
                M68kInstructionTimingKey.MoveByteAddressIndirectToData or
                M68kInstructionTimingKey.MoveBytePostIncrementToData or
                M68kInstructionTimingKey.MoveByteAddressDisplacementToData or
                M68kInstructionTimingKey.MoveByteAbsoluteLongToData or
                M68kInstructionTimingKey.MoveByteBriefIndexedToData or
                M68kInstructionTimingKey.MoveWordAbsoluteLongToData or
                M68kInstructionTimingKey.MoveWordAddressDisplacementToData or
                M68kInstructionTimingKey.MoveWordImmediateToData or
                M68kInstructionTimingKey.ClrDataLong or
                M68kInstructionTimingKey.ClrDataWord or
                M68kInstructionTimingKey.ExtbLong or
                M68kInstructionTimingKey.ExtWordData or
                M68kInstructionTimingKey.TstWordData or
                M68kInstructionTimingKey.Moveq or
                M68kInstructionTimingKey.NegLongData or
                M68kInstructionTimingKey.NotByteData or
                M68kInstructionTimingKey.SwapData or
                >= M68kInstructionTimingKey.CmpLongDataToData and <= M68kInstructionTimingKey.CmpWordAddressDisplacementToData or
                M68kInstructionTimingKey.AndByteDataToData or
                M68kInstructionTimingKey.AndWordImmediateToData or
                M68kInstructionTimingKey.AndLongImmediateToData or
                M68kInstructionTimingKey.EoriWordImmediateToData or
                M68kInstructionTimingKey.EoriLongImmediateToData or
                M68kInstructionTimingKey.OriByteImmediateToData or
                >= M68kInstructionTimingKey.AsrLongImmediateData and <= M68kInstructionTimingKey.RolWordImmediateData or
                M68kInstructionTimingKey.BtstImmediateData or
                M68kInstructionTimingKey.BclrImmediateData or
                M68kInstructionTimingKey.BsetImmediateData or
                M68kInstructionTimingKey.BtstDynamicData or
                M68kInstructionTimingKey.BclrDynamicData => M68kTimingOperand.DataRegister,

                M68kInstructionTimingKey.MoveLongImmediateToAddressIndirect or
                M68kInstructionTimingKey.MoveLongDataToAddressIndirect or
                M68kInstructionTimingKey.MoveLongAddressToAddressIndirect or
                M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect or
                M68kInstructionTimingKey.MoveByteImmediateToAddressIndirect or
                M68kInstructionTimingKey.MoveByteDataToAddressIndirect or
                M68kInstructionTimingKey.ClrLongAddressIndirect or
                M68kInstructionTimingKey.ClrByteAddressIndirect or
                M68kInstructionTimingKey.AddiByteImmediateToAddressIndirect or
                M68kInstructionTimingKey.CmpiByteImmediateToAddressIndirect or
                M68kInstructionTimingKey.NbcdByteAddressIndirect => M68kTimingOperand.AddressIndirect,

                M68kInstructionTimingKey.MoveLongImmediateToPostIncrement or
                M68kInstructionTimingKey.MoveLongAddressToPostIncrement or
                M68kInstructionTimingKey.MoveLongAddressDisplacementToPostIncrement or
                M68kInstructionTimingKey.MoveByteDataToPostIncrement or
                M68kInstructionTimingKey.MoveBytePostIncrementToPostIncrement or
                M68kInstructionTimingKey.ClrLongPostIncrement or
                M68kInstructionTimingKey.CmpiLongImmediateToPostIncrement or
                M68kInstructionTimingKey.NbcdBytePostIncrement => M68kTimingOperand.PostIncrement,

                M68kInstructionTimingKey.MoveByteDataToPredecrement or
                M68kInstructionTimingKey.MoveByteBriefIndexedToPredecrement or
                M68kInstructionTimingKey.NbcdBytePredecrement => M68kTimingOperand.Predecrement,

                M68kInstructionTimingKey.MoveLongImmediateToAddressDisplacement or
                M68kInstructionTimingKey.MoveLongDataToAddressDisplacement or
                M68kInstructionTimingKey.MoveLongAddressToAddressDisplacement or
                M68kInstructionTimingKey.MoveLongAbsoluteWordToAddressDisplacement or
                M68kInstructionTimingKey.MoveLongAbsoluteLongToAddressDisplacement or
                M68kInstructionTimingKey.MoveByteImmediateToAddressDisplacement or
                M68kInstructionTimingKey.MoveWordImmediateToAddressDisplacement or
                M68kInstructionTimingKey.MoveWordDataToAddressDisplacement or
                M68kInstructionTimingKey.MoveWordAbsoluteLongToAddressDisplacement or
                M68kInstructionTimingKey.MoveByteDataToAddressDisplacement or
                M68kInstructionTimingKey.ClrLongAddressDisplacement or
                M68kInstructionTimingKey.ClrByteAddressDisplacement or
                M68kInstructionTimingKey.ClrWordAddressDisplacement or
                M68kInstructionTimingKey.AddiByteImmediateToAddressDisplacement or
                M68kInstructionTimingKey.SubiByteImmediateToAddressDisplacement or
                M68kInstructionTimingKey.AddWordDataToAddressDisplacement or
                M68kInstructionTimingKey.AddLongDataToAddressDisplacement or
                M68kInstructionTimingKey.CmpiLongImmediateToAddressDisplacement or
                M68kInstructionTimingKey.CmpiByteImmediateToAddressDisplacement or
                M68kInstructionTimingKey.CmpiWordImmediateToAddressDisplacement or
                M68kInstructionTimingKey.BsetByteImmediateAddressDisplacement or
                M68kInstructionTimingKey.NbcdByteAddressDisplacement => M68kTimingOperand.AddressDisplacement,

                M68kInstructionTimingKey.MoveByteImmediateToBriefIndexed or
                M68kInstructionTimingKey.MoveByteDataToBriefIndexed or
                M68kInstructionTimingKey.NbcdByteBriefIndexed => M68kTimingOperand.BriefIndexed,

                M68kInstructionTimingKey.NbcdByteAbsoluteWord => M68kTimingOperand.AbsoluteWord,

                M68kInstructionTimingKey.MoveByteImmediateToAbsoluteLong or
                M68kInstructionTimingKey.MoveWordImmediateToAbsoluteLong or
                M68kInstructionTimingKey.MoveLongImmediateToAbsoluteLong or
                M68kInstructionTimingKey.MoveLongDataToAbsoluteLong or
                M68kInstructionTimingKey.MoveLongAddressToAbsoluteLong or
                M68kInstructionTimingKey.MoveByteDataToAbsoluteLong or
                M68kInstructionTimingKey.MoveByteAddressIndirectToAbsoluteLong or
                M68kInstructionTimingKey.MoveByteAbsoluteLongToAbsoluteLong or
                M68kInstructionTimingKey.MoveWordDataToAbsoluteLong or
                M68kInstructionTimingKey.MoveWordAbsoluteLongToAbsoluteLong or
                M68kInstructionTimingKey.ClrLongAbsoluteLong or
                M68kInstructionTimingKey.ImmediateLogicalByteToAbsoluteLong or
                M68kInstructionTimingKey.AddiLongImmediateToAbsoluteLong or
                M68kInstructionTimingKey.CmpiLongImmediateToAbsoluteLong or
                M68kInstructionTimingKey.CmpiWordImmediateToAbsoluteLong or
                M68kInstructionTimingKey.BtstByteImmediateAbsoluteLong or
                M68kInstructionTimingKey.BchgByteImmediateAbsoluteLong or
                M68kInstructionTimingKey.BclrByteImmediateAbsoluteLong or
                M68kInstructionTimingKey.BsetByteImmediateAbsoluteLong or
                M68kInstructionTimingKey.SccAbsoluteLong or
                M68kInstructionTimingKey.NbcdByteAbsoluteLong => M68kTimingOperand.AbsoluteLong,

                _ => M68kTimingOperand.None
            };
        }

        private static M68kTimingOutcome GetOutcome(M68kInstructionTimingKey key)
        {
            return key switch
            {
                M68kInstructionTimingKey.BranchByteTaken or
                M68kInstructionTimingKey.BranchWordTaken or
                M68kInstructionTimingKey.BranchLongTaken or
                M68kInstructionTimingKey.DbccBranchTaken => M68kTimingOutcome.Taken,
                M68kInstructionTimingKey.BranchByteNotTaken or
                M68kInstructionTimingKey.BranchWordNotTaken or
                M68kInstructionTimingKey.BranchLongNotTaken => M68kTimingOutcome.NotTaken,
                M68kInstructionTimingKey.DbccConditionTrue => M68kTimingOutcome.ConditionTrue,
                M68kInstructionTimingKey.DbccExpired => M68kTimingOutcome.Expired,
                _ => M68kTimingOutcome.None
            };
        }
    }
}
