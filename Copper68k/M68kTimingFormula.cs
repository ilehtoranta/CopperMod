using System;

namespace Copper68k
{
    internal enum M68kTimingOperation
    {
        Other,
        Idle,
        NoOperation,
        Exception,
        Move,
        MoveAddress,
        MoveQuick,
        MoveControl,
        MoveMultiple,
        Clear,
        Test,
        LoadEffectiveAddress,
        Add,
        AddAddress,
        AddExtended,
        Subtract,
        SubtractAddress,
        Compare,
        CompareAddress,
        Logical,
        Bit,
        ShiftRotate,
        Decimal,
        Multiply,
        Divide,
        Branch,
        DecrementBranch,
        Jump,
        JumpSubroutine,
        Return,
        Link,
        Extend,
        Swap,
        SetCondition
    }

    internal enum M68kTimingOperandForm
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
        ConditionCodeRegister,
        StatusRegister,
        ControlRegister,
        RegisterList,
        EffectiveAddress,
        Stack,
        Other
    }

    internal enum M68kTimingBranchOutcome
    {
        None,
        Taken,
        NotTaken,
        ConditionTrue,
        Expired
    }

    internal enum M68kTimingFormulaKind
    {
        Compatibility,
        OperandShape,
        SpecialControl,
        MovemLong
    }

    internal readonly record struct M68kTimingOperand(M68kTimingOperandForm Form, string Label)
    {
        public static M68kTimingOperand None => new(M68kTimingOperandForm.None, string.Empty);

        public static M68kTimingOperand FromLabel(string label)
        {
            label = label.Trim();
            if (label.Length == 0)
            {
                return None;
            }

            if (label == "Dn")
            {
                return new(M68kTimingOperandForm.DataRegister, label);
            }

            if (label == "An")
            {
                return new(M68kTimingOperandForm.AddressRegister, label);
            }

            if (label == "CCR")
            {
                return new(M68kTimingOperandForm.ConditionCodeRegister, label);
            }

            if (label == "SR")
            {
                return new(M68kTimingOperandForm.StatusRegister, label);
            }

            if (label == "MOVEC")
            {
                return new(M68kTimingOperandForm.ControlRegister, label);
            }

            if (label == "#<data>")
            {
                return new(M68kTimingOperandForm.Immediate, label);
            }

            if (label == "<ea>")
            {
                return new(M68kTimingOperandForm.EffectiveAddress, label);
            }

            if (label.Contains("register", StringComparison.OrdinalIgnoreCase))
            {
                return new(M68kTimingOperandForm.RegisterList, label);
            }

            if (label == "(An)")
            {
                return new(M68kTimingOperandForm.AddressIndirect, label);
            }

            if (label == "(An)+")
            {
                return new(M68kTimingOperandForm.PostIncrement, label);
            }

            if (label == "-(An)")
            {
                return new(M68kTimingOperandForm.Predecrement, label);
            }

            if (label == "(d16,An)")
            {
                return new(M68kTimingOperandForm.AddressDisplacement, label);
            }

            if (label == "(d8,An,Xn)")
            {
                return new(M68kTimingOperandForm.BriefIndexed, label);
            }

            if (label == "(xxx).W")
            {
                return new(M68kTimingOperandForm.AbsoluteWord, label);
            }

            if (label == "(xxx).L")
            {
                return new(M68kTimingOperandForm.AbsoluteLong, label);
            }

            if (label.IndexOf("stack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new(M68kTimingOperandForm.Stack, label);
            }

            return new(M68kTimingOperandForm.Other, label);
        }
    }

    internal readonly record struct M68kTimingPlanShape(
        int NativeCycles,
        int HeadCycles,
        int TailCycles,
        bool UsesHeadTail)
    {
        public static M68kTimingPlanShape FromPlan(M68kInstructionPlan plan)
            => new(plan.NativeCycles, plan.HeadCycles, plan.TailCycles, plan.UsesHeadTail);
    }

    internal readonly record struct M68kTimingDescriptor(
        M68kTimingFormulaKind FormulaKind,
        M68kTimingOperation Operation,
        M68kOperandSize? Size,
        M68kTimingOperand Source,
        M68kTimingOperand Destination,
        M68kTimingBranchOutcome BranchOutcome,
        int RegisterCount,
        M68kTimingBarrier Barriers,
        M68kInstructionTimingKey LegacyKey,
        string LegacyLabel,
        M68kTimingPlanShape Plan)
    {
        public static bool TryCreateOperandShapeDescriptor(
            M68kInstructionTimingKey key,
            bool useHeadTail,
            out M68kTimingDescriptor descriptor)
        {
            descriptor = default;
            if (!UsesOperandShapeFormula(key) ||
                !TryCreateOperandShapeLabel(key, out var label))
            {
                return false;
            }

            var (source, destination) = SplitOperands(label);
            descriptor = new(
                M68kTimingFormulaKind.OperandShape,
                ClassifyOperation(key, label),
                ParseSize(label),
                source,
                destination,
                ClassifyBranchOutcome(key, label),
                RegisterCount: 0,
                GetOperandShapeBarriers(key),
                key,
                label,
                GetOperandShapePlanShape(key, useHeadTail));
            return true;
        }

        public static bool TryCreateSpecialControlDescriptor(
            M68kInstructionTimingKey key,
            bool useHeadTail,
            out M68kTimingDescriptor descriptor)
        {
            descriptor = default;
            if (!UsesSpecialControlFormula(key))
            {
                return false;
            }

            var label = GetSpecialControlLabel(key);
            var (source, destination) = SplitOperands(label);
            descriptor = new(
                M68kTimingFormulaKind.SpecialControl,
                ClassifyOperation(key, label),
                ParseSize(label),
                source,
                destination,
                ClassifyBranchOutcome(key, label),
                RegisterCount: 0,
                GetSpecialControlBarriers(key, useHeadTail),
                key,
                label,
                GetSpecialControlPlanShape(key, useHeadTail));
            return true;
        }

        public static M68kTimingDescriptor ForMovemLong(
            M68kInstructionTimingKey key,
            string label,
            int registerCount,
            bool registerToMemory,
            M68kAcceleratorModel model,
            int? fixedCycles)
        {
            const int registerListImmediateAddressCycles = 4;
            var nativeCycles = fixedCycles ?? (model == M68kAcceleratorModel.M68030
                ? registerToMemory
                    ? 4 + (2 * registerCount) + registerListImmediateAddressCycles
                    : 8 + (4 * registerCount) + registerListImmediateAddressCycles
                : registerToMemory
                    ? 4 + (3 * registerCount) + registerListImmediateAddressCycles
                    : 8 + (4 * registerCount) + registerListImmediateAddressCycles);
            var plan = model == M68kAcceleratorModel.M68030
                ? new M68kTimingPlanShape(nativeCycles, HeadCycles: 2, TailCycles: 0, UsesHeadTail: true)
                : new M68kTimingPlanShape(nativeCycles, HeadCycles: 0, TailCycles: 0, UsesHeadTail: false);

            return new(
                M68kTimingFormulaKind.MovemLong,
                M68kTimingOperation.MoveMultiple,
                M68kOperandSize.Long,
                registerToMemory
                    ? new M68kTimingOperand(M68kTimingOperandForm.RegisterList, "registers")
                    : M68kTimingOperand.FromLabel("(An)+"),
                registerToMemory
                    ? M68kTimingOperand.FromLabel("-(An)")
                    : new M68kTimingOperand(M68kTimingOperandForm.RegisterList, "registers"),
                M68kTimingBranchOutcome.None,
                registerCount,
                M68kTimingBarrier.None,
                key,
                label,
                plan);
        }

        public static bool UsesOperandShapeFormula(M68kInstructionTimingKey key)
        {
            var keyName = key.ToString();
            return key is M68kInstructionTimingKey.Moveq or M68kInstructionTimingKey.TstWordData ||
                keyName.StartsWith("MoveLong", StringComparison.Ordinal) ||
                keyName.StartsWith("MoveWord", StringComparison.Ordinal) ||
                keyName.StartsWith("MoveByte", StringComparison.Ordinal) ||
                keyName.StartsWith("Lea", StringComparison.Ordinal) ||
                keyName.StartsWith("Clr", StringComparison.Ordinal) ||
                keyName.StartsWith("ImmediateWordTo", StringComparison.Ordinal) ||
                keyName.StartsWith("ImmediateLogical", StringComparison.Ordinal) ||
                keyName.StartsWith("Add", StringComparison.Ordinal) ||
                keyName.StartsWith("Sub", StringComparison.Ordinal) ||
                keyName.StartsWith("Neg", StringComparison.Ordinal) ||
                keyName.StartsWith("Not", StringComparison.Ordinal) ||
                keyName.StartsWith("Ori", StringComparison.Ordinal) ||
                keyName.StartsWith("And", StringComparison.Ordinal) ||
                keyName.StartsWith("Eori", StringComparison.Ordinal) ||
                keyName.StartsWith("Cmpi", StringComparison.Ordinal) ||
                keyName.StartsWith("Cmpa", StringComparison.Ordinal) ||
                keyName.StartsWith("Cmp", StringComparison.Ordinal) ||
                keyName.StartsWith("Branch", StringComparison.Ordinal) ||
                keyName.StartsWith("Bsr", StringComparison.Ordinal) ||
                keyName.StartsWith("Dbcc", StringComparison.Ordinal) ||
                keyName.StartsWith("Scc", StringComparison.Ordinal) ||
                keyName.StartsWith("Btst", StringComparison.Ordinal) ||
                keyName.StartsWith("Bchg", StringComparison.Ordinal) ||
                keyName.StartsWith("Bclr", StringComparison.Ordinal) ||
                keyName.StartsWith("Bset", StringComparison.Ordinal) ||
                keyName.StartsWith("Asr", StringComparison.Ordinal) ||
                keyName.StartsWith("Asl", StringComparison.Ordinal) ||
                keyName.StartsWith("Lsr", StringComparison.Ordinal) ||
                keyName.StartsWith("Lsl", StringComparison.Ordinal) ||
                keyName.StartsWith("Ror", StringComparison.Ordinal) ||
                keyName.StartsWith("Rol", StringComparison.Ordinal) ||
                keyName.StartsWith("Roxr", StringComparison.Ordinal) ||
                keyName.StartsWith("Roxl", StringComparison.Ordinal) ||
                keyName.StartsWith("Abcd", StringComparison.Ordinal) ||
                keyName.StartsWith("Sbcd", StringComparison.Ordinal) ||
                keyName.StartsWith("Nbcd", StringComparison.Ordinal) ||
                keyName.StartsWith("Mulu", StringComparison.Ordinal) ||
                keyName.StartsWith("Muls", StringComparison.Ordinal) ||
                keyName.StartsWith("Divu", StringComparison.Ordinal) ||
                keyName.StartsWith("Divs", StringComparison.Ordinal);
        }

        public static bool UsesSpecialControlFormula(M68kInstructionTimingKey key)
            => key is M68kInstructionTimingKey.Idle or
                M68kInstructionTimingKey.Nop or
                M68kInstructionTimingKey.LineAException or
                M68kInstructionTimingKey.LineFException or
                M68kInstructionTimingKey.IllegalInstruction or
                M68kInstructionTimingKey.PrivilegeViolation or
                M68kInstructionTimingKey.FormatError or
                M68kInstructionTimingKey.InterruptAcknowledge or
                M68kInstructionTimingKey.Movec or
                M68kInstructionTimingKey.Rte or
                M68kInstructionTimingKey.Rtd or
                M68kInstructionTimingKey.Rts or
                M68kInstructionTimingKey.LinkLong or
                M68kInstructionTimingKey.ExtbLong or
                M68kInstructionTimingKey.ExtWordData or
                M68kInstructionTimingKey.SwapData or
                M68kInstructionTimingKey.JsrAbsoluteLong or
                M68kInstructionTimingKey.JmpAddressIndirect or
                M68kInstructionTimingKey.JmpAbsoluteLong;

        private static string GetSpecialControlLabel(M68kInstructionTimingKey key)
            => key switch
            {
                M68kInstructionTimingKey.Idle => "IDLE",
                M68kInstructionTimingKey.Nop => "NOP",
                M68kInstructionTimingKey.LineAException => "LINEA",
                M68kInstructionTimingKey.LineFException => "LINEF",
                M68kInstructionTimingKey.IllegalInstruction => "ILLEGAL",
                M68kInstructionTimingKey.PrivilegeViolation => "PRIVILEGE",
                M68kInstructionTimingKey.FormatError => "FORMAT",
                M68kInstructionTimingKey.InterruptAcknowledge => "INTERRUPT",
                M68kInstructionTimingKey.Movec => "MOVEC",
                M68kInstructionTimingKey.Rte => "RTE",
                M68kInstructionTimingKey.Rtd => "RTD",
                M68kInstructionTimingKey.Rts => "RTS",
                M68kInstructionTimingKey.LinkLong => "LINK.L",
                M68kInstructionTimingKey.ExtbLong => "EXTB.L",
                M68kInstructionTimingKey.ExtWordData => "EXT.W Dn",
                M68kInstructionTimingKey.SwapData => "SWAP Dn",
                M68kInstructionTimingKey.JsrAbsoluteLong => "JSR (xxx).L",
                M68kInstructionTimingKey.JmpAddressIndirect => "JMP (An)",
                M68kInstructionTimingKey.JmpAbsoluteLong => "JMP (xxx).L",
                _ => throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68020)
            };

        private static M68kTimingPlanShape GetSpecialControlPlanShape(M68kInstructionTimingKey key, bool useHeadTail)
        {
            if (!useHeadTail)
            {
                return new M68kTimingPlanShape(0, HeadCycles: 0, TailCycles: 0, UsesHeadTail: false);
            }

            var (headCycles, tailCycles) = key switch
            {
                M68kInstructionTimingKey.LinkLong => (2, 2),
                M68kInstructionTimingKey.ExtbLong => (2, 2),
                M68kInstructionTimingKey.ExtWordData => (1, 1),
                M68kInstructionTimingKey.SwapData => (1, 1),
                _ => (0, 0)
            };
            return new M68kTimingPlanShape(0, headCycles, tailCycles, UsesHeadTail: true);
        }

        private static M68kTimingBarrier GetSpecialControlBarriers(M68kInstructionTimingKey key, bool useHeadTail)
            => key switch
            {
                M68kInstructionTimingKey.Idle => M68kTimingBarrier.SynchronizeBus,
                M68kInstructionTimingKey.Nop => M68kTimingBarrier.SynchronizeBus,
                M68kInstructionTimingKey.LineAException => ExceptionBarrier(),
                M68kInstructionTimingKey.LineFException => ExceptionBarrier(),
                M68kInstructionTimingKey.IllegalInstruction => ExceptionBarrier(),
                M68kInstructionTimingKey.PrivilegeViolation => ExceptionBarrier(),
                M68kInstructionTimingKey.FormatError => ExceptionBarrier(),
                M68kInstructionTimingKey.InterruptAcknowledge => ExceptionBarrier(),
                M68kInstructionTimingKey.Movec => useHeadTail
                    ? M68kTimingBarrier.CacheControl | M68kTimingBarrier.SynchronizeBus
                    : M68kTimingBarrier.CacheControl,
                M68kInstructionTimingKey.Rte => M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.SynchronizeBus,
                M68kInstructionTimingKey.Rtd => M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch,
                M68kInstructionTimingKey.Rts => M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch,
                M68kInstructionTimingKey.JsrAbsoluteLong => M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch,
                M68kInstructionTimingKey.JmpAddressIndirect => M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch,
                M68kInstructionTimingKey.JmpAbsoluteLong => M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch,
                _ => M68kTimingBarrier.None
            };

        private static M68kTimingBarrier ExceptionBarrier()
            => M68kTimingBarrier.Exception | M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.SynchronizeBus;

        private static M68kTimingPlanShape GetOperandShapePlanShape(M68kInstructionTimingKey key, bool useHeadTail)
        {
            if (!useHeadTail)
            {
                return new M68kTimingPlanShape(0, HeadCycles: 0, TailCycles: 0, UsesHeadTail: false);
            }

            var headTailCycles = UsesZeroHeadTailOperandShape(key) ? 0 : 1;
            return new M68kTimingPlanShape(0, headTailCycles, headTailCycles, UsesHeadTail: true);
        }

        private static bool UsesZeroHeadTailOperandShape(M68kInstructionTimingKey key)
            => key is M68kInstructionTimingKey.ImmediateWordToStatusRegister or
                M68kInstructionTimingKey.BranchByteTaken or
                M68kInstructionTimingKey.BsrByte or
                M68kInstructionTimingKey.BranchWordTaken or
                M68kInstructionTimingKey.BsrWord or
                M68kInstructionTimingKey.DbccBranchTaken or
                M68kInstructionTimingKey.BranchLongTaken or
                M68kInstructionTimingKey.BsrLong;

        private static M68kTimingBarrier GetOperandShapeBarriers(M68kInstructionTimingKey key)
            => key switch
            {
                M68kInstructionTimingKey.ImmediateWordToStatusRegister => M68kTimingBarrier.SynchronizeBus,
                M68kInstructionTimingKey.BranchByteTaken => BranchBarrier(),
                M68kInstructionTimingKey.BsrByte => BranchBarrier(),
                M68kInstructionTimingKey.BranchWordTaken => BranchBarrier(),
                M68kInstructionTimingKey.BsrWord => BranchBarrier(),
                M68kInstructionTimingKey.DbccBranchTaken => BranchBarrier(),
                M68kInstructionTimingKey.BranchLongTaken => BranchBarrier(),
                M68kInstructionTimingKey.BsrLong => BranchBarrier(),
                _ => M68kTimingBarrier.None
            };

        private static M68kTimingBarrier BranchBarrier()
            => M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch;

        private static bool TryCreateOperandShapeLabel(M68kInstructionTimingKey key, out string label)
        {
            var keyName = key.ToString();
            label = key switch
            {
                M68kInstructionTimingKey.Moveq => "MOVEQ #<data>,Dn",
                M68kInstructionTimingKey.ImmediateWordToConditionCodeRegister => "ORI/ANDI/EORI.W #<data>,CCR",
                M68kInstructionTimingKey.ImmediateWordToStatusRegister => "ORI/ANDI/EORI.W #<data>,SR",
                M68kInstructionTimingKey.MuluLong => "MULU.L <ea>,Dn",
                M68kInstructionTimingKey.MulsLong => "MULS.L <ea>,Dn",
                M68kInstructionTimingKey.DivuLong => "DIVU.L <ea>,Dr:Dq",
                M68kInstructionTimingKey.DivsLong => "DIVS.L <ea>,Dr:Dq",
                M68kInstructionTimingKey.DbccConditionTrue => "DBcc condition true",
                M68kInstructionTimingKey.DbccBranchTaken => "DBcc branch taken",
                M68kInstructionTimingKey.DbccExpired => "DBcc expired",
                M68kInstructionTimingKey.SccAbsoluteLong => "Scc (xxx).L",
                _ => string.Empty
            };

            if (label.Length != 0)
            {
                return true;
            }

            return TryCreateMoveLabel(keyName, out label) ||
                TryCreateLeaLabel(keyName, out label) ||
                TryCreateClearLabel(keyName, out label) ||
                TryCreateUnaryInstructionLabel(keyName, "Tst", "TST", out label) ||
                TryCreateUnaryInstructionLabel(keyName, "Neg", "NEG", out label) ||
                TryCreateUnaryInstructionLabel(keyName, "Not", "NOT", out label) ||
                TryCreateImmediateLogicalLabel(keyName, out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Addi", "ADDI", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Adda", "ADDA", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Addx", "ADDX", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Add", "ADD", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Subi", "SUBI", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Suba", "SUBA", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Sub", "SUB", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Cmpi", "CMPI", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Cmpa", "CMPA", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Cmp", "CMP", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "And", "AND", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Eori", "EORI", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Ori", "ORI", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Mulu", "MULU", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Muls", "MULS", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Divu", "DIVU", out label) ||
                TryCreateSizedBinaryInstructionLabel(keyName, "Divs", "DIVS", out label) ||
                TryCreateDecimalLabel(keyName, "Abcd", "ABCD", out label) ||
                TryCreateDecimalLabel(keyName, "Sbcd", "SBCD", out label) ||
                TryCreateNbcdLabel(keyName, out label) ||
                TryCreateBitLabel(keyName, out label) ||
                TryCreateShiftRotateLabel(keyName, out label) ||
                TryCreateBranchLabel(keyName, out label);
        }

        private static bool TryCreateMoveLabel(string keyName, out string label)
        {
            label = string.Empty;
            if (!TryReadPrefixedSize(keyName, "Move", out var size, out var operands) ||
                !TrySplitOperandTokens(operands, out var sourceToken, out var destinationToken) ||
                !TryGetOperandLabel(sourceToken, out var source) ||
                !TryGetOperandLabel(destinationToken, out var destination))
            {
                return false;
            }

            var mnemonic = destinationToken == "Address" ? "MOVEA" : "MOVE";
            label = $"{mnemonic}.{GetSizeSuffix(size)} {source},{destination}";
            return true;
        }

        private static bool TryCreateLeaLabel(string keyName, out string label)
        {
            label = string.Empty;
            if (!keyName.StartsWith("Lea", StringComparison.Ordinal) ||
                !TryGetOperandLabel(keyName["Lea".Length..], out var source))
            {
                return false;
            }

            label = $"LEA {source},An";
            return true;
        }

        private static bool TryCreateClearLabel(string keyName, out string label)
        {
            label = string.Empty;
            if (!keyName.StartsWith("Clr", StringComparison.Ordinal))
            {
                return false;
            }

            var remainder = keyName["Clr".Length..];
            string destinationToken;
            M68kOperandSize size;
            if (remainder.StartsWith("Data", StringComparison.Ordinal))
            {
                destinationToken = "Data";
                if (!TryReadSizePrefix(remainder["Data".Length..], out size, out var extra) ||
                    extra.Length != 0)
                {
                    return false;
                }
            }
            else if (!TryReadSizePrefix(remainder, out size, out destinationToken))
            {
                return false;
            }

            if (!TryGetOperandLabel(destinationToken, out var destination))
            {
                return false;
            }

            label = $"CLR.{GetSizeSuffix(size)} {destination}";
            return true;
        }

        private static bool TryCreateUnaryInstructionLabel(
            string keyName,
            string keyPrefix,
            string mnemonic,
            out string label)
        {
            label = string.Empty;
            if (!TryReadPrefixedSize(keyName, keyPrefix, out var size, out var destinationToken) ||
                !TryGetOperandLabel(destinationToken, out var destination))
            {
                return false;
            }

            label = $"{mnemonic}.{GetSizeSuffix(size)} {destination}";
            return true;
        }

        private static bool TryCreateImmediateLogicalLabel(string keyName, out string label)
        {
            label = string.Empty;
            if (!TryReadPrefixedSize(keyName, "ImmediateLogical", out var size, out var remainder) ||
                !remainder.StartsWith("To", StringComparison.Ordinal) ||
                !TryGetOperandLabel(remainder["To".Length..], out var destination))
            {
                return false;
            }

            label = $"ORI/ANDI.{GetSizeSuffix(size)} #<data>,{destination}";
            return true;
        }

        private static bool TryCreateSizedBinaryInstructionLabel(
            string keyName,
            string keyPrefix,
            string mnemonic,
            out string label)
        {
            label = string.Empty;
            if (!TryReadPrefixedSize(keyName, keyPrefix, out var size, out var operands) ||
                !TrySplitOperandTokens(operands, out var sourceToken, out var destinationToken) ||
                !TryGetOperandLabel(sourceToken, out var source) ||
                !TryGetOperandLabel(destinationToken, out var destination))
            {
                return false;
            }

            label = $"{mnemonic}.{GetSizeSuffix(size)} {source},{destination}";
            return true;
        }

        private static bool TryCreateDecimalLabel(
            string keyName,
            string keyPrefix,
            string mnemonic,
            out string label)
        {
            label = string.Empty;
            if (!TryReadPrefixedSize(keyName, keyPrefix, out var size, out var operands) ||
                size != M68kOperandSize.Byte)
            {
                return false;
            }

            if (operands == "PredecrementMemory")
            {
                label = $"{mnemonic}.B -(An),-(An)";
                return true;
            }

            if (!TrySplitOperandTokens(operands, out var sourceToken, out var destinationToken) ||
                !TryGetOperandLabel(sourceToken, out var source) ||
                !TryGetOperandLabel(destinationToken, out var destination))
            {
                return false;
            }

            label = $"{mnemonic}.B {source},{destination}";
            return true;
        }

        private static bool TryCreateNbcdLabel(string keyName, out string label)
        {
            label = string.Empty;
            if (!TryReadPrefixedSize(keyName, "Nbcd", out var size, out var destinationToken) ||
                size != M68kOperandSize.Byte ||
                !TryGetOperandLabel(destinationToken, out var destination))
            {
                return false;
            }

            label = $"NBCD.B {destination}";
            return true;
        }

        private static bool TryCreateBitLabel(string keyName, out string label)
        {
            label = string.Empty;
            foreach (var (keyPrefix, mnemonic) in new[]
            {
                ("Btst", "BTST"),
                ("Bchg", "BCHG"),
                ("Bclr", "BCLR"),
                ("Bset", "BSET")
            })
            {
                if (!keyName.StartsWith(keyPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var operands = keyName[keyPrefix.Length..];
                if (operands.StartsWith("Byte", StringComparison.Ordinal))
                {
                    operands = operands["Byte".Length..];
                }

                string source;
                string destinationToken;
                if (operands.StartsWith("Immediate", StringComparison.Ordinal))
                {
                    source = "#<data>";
                    destinationToken = operands["Immediate".Length..];
                }
                else if (operands.StartsWith("Dynamic", StringComparison.Ordinal))
                {
                    source = "Dn";
                    destinationToken = operands["Dynamic".Length..];
                }
                else
                {
                    return false;
                }

                if (!TryGetOperandLabel(destinationToken, out var destination))
                {
                    return false;
                }

                label = $"{mnemonic} {source},{destination}";
                return true;
            }

            return false;
        }

        private static bool TryCreateShiftRotateLabel(string keyName, out string label)
        {
            label = string.Empty;
            foreach (var (keyPrefix, mnemonic) in new[]
            {
                ("Roxr", "ROXR"),
                ("Roxl", "ROXL"),
                ("Asr", "ASR"),
                ("Asl", "ASL"),
                ("Lsr", "LSR"),
                ("Lsl", "LSL"),
                ("Ror", "ROR"),
                ("Rol", "ROL")
            })
            {
                if (!TryReadPrefixedSize(keyName, keyPrefix, out var size, out var operands))
                {
                    continue;
                }

                if (operands != "ImmediateData")
                {
                    return false;
                }

                label = $"{mnemonic}.{GetSizeSuffix(size)} #<data>,Dn";
                return true;
            }

            return false;
        }

        private static bool TryCreateBranchLabel(string keyName, out string label)
        {
            label = string.Empty;
            if (TryReadPrefixedSize(keyName, "Branch", out var size, out var branchOutcome))
            {
                label = branchOutcome switch
                {
                    "Taken" => $"Bcc.{GetSizeSuffix(size)} taken",
                    "NotTaken" => $"Bcc.{GetSizeSuffix(size)} not taken",
                    _ => string.Empty
                };
                return label.Length != 0;
            }

            if (TryReadPrefixedSize(keyName, "Bsr", out size, out var remainder) &&
                remainder.Length == 0)
            {
                label = $"BSR.{GetSizeSuffix(size)}";
                return true;
            }

            return false;
        }

        private static bool TryReadPrefixedSize(
            string keyName,
            string keyPrefix,
            out M68kOperandSize size,
            out string remainder)
        {
            size = default;
            remainder = string.Empty;
            return keyName.StartsWith(keyPrefix, StringComparison.Ordinal) &&
                TryReadSizePrefix(keyName[keyPrefix.Length..], out size, out remainder);
        }

        private static bool TryReadSizePrefix(
            string text,
            out M68kOperandSize size,
            out string remainder)
        {
            if (text.StartsWith("Byte", StringComparison.Ordinal))
            {
                size = M68kOperandSize.Byte;
                remainder = text["Byte".Length..];
                return true;
            }

            if (text.StartsWith("Word", StringComparison.Ordinal))
            {
                size = M68kOperandSize.Word;
                remainder = text["Word".Length..];
                return true;
            }

            if (text.StartsWith("Long", StringComparison.Ordinal))
            {
                size = M68kOperandSize.Long;
                remainder = text["Long".Length..];
                return true;
            }

            size = default;
            remainder = string.Empty;
            return false;
        }

        private static bool TrySplitOperandTokens(
            string operands,
            out string sourceToken,
            out string destinationToken)
        {
            var separator = operands.IndexOf("To", StringComparison.Ordinal);
            if (separator <= 0 || separator == operands.Length - "To".Length)
            {
                sourceToken = string.Empty;
                destinationToken = string.Empty;
                return false;
            }

            sourceToken = operands[..separator];
            destinationToken = operands[(separator + "To".Length)..];
            return true;
        }

        private static bool TryGetOperandLabel(string token, out string label)
        {
            label = token switch
            {
                "Data" => "Dn",
                "Address" => "An",
                "AddressIndirect" => "(An)",
                "PostIncrement" => "(An)+",
                "Predecrement" => "-(An)",
                "AddressDisplacement" => "(d16,An)",
                "BriefIndexed" => "(d8,An,Xn)",
                "AbsoluteWord" => "(xxx).W",
                "AbsoluteLong" => "(xxx).L",
                "Immediate" => "#<data>",
                "ConditionCodeRegister" => "CCR",
                "StatusRegister" => "SR",
                _ => string.Empty
            };
            return label.Length != 0;
        }

        private static char GetSizeSuffix(M68kOperandSize size)
            => size switch
            {
                M68kOperandSize.Byte => 'B',
                M68kOperandSize.Word => 'W',
                M68kOperandSize.Long => 'L',
                _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
            };

        private static (M68kTimingOperand Source, M68kTimingOperand Destination) SplitOperands(string label)
        {
            var firstSpace = label.IndexOf(' ');
            if (firstSpace < 0 || firstSpace == label.Length - 1)
            {
                return (M68kTimingOperand.None, M68kTimingOperand.None);
            }

            var operands = label[(firstSpace + 1)..].Trim();
            if (operands.Length == 0 || operands.Contains("taken", StringComparison.OrdinalIgnoreCase))
            {
                return (M68kTimingOperand.None, M68kTimingOperand.None);
            }

            var comma = FindTopLevelOperandSeparator(operands);
            if (comma >= 0)
            {
                return (
                    M68kTimingOperand.FromLabel(operands[..comma]),
                    M68kTimingOperand.FromLabel(operands[(comma + 1)..]));
            }

            return (M68kTimingOperand.None, M68kTimingOperand.FromLabel(operands));
        }

        private static int FindTopLevelOperandSeparator(string operands)
        {
            var depth = 0;
            for (var i = 0; i < operands.Length; i++)
            {
                depth += operands[i] switch
                {
                    '(' => 1,
                    ')' => depth > 0 ? -1 : 0,
                    _ => 0
                };

                if (operands[i] == ',' && depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static M68kOperandSize? ParseSize(string label)
        {
            var space = label.IndexOf(' ');
            var mnemonic = space >= 0 ? label[..space] : label;
            if (mnemonic.Contains(".B", StringComparison.Ordinal))
            {
                return M68kOperandSize.Byte;
            }

            if (mnemonic.Contains(".W", StringComparison.Ordinal))
            {
                return M68kOperandSize.Word;
            }

            if (mnemonic.Contains(".L", StringComparison.Ordinal))
            {
                return M68kOperandSize.Long;
            }

            return mnemonic == "MOVEQ" ? M68kOperandSize.Long : null;
        }

        private static M68kTimingBranchOutcome ClassifyBranchOutcome(M68kInstructionTimingKey key, string label)
        {
            return key switch
            {
                M68kInstructionTimingKey.DbccConditionTrue => M68kTimingBranchOutcome.ConditionTrue,
                M68kInstructionTimingKey.DbccExpired => M68kTimingBranchOutcome.Expired,
                _ when label.Contains("not taken", StringComparison.OrdinalIgnoreCase) => M68kTimingBranchOutcome.NotTaken,
                _ when label.Contains("taken", StringComparison.OrdinalIgnoreCase) => M68kTimingBranchOutcome.Taken,
                _ => M68kTimingBranchOutcome.None
            };
        }

        private static M68kTimingOperation ClassifyOperation(M68kInstructionTimingKey key, string label)
        {
            var keyName = key.ToString();
            if (key == M68kInstructionTimingKey.Idle)
            {
                return M68kTimingOperation.Idle;
            }

            if (key == M68kInstructionTimingKey.Nop)
            {
                return M68kTimingOperation.NoOperation;
            }

            if (keyName.Contains("Exception", StringComparison.Ordinal) ||
                key is M68kInstructionTimingKey.IllegalInstruction or
                    M68kInstructionTimingKey.PrivilegeViolation or
                    M68kInstructionTimingKey.FormatError or
                    M68kInstructionTimingKey.InterruptAcknowledge)
            {
                return M68kTimingOperation.Exception;
            }

            if (keyName.StartsWith("MoveLong", StringComparison.Ordinal) ||
                keyName.StartsWith("MoveWord", StringComparison.Ordinal) ||
                keyName.StartsWith("MoveByte", StringComparison.Ordinal))
            {
                return label.StartsWith("MOVEA", StringComparison.Ordinal)
                    ? M68kTimingOperation.MoveAddress
                    : M68kTimingOperation.Move;
            }

            if (key == M68kInstructionTimingKey.Moveq)
            {
                return M68kTimingOperation.MoveQuick;
            }

            if (key == M68kInstructionTimingKey.Movec)
            {
                return M68kTimingOperation.MoveControl;
            }

            if (keyName.StartsWith("Movem", StringComparison.Ordinal))
            {
                return M68kTimingOperation.MoveMultiple;
            }

            if (keyName.StartsWith("Clr", StringComparison.Ordinal)) return M68kTimingOperation.Clear;
            if (keyName.StartsWith("Tst", StringComparison.Ordinal)) return M68kTimingOperation.Test;
            if (keyName.StartsWith("Lea", StringComparison.Ordinal)) return M68kTimingOperation.LoadEffectiveAddress;
            if (keyName.StartsWith("Adda", StringComparison.Ordinal)) return M68kTimingOperation.AddAddress;
            if (keyName.StartsWith("Addx", StringComparison.Ordinal)) return M68kTimingOperation.AddExtended;
            if (keyName.StartsWith("Add", StringComparison.Ordinal)) return M68kTimingOperation.Add;
            if (keyName.StartsWith("Neg", StringComparison.Ordinal)) return M68kTimingOperation.Subtract;
            if (keyName.StartsWith("Suba", StringComparison.Ordinal)) return M68kTimingOperation.SubtractAddress;
            if (keyName.StartsWith("Sub", StringComparison.Ordinal)) return M68kTimingOperation.Subtract;
            if (keyName.StartsWith("Cmpa", StringComparison.Ordinal)) return M68kTimingOperation.CompareAddress;
            if (keyName.StartsWith("Cmp", StringComparison.Ordinal)) return M68kTimingOperation.Compare;
            if (keyName.StartsWith("And", StringComparison.Ordinal) || keyName.StartsWith("Eori", StringComparison.Ordinal) || keyName.StartsWith("Ori", StringComparison.Ordinal) || keyName.StartsWith("Not", StringComparison.Ordinal) || keyName.StartsWith("ImmediateLogical", StringComparison.Ordinal) || keyName.StartsWith("ImmediateWordTo", StringComparison.Ordinal)) return M68kTimingOperation.Logical;
            if (keyName.StartsWith("Btst", StringComparison.Ordinal) || keyName.StartsWith("Bchg", StringComparison.Ordinal) || keyName.StartsWith("Bclr", StringComparison.Ordinal) || keyName.StartsWith("Bset", StringComparison.Ordinal)) return M68kTimingOperation.Bit;
            if (keyName.StartsWith("As", StringComparison.Ordinal) || keyName.StartsWith("Ls", StringComparison.Ordinal) || keyName.StartsWith("Ro", StringComparison.Ordinal)) return M68kTimingOperation.ShiftRotate;
            if (keyName.StartsWith("Abcd", StringComparison.Ordinal) || keyName.StartsWith("Sbcd", StringComparison.Ordinal) || keyName.StartsWith("Nbcd", StringComparison.Ordinal)) return M68kTimingOperation.Decimal;
            if (keyName.StartsWith("Mulu", StringComparison.Ordinal) || keyName.StartsWith("Muls", StringComparison.Ordinal)) return M68kTimingOperation.Multiply;
            if (keyName.StartsWith("Divu", StringComparison.Ordinal) || keyName.StartsWith("Divs", StringComparison.Ordinal)) return M68kTimingOperation.Divide;
            if (keyName.StartsWith("Branch", StringComparison.Ordinal) || keyName.StartsWith("Bsr", StringComparison.Ordinal)) return M68kTimingOperation.Branch;
            if (keyName.StartsWith("Dbcc", StringComparison.Ordinal)) return M68kTimingOperation.DecrementBranch;
            if (keyName.StartsWith("Jmp", StringComparison.Ordinal)) return M68kTimingOperation.Jump;
            if (keyName.StartsWith("Jsr", StringComparison.Ordinal)) return M68kTimingOperation.JumpSubroutine;
            if (keyName is nameof(M68kInstructionTimingKey.Rte) or nameof(M68kInstructionTimingKey.Rtd) or nameof(M68kInstructionTimingKey.Rts)) return M68kTimingOperation.Return;
            if (keyName.StartsWith("Link", StringComparison.Ordinal)) return M68kTimingOperation.Link;
            if (keyName.StartsWith("Ext", StringComparison.Ordinal)) return M68kTimingOperation.Extend;
            if (keyName.StartsWith("Swap", StringComparison.Ordinal)) return M68kTimingOperation.Swap;
            if (keyName.StartsWith("Scc", StringComparison.Ordinal)) return M68kTimingOperation.SetCondition;

            return M68kTimingOperation.Other;
        }
    }

    internal static class M68kTimingFormula
    {
        public static M68kInstructionPlan CreatePlan(M68kTimingDescriptor descriptor)
        {
            if (descriptor.FormulaKind is M68kTimingFormulaKind.OperandShape or M68kTimingFormulaKind.SpecialControl)
            {
                return CreateOperandShapePlan(descriptor);
            }

            return CreateCompatibilityPlan(descriptor);
        }

        private static M68kInstructionPlan CreateCompatibilityPlan(M68kTimingDescriptor descriptor)
        {
            return descriptor.Plan.UsesHeadTail
                ? M68kInstructionPlan.CreateHeadTail(
                    descriptor.LegacyKey,
                    descriptor.LegacyLabel,
                    descriptor.Plan.NativeCycles,
                    descriptor.Plan.HeadCycles,
                    descriptor.Plan.TailCycles,
                    descriptor.Barriers)
                : M68kInstructionPlan.CreateFlat(
                    descriptor.LegacyKey,
                    descriptor.LegacyLabel,
                    descriptor.Plan.NativeCycles,
                    descriptor.Barriers);
        }

        private static M68kInstructionPlan CreateOperandShapePlan(M68kTimingDescriptor descriptor)
        {
            var cycles = descriptor.Operation switch
            {
                M68kTimingOperation.Idle => 2,
                M68kTimingOperation.NoOperation => 4,
                M68kTimingOperation.Exception => CalculateExceptionCycles(descriptor),
                M68kTimingOperation.Move => CalculateMoveCycles(descriptor),
                M68kTimingOperation.MoveAddress => CalculateMoveCycles(descriptor),
                M68kTimingOperation.MoveQuick => 2,
                M68kTimingOperation.MoveControl => 12,
                M68kTimingOperation.Clear => CalculateClearCycles(descriptor),
                M68kTimingOperation.Test => CalculateTestCycles(descriptor),
                M68kTimingOperation.LoadEffectiveAddress => CalculateLeaCycles(descriptor),
                M68kTimingOperation.Add => CalculateArithmeticCycles(descriptor),
                M68kTimingOperation.AddAddress => CalculateArithmeticCycles(descriptor),
                M68kTimingOperation.AddExtended => CalculateArithmeticCycles(descriptor),
                M68kTimingOperation.Subtract => CalculateArithmeticCycles(descriptor),
                M68kTimingOperation.SubtractAddress => CalculateArithmeticCycles(descriptor),
                M68kTimingOperation.Compare => CalculateCompareCycles(descriptor),
                M68kTimingOperation.CompareAddress => CalculateCompareCycles(descriptor),
                M68kTimingOperation.Logical => CalculateLogicalCycles(descriptor),
                M68kTimingOperation.Branch => CalculateBranchCycles(descriptor),
                M68kTimingOperation.DecrementBranch => CalculateDecrementBranchCycles(descriptor),
                M68kTimingOperation.SetCondition => CalculateSetConditionCycles(descriptor),
                M68kTimingOperation.Bit => CalculateBitCycles(descriptor),
                M68kTimingOperation.ShiftRotate => CalculateShiftRotateCycles(descriptor),
                M68kTimingOperation.Decimal => CalculateDecimalCycles(descriptor),
                M68kTimingOperation.Multiply => CalculateMultiplyCycles(descriptor),
                M68kTimingOperation.Divide => CalculateDivideCycles(descriptor),
                M68kTimingOperation.Jump => CalculateJumpCycles(descriptor),
                M68kTimingOperation.JumpSubroutine => 7,
                M68kTimingOperation.Return => CalculateReturnCycles(descriptor),
                M68kTimingOperation.Link => 16,
                M68kTimingOperation.Extend => CalculateExtendCycles(descriptor),
                M68kTimingOperation.Swap => 4,
                _ => throw Unsupported(descriptor)
            };

            return descriptor.Plan.UsesHeadTail
                ? M68kInstructionPlan.CreateHeadTail(
                    descriptor.LegacyKey,
                    descriptor.LegacyLabel,
                    cycles,
                    descriptor.Plan.HeadCycles,
                    descriptor.Plan.TailCycles,
                    descriptor.Barriers)
                : M68kInstructionPlan.CreateFlat(
                    descriptor.LegacyKey,
                    descriptor.LegacyLabel,
                    cycles,
                    descriptor.Barriers);
        }

        private static int CalculateMoveCycles(M68kTimingDescriptor descriptor)
        {
            var size = RequireSize(descriptor);
            var source = descriptor.Source.Form;
            var destination = descriptor.Destination.Form;

            if (IsRegister(destination))
            {
                return CalculateMoveToRegisterCycles(descriptor, source, size);
            }

            if (IsMemory(destination))
            {
                if (IsRegister(source))
                {
                    return CalculateRegisterToMemoryMoveCycles(descriptor, destination);
                }

                if (source == M68kTimingOperandForm.Immediate)
                {
                    return CalculateImmediateToMemoryMoveCycles(descriptor, destination, size);
                }

                if (IsMemory(source))
                {
                    return CalculateMemoryToMemoryMoveCycles(descriptor, source, destination, size);
                }
            }

            throw Unsupported(descriptor);
        }

        private static int CalculateArithmeticCycles(M68kTimingDescriptor descriptor)
        {
            var size = RequireSize(descriptor);
            var source = descriptor.Source.Form;
            var destination = descriptor.Destination.Form;

            if (source == M68kTimingOperandForm.None && destination == M68kTimingOperandForm.DataRegister)
            {
                return 2;
            }

            if (destination is M68kTimingOperandForm.DataRegister or M68kTimingOperandForm.AddressRegister)
            {
                return CalculateReadToRegisterArithmeticCycles(descriptor, source, size);
            }

            if (IsMemory(destination))
            {
                if (IsRegister(source))
                {
                    return CalculateRegisterToMemoryArithmeticCycles(descriptor, destination, size);
                }

                if (source == M68kTimingOperandForm.Immediate)
                {
                    return CalculateImmediateToMemoryArithmeticCycles(descriptor, destination, size);
                }
            }

            throw Unsupported(descriptor);
        }

        private static int CalculateCompareCycles(M68kTimingDescriptor descriptor)
        {
            var size = RequireSize(descriptor);
            var source = descriptor.Source.Form;
            var destination = descriptor.Destination.Form;

            if (destination is M68kTimingOperandForm.DataRegister or M68kTimingOperandForm.AddressRegister)
            {
                return CalculateReadToRegisterCompareCycles(descriptor, source, size);
            }

            if (source == M68kTimingOperandForm.Immediate && IsMemory(destination))
            {
                return CalculateImmediateCompareToMemoryCycles(descriptor, destination, size);
            }

            throw Unsupported(descriptor);
        }

        private static int CalculateLogicalCycles(M68kTimingDescriptor descriptor)
        {
            var size = RequireSize(descriptor);
            var source = descriptor.Source.Form;
            var destination = descriptor.Destination.Form;

            if (source == M68kTimingOperandForm.None && destination == M68kTimingOperandForm.DataRegister)
            {
                return 2;
            }

            if (source == M68kTimingOperandForm.Immediate &&
                destination is M68kTimingOperandForm.ConditionCodeRegister or M68kTimingOperandForm.StatusRegister)
            {
                return destination == M68kTimingOperandForm.StatusRegister ? 20 : 8;
            }

            if (destination == M68kTimingOperandForm.DataRegister)
            {
                if (source == M68kTimingOperandForm.Immediate)
                {
                    return CalculateImmediateToRegisterCycles(size);
                }

                if (source == M68kTimingOperandForm.DataRegister)
                {
                    return 2;
                }
            }

            if (source == M68kTimingOperandForm.Immediate && IsMemory(destination))
            {
                return CalculateImmediateLogicalToMemoryCycles(descriptor, destination, size);
            }

            throw Unsupported(descriptor);
        }

        private static int CalculateBranchCycles(M68kTimingDescriptor descriptor)
        {
            var size = RequireSize(descriptor);
            if (descriptor.LegacyKey.ToString().StartsWith("Bsr", StringComparison.Ordinal))
            {
                return size == M68kOperandSize.Long ? 18 : 7;
            }

            return descriptor.BranchOutcome switch
            {
                M68kTimingBranchOutcome.Taken => size == M68kOperandSize.Long ? 10 : 6,
                M68kTimingBranchOutcome.NotTaken => size switch
                {
                    M68kOperandSize.Byte => 4,
                    M68kOperandSize.Word => 6,
                    M68kOperandSize.Long => 8,
                    _ => throw Unsupported(descriptor)
                },
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateExceptionCycles(M68kTimingDescriptor descriptor)
            => descriptor.LegacyKey switch
            {
                M68kInstructionTimingKey.LineAException => 34,
                M68kInstructionTimingKey.LineFException => 34,
                M68kInstructionTimingKey.IllegalInstruction => 20,
                M68kInstructionTimingKey.PrivilegeViolation => 20,
                M68kInstructionTimingKey.FormatError => 20,
                M68kInstructionTimingKey.InterruptAcknowledge => 44,
                _ => throw Unsupported(descriptor)
            };

        private static int CalculateJumpCycles(M68kTimingDescriptor descriptor)
            => descriptor.Destination.Form switch
            {
                M68kTimingOperandForm.AddressIndirect => 4,
                M68kTimingOperandForm.AbsoluteLong => 6,
                _ => throw Unsupported(descriptor)
            };

        private static int CalculateReturnCycles(M68kTimingDescriptor descriptor)
            => descriptor.LegacyKey switch
            {
                M68kInstructionTimingKey.Rte => 20,
                M68kInstructionTimingKey.Rtd => 16,
                M68kInstructionTimingKey.Rts => 7,
                _ => throw Unsupported(descriptor)
            };

        private static int CalculateExtendCycles(M68kTimingDescriptor descriptor)
            => RequireSize(descriptor) switch
            {
                M68kOperandSize.Word => 2,
                M68kOperandSize.Long => 4,
                _ => throw Unsupported(descriptor)
            };

        private static int CalculateDecrementBranchCycles(M68kTimingDescriptor descriptor)
        {
            return descriptor.BranchOutcome switch
            {
                M68kTimingBranchOutcome.ConditionTrue => 6,
                M68kTimingBranchOutcome.Taken => 10,
                M68kTimingBranchOutcome.Expired => 12,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateSetConditionCycles(M68kTimingDescriptor descriptor)
        {
            return descriptor.Destination.Form switch
            {
                M68kTimingOperandForm.AbsoluteLong => 10,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateBitCycles(M68kTimingDescriptor descriptor)
        {
            if (descriptor.Destination.Form == M68kTimingOperandForm.DataRegister)
            {
                return 4;
            }

            return descriptor.Destination.Form switch
            {
                M68kTimingOperandForm.AbsoluteLong => descriptor.LegacyKey.ToString().StartsWith("Btst", StringComparison.Ordinal) ? 10 : 12,
                M68kTimingOperandForm.AddressDisplacement when descriptor.LegacyKey.ToString().StartsWith("Bset", StringComparison.Ordinal) => 10,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateShiftRotateCycles(M68kTimingDescriptor descriptor)
        {
            if (descriptor.Source.Form != M68kTimingOperandForm.Immediate ||
                descriptor.Destination.Form != M68kTimingOperandForm.DataRegister)
            {
                throw Unsupported(descriptor);
            }

            return descriptor.LegacyLabel.StartsWith("ASR", StringComparison.Ordinal) ||
                descriptor.LegacyLabel.StartsWith("LSR", StringComparison.Ordinal)
                ? 6
                : 8;
        }

        private static int CalculateDecimalCycles(M68kTimingDescriptor descriptor)
        {
            var source = descriptor.Source.Form;
            var destination = descriptor.Destination.Form;

            if (source == M68kTimingOperandForm.DataRegister &&
                destination == M68kTimingOperandForm.DataRegister)
            {
                return 6;
            }

            if (source == M68kTimingOperandForm.Predecrement &&
                destination == M68kTimingOperandForm.Predecrement)
            {
                return 18;
            }

            if (source == M68kTimingOperandForm.None)
            {
                return destination switch
                {
                    M68kTimingOperandForm.DataRegister => 6,
                    M68kTimingOperandForm.AddressIndirect => 8,
                    M68kTimingOperandForm.PostIncrement => 8,
                    M68kTimingOperandForm.Predecrement => 8,
                    M68kTimingOperandForm.AddressDisplacement => 10,
                    M68kTimingOperandForm.BriefIndexed => 12,
                    M68kTimingOperandForm.AbsoluteWord => 10,
                    M68kTimingOperandForm.AbsoluteLong => 12,
                    _ => throw Unsupported(descriptor)
                };
            }

            throw Unsupported(descriptor);
        }

        private static int CalculateMultiplyCycles(M68kTimingDescriptor descriptor)
        {
            var size = RequireSize(descriptor);
            return size switch
            {
                M68kOperandSize.Word => 46,
                M68kOperandSize.Long => 44,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateDivideCycles(M68kTimingDescriptor descriptor)
        {
            var size = RequireSize(descriptor);
            var signed = descriptor.LegacyKey.ToString().StartsWith("Divs", StringComparison.Ordinal);
            return size switch
            {
                M68kOperandSize.Word => signed ? 58 : 46,
                M68kOperandSize.Long => signed ? 82 : 76,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateReadToRegisterArithmeticCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm source,
            M68kOperandSize size)
        {
            if (IsRegister(source))
            {
                return 2;
            }

            if (source == M68kTimingOperandForm.Immediate)
            {
                return CalculateImmediateToRegisterCycles(size);
            }

            return source switch
            {
                M68kTimingOperandForm.PostIncrement => 6,
                M68kTimingOperandForm.AddressDisplacement => size == M68kOperandSize.Long ? 7 : 6,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateReadToRegisterCompareCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm source,
            M68kOperandSize size)
        {
            if (IsRegister(source))
            {
                return 2;
            }

            if (source == M68kTimingOperandForm.Immediate)
            {
                return CalculateImmediateToRegisterCycles(size);
            }

            return source switch
            {
                M68kTimingOperandForm.AddressIndirect => 6,
                M68kTimingOperandForm.PostIncrement => 6,
                M68kTimingOperandForm.AddressDisplacement => 6,
                M68kTimingOperandForm.AbsoluteLong => 6,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateRegisterToMemoryArithmeticCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm destination,
            M68kOperandSize size)
        {
            return destination switch
            {
                M68kTimingOperandForm.AddressDisplacement => size == M68kOperandSize.Long ? 13 : 9,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateImmediateToMemoryArithmeticCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm destination,
            M68kOperandSize size)
        {
            return destination switch
            {
                M68kTimingOperandForm.AddressIndirect => 6,
                M68kTimingOperandForm.AddressDisplacement => size == M68kOperandSize.Long ? 10 : 8,
                M68kTimingOperandForm.AbsoluteLong => size == M68kOperandSize.Long ? 12 : 10,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateImmediateCompareToMemoryCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm destination,
            M68kOperandSize size)
        {
            return destination switch
            {
                M68kTimingOperandForm.AddressIndirect => 6,
                M68kTimingOperandForm.PostIncrement => size == M68kOperandSize.Long ? 10 : 8,
                M68kTimingOperandForm.AddressDisplacement => size == M68kOperandSize.Long ? 9 : 8,
                M68kTimingOperandForm.AbsoluteLong => size == M68kOperandSize.Long ? 10 : 8,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateImmediateLogicalToMemoryCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm destination,
            M68kOperandSize size)
        {
            return destination switch
            {
                M68kTimingOperandForm.AbsoluteLong => size == M68kOperandSize.Long ? 12 : 10,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateImmediateToRegisterCycles(M68kOperandSize size)
            => size == M68kOperandSize.Long ? 6 : 4;

        private static int CalculateMoveToRegisterCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm source,
            M68kOperandSize size)
        {
            if (IsRegister(source))
            {
                return 2;
            }

            return source switch
            {
                M68kTimingOperandForm.Immediate => size == M68kOperandSize.Long ? 6 : 4,
                M68kTimingOperandForm.AddressIndirect => 6,
                M68kTimingOperandForm.PostIncrement => 6,
                M68kTimingOperandForm.AddressDisplacement => 6,
                M68kTimingOperandForm.BriefIndexed => 8,
                M68kTimingOperandForm.AbsoluteWord => 6,
                M68kTimingOperandForm.AbsoluteLong => size == M68kOperandSize.Long ? 8 : 6,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateRegisterToMemoryMoveCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm destination)
        {
            return destination switch
            {
                M68kTimingOperandForm.AddressIndirect => 4,
                M68kTimingOperandForm.PostIncrement => 4,
                M68kTimingOperandForm.Predecrement => 4,
                M68kTimingOperandForm.AddressDisplacement => 6,
                M68kTimingOperandForm.BriefIndexed => 8,
                M68kTimingOperandForm.AbsoluteLong => 6,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateImmediateToMemoryMoveCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm destination,
            M68kOperandSize size)
        {
            return destination switch
            {
                M68kTimingOperandForm.AddressIndirect => size == M68kOperandSize.Long ? 8 : 6,
                M68kTimingOperandForm.PostIncrement => size == M68kOperandSize.Long ? 8 : 6,
                M68kTimingOperandForm.AddressDisplacement => size == M68kOperandSize.Long ? 10 : 6,
                M68kTimingOperandForm.BriefIndexed => 8,
                M68kTimingOperandForm.AbsoluteLong => size == M68kOperandSize.Long ? 10 : 8,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateMemoryToMemoryMoveCycles(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm source,
            M68kTimingOperandForm destination,
            M68kOperandSize size)
        {
            return (source, destination, size) switch
            {
                (M68kTimingOperandForm.AddressIndirect, M68kTimingOperandForm.AddressIndirect, M68kOperandSize.Long) => 8,
                (M68kTimingOperandForm.AddressDisplacement, M68kTimingOperandForm.PostIncrement, M68kOperandSize.Long) => 10,
                (M68kTimingOperandForm.AbsoluteWord, M68kTimingOperandForm.AddressDisplacement, M68kOperandSize.Long) => 8,
                (M68kTimingOperandForm.AbsoluteLong, M68kTimingOperandForm.AddressDisplacement, M68kOperandSize.Long) => 10,
                (M68kTimingOperandForm.AbsoluteLong, M68kTimingOperandForm.AddressDisplacement, M68kOperandSize.Word) => 8,
                (M68kTimingOperandForm.BriefIndexed, M68kTimingOperandForm.Predecrement, M68kOperandSize.Byte) => 10,
                (M68kTimingOperandForm.PostIncrement, M68kTimingOperandForm.PostIncrement, M68kOperandSize.Byte) => 8,
                (M68kTimingOperandForm.AddressIndirect, M68kTimingOperandForm.AbsoluteLong, M68kOperandSize.Byte) => 8,
                (M68kTimingOperandForm.AbsoluteLong, M68kTimingOperandForm.AbsoluteLong, M68kOperandSize.Byte) => 10,
                (M68kTimingOperandForm.AbsoluteLong, M68kTimingOperandForm.AbsoluteLong, M68kOperandSize.Word) => 10,
                _ => CalculateMemorySourceContribution(descriptor, source, size) + CalculateMemoryDestinationContribution(descriptor, destination)
            };
        }

        private static int CalculateMemorySourceContribution(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm source,
            M68kOperandSize size)
        {
            return source switch
            {
                M68kTimingOperandForm.AddressIndirect => 4,
                M68kTimingOperandForm.PostIncrement => 4,
                M68kTimingOperandForm.AddressDisplacement => 6,
                M68kTimingOperandForm.BriefIndexed => 6,
                M68kTimingOperandForm.AbsoluteWord => size == M68kOperandSize.Long ? 4 : 6,
                M68kTimingOperandForm.AbsoluteLong => 6,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateMemoryDestinationContribution(
            M68kTimingDescriptor descriptor,
            M68kTimingOperandForm destination)
        {
            return destination switch
            {
                M68kTimingOperandForm.AddressIndirect => 4,
                M68kTimingOperandForm.PostIncrement => 4,
                M68kTimingOperandForm.Predecrement => 4,
                M68kTimingOperandForm.AddressDisplacement => 4,
                M68kTimingOperandForm.AbsoluteLong => 4,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateClearCycles(M68kTimingDescriptor descriptor)
        {
            var size = RequireSize(descriptor);
            var destination = descriptor.Destination.Form;
            if (IsRegister(destination))
            {
                return 2;
            }

            return destination switch
            {
                M68kTimingOperandForm.AddressIndirect => size == M68kOperandSize.Long ? 6 : 4,
                M68kTimingOperandForm.PostIncrement => size == M68kOperandSize.Long ? 6 : 4,
                M68kTimingOperandForm.Predecrement => size == M68kOperandSize.Long ? 6 : 4,
                M68kTimingOperandForm.AddressDisplacement => size == M68kOperandSize.Long ? 8 : 6,
                M68kTimingOperandForm.AbsoluteLong => size == M68kOperandSize.Long ? 8 : 6,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateTestCycles(M68kTimingDescriptor descriptor)
        {
            return descriptor.Destination.Form switch
            {
                M68kTimingOperandForm.DataRegister => 2,
                _ => throw Unsupported(descriptor)
            };
        }

        private static int CalculateLeaCycles(M68kTimingDescriptor descriptor)
        {
            return descriptor.Source.Form switch
            {
                M68kTimingOperandForm.AddressDisplacement => 4,
                M68kTimingOperandForm.AbsoluteWord => 4,
                M68kTimingOperandForm.AbsoluteLong => 6,
                M68kTimingOperandForm.BriefIndexed => 8,
                _ => throw Unsupported(descriptor)
            };
        }

        private static M68kOperandSize RequireSize(M68kTimingDescriptor descriptor)
        {
            if (descriptor.Size is M68kOperandSize size)
            {
                return size;
            }

            throw Unsupported(descriptor);
        }

        private static UnsupportedM68kTimingException Unsupported(M68kTimingDescriptor descriptor)
            => new(
                descriptor.LegacyKey,
                descriptor.Plan.UsesHeadTail ? M68kAcceleratorModel.M68030 : M68kAcceleratorModel.M68020);

        private static bool IsRegister(M68kTimingOperandForm form)
            => form is M68kTimingOperandForm.DataRegister or M68kTimingOperandForm.AddressRegister;

        private static bool IsMemory(M68kTimingOperandForm form)
            => form is M68kTimingOperandForm.AddressIndirect or
                M68kTimingOperandForm.PostIncrement or
                M68kTimingOperandForm.Predecrement or
                M68kTimingOperandForm.AddressDisplacement or
                M68kTimingOperandForm.BriefIndexed or
                M68kTimingOperandForm.AbsoluteWord or
                M68kTimingOperandForm.AbsoluteLong;

        public static M68kInstructionPlan CreateFixedPlan(M68kInstructionTimingKey key, int nativeCycles)
        {
            var descriptor = new M68kTimingDescriptor(
                M68kTimingFormulaKind.Compatibility,
                M68kTimingOperation.Other,
                null,
                M68kTimingOperand.None,
                M68kTimingOperand.None,
                M68kTimingBranchOutcome.None,
                RegisterCount: 0,
                M68kTimingBarrier.None,
                key,
                "fixed JIT fallback",
                new M68kTimingPlanShape(nativeCycles, HeadCycles: 0, TailCycles: 0, UsesHeadTail: false));
            return CreatePlan(descriptor);
        }

        public static M68kInstructionPlan CreateMovemLongPlan(
            M68kInstructionTimingKey key,
            string label,
            int registerCount,
            bool registerToMemory,
            M68kAcceleratorModel model,
            int? fixedCycles)
            => CreatePlan(M68kTimingDescriptor.ForMovemLong(
                key,
                label,
                registerCount,
                registerToMemory,
                model,
                fixedCycles));
    }
}
