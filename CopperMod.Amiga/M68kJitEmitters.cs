using System;
using System.Reflection;
using System.Reflection.Emit;

namespace CopperMod.Amiga
{
    internal static class M68kEaEmitter
    {
        public static void EmitEaArguments(ILGenerator il, M68kDecodedEa ea)
        {
            il.Emit(OpCodes.Ldc_I4, (int)ea.Kind);
            il.Emit(OpCodes.Ldc_I4, ea.Register);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)ea.ExtensionAddress));
            il.Emit(OpCodes.Ldc_I4, ea.Extension0);
            il.Emit(OpCodes.Ldc_I4, ea.Extension1);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)ea.Immediate));
        }
    }

    internal static class M68kOperationEmitter
    {
        private const ushort LogicFlags = M68kCpuState.Negative |
            M68kCpuState.Zero |
            M68kCpuState.Overflow |
            M68kCpuState.Carry;
        private const ushort ArithmeticFlags = LogicFlags | M68kCpuState.Extend;

        private static readonly MethodInfo ExecuteDecodedOperation =
            typeof(M68kJitCore).GetMethod(
                "ExecuteCompiledDecodedOperation",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, "ExecuteCompiledDecodedOperation");
        private static readonly PropertyInfo StateProperty = RequiredProperty(typeof(M68kJitCore), nameof(M68kJitCore.State));
        private static readonly PropertyInfo DataRegistersProperty = RequiredProperty(typeof(M68kCpuState), nameof(M68kCpuState.D));
        private static readonly PropertyInfo AddressRegistersProperty = RequiredProperty(typeof(M68kCpuState), nameof(M68kCpuState.A));
        private static readonly PropertyInfo ProgramCounterProperty = RequiredProperty(typeof(M68kCpuState), nameof(M68kCpuState.ProgramCounter));
        private static readonly PropertyInfo CyclesProperty = RequiredProperty(typeof(M68kCpuState), nameof(M68kCpuState.Cycles));
        private static readonly PropertyInfo StatusRegisterProperty = RequiredProperty(typeof(M68kCpuState), nameof(M68kCpuState.StatusRegister));

        public static TraceEmitContext EmitTraceLocals(ILGenerator il)
        {
            var state = il.DeclareLocal(typeof(M68kCpuState));
            var dataRegisters = il.DeclareLocal(typeof(uint[]));
            var addressRegisters = il.DeclareLocal(typeof(uint[]));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, StateProperty.GetMethod!);
            il.Emit(OpCodes.Stloc, state);
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Call, DataRegistersProperty.GetMethod!);
            il.Emit(OpCodes.Stloc, dataRegisters);
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Call, AddressRegistersProperty.GetMethod!);
            il.Emit(OpCodes.Stloc, addressRegisters);

            return new TraceEmitContext(state, dataRegisters, addressRegisters);
        }

        public static bool Emit(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            if (TryEmitDirect(il, instruction, context))
            {
                il.Emit(OpCodes.Ldc_I4_1);
                return true;
            }

            EmitHelper(il, instruction);
            return false;
        }

        public static bool CanEmitDirect(M68kDecodedInstruction instruction)
        {
            return instruction.Operation switch
            {
                M68kJitOperation.Moveq => true,
                M68kJitOperation.Move => instruction.Source.Kind == M68kJitEaKind.DataRegister &&
                    instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Addq or M68kJitOperation.Subq => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Tst => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Cmpi => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Cmp => instruction.Source.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Not or M68kJitOperation.Neg => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Bra or M68kJitOperation.Bcc or M68kJitOperation.Dbcc => true,
                _ => false
            };
        }

        private static void EmitHelper(ILGenerator il, M68kDecodedInstruction instruction)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Operation);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Size);
            M68kEaEmitter.EmitEaArguments(il, instruction.Source);
            M68kEaEmitter.EmitEaArguments(il, instruction.Destination);
            il.Emit(OpCodes.Ldc_I4, instruction.Register);
            il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
            il.Emit(OpCodes.Ldc_I4, instruction.Condition);
            il.Emit(OpCodes.Ldc_I4, instruction.Displacement);
            il.Emit(OpCodes.Ldc_I4, instruction.Variant);
            il.Emit(OpCodes.Ldc_I4, instruction.RegisterMask);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)instruction.BranchBase));
            il.Emit(OpCodes.Call, ExecuteDecodedOperation);
        }

        private static bool TryEmitDirect(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            if (!CanEmitDirect(instruction))
            {
                return false;
            }

            switch (instruction.Operation)
            {
                case M68kJitOperation.Moveq:
                    EmitMoveq(il, instruction, context);
                    return true;
                case M68kJitOperation.Move:
                    EmitMoveDataRegister(il, instruction, context);
                    return true;
                case M68kJitOperation.Addq:
                    EmitQuickArithmetic(il, instruction, context, add: true);
                    return true;
                case M68kJitOperation.Subq:
                    EmitQuickArithmetic(il, instruction, context, add: false);
                    return true;
                case M68kJitOperation.Tst:
                    EmitTstDataRegister(il, instruction, context);
                    return true;
                case M68kJitOperation.Cmpi:
                    EmitCompareImmediateDataRegister(il, instruction, context);
                    return true;
                case M68kJitOperation.Cmp:
                    EmitCompareDataRegisters(il, instruction, context);
                    return true;
                case M68kJitOperation.Not:
                    EmitNotDataRegister(il, instruction, context);
                    return true;
                case M68kJitOperation.Neg:
                    EmitNegDataRegister(il, instruction, context);
                    return true;
                case M68kJitOperation.Bra:
                    EmitBra(il, instruction, context);
                    return true;
                case M68kJitOperation.Bcc:
                    EmitBcc(il, instruction, context);
                    return true;
                case M68kJitOperation.Dbcc:
                    EmitDbcc(il, instruction, context);
                    return true;
                default:
                    return false;
            }
        }

        private static void EmitMoveq(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var value = il.DeclareLocal(typeof(uint));
            il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
            il.Emit(OpCodes.Stloc, value);
            EmitStoreDataRegister(il, context, instruction.Register, value, M68kOperandSize.Long);
            EmitSetLogicFlags(il, context, value, M68kOperandSize.Long);
            EmitAddCycles(il, context, 4);
        }

        private static void EmitMoveDataRegister(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var value = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Source.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, value);
            EmitStoreDataRegister(il, context, instruction.Destination.Register, value, instruction.Size);
            EmitSetLogicFlags(il, context, value, instruction.Size);
            EmitAddCycles(il, context, instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitQuickArithmetic(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context, bool add)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Destination.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            EmitLoadUIntConstant(il, (uint)instruction.QuickValue);
            il.Emit(OpCodes.Stloc, source);
            EmitArithmeticFlagsAndResult(il, context, destination, source, result, instruction.Size, add, setExtend: true);
            EmitStoreDataRegister(il, context, instruction.Destination.Register, result, instruction.Size);
            EmitAddCycles(il, context, instruction.Size == M68kOperandSize.Long ? 8 : 4);
        }

        private static void EmitTstDataRegister(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var value = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Destination.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, value);
            EmitSetLogicFlags(il, context, value, instruction.Size);
            EmitAddCycles(il, context, instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitCompareImmediateDataRegister(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Destination.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            EmitLoadUIntConstant(il, instruction.Source.Immediate);
            il.Emit(OpCodes.Stloc, source);
            EmitArithmeticFlagsAndResult(il, context, destination, source, result, instruction.Size, add: false, setExtend: false);
            EmitAddCycles(il, context, instruction.Size == M68kOperandSize.Long ? 16 : 8);
        }

        private static void EmitCompareDataRegisters(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            EmitLoadDataRegister(il, context, instruction.Source.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, source);
            EmitArithmeticFlagsAndResult(il, context, destination, source, result, instruction.Size, add: false, setExtend: false);
            EmitAddCycles(il, context, instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitNotDataRegister(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var value = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Destination.Register, instruction.Size);
            EmitLoadUIntConstant(il, GetMask(instruction.Size));
            il.Emit(OpCodes.Xor);
            EmitLoadUIntConstant(il, GetMask(instruction.Size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, value);
            EmitStoreDataRegister(il, context, instruction.Destination.Register, value, instruction.Size);
            EmitSetLogicFlags(il, context, value, instruction.Size);
            EmitAddCycles(il, context, instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitNegDataRegister(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, destination);
            EmitLoadDataRegister(il, context, instruction.Destination.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, source);
            EmitArithmeticFlagsAndResult(il, context, destination, source, result, instruction.Size, add: false, setExtend: true);
            EmitStoreDataRegister(il, context, instruction.Destination.Register, result, instruction.Size);
            EmitAddCycles(il, context, instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitBra(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            EmitSetProgramCounter(il, context, GetBranchTarget(instruction));
            EmitAddCycles(il, context, 10);
        }

        private static void EmitBcc(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var notTaken = il.DefineLabel();
            var done = il.DefineLabel();
            EmitCondition(il, context, instruction.Condition);
            il.Emit(OpCodes.Brfalse, notTaken);
            EmitSetProgramCounter(il, context, GetBranchTarget(instruction));
            EmitAddCycles(il, context, 10);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(notTaken);
            EmitAddCycles(il, context, 8);
            il.MarkLabel(done);
        }

        private static void EmitDbcc(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var conditionTrue = il.DefineLabel();
            var expired = il.DefineLabel();
            var done = il.DefineLabel();
            var counter = il.DeclareLocal(typeof(uint));

            EmitCondition(il, context, instruction.Condition);
            il.Emit(OpCodes.Brtrue, conditionTrue);

            EmitLoadDataRegister(il, context, instruction.Register, M68kOperandSize.Word);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Ldc_I4, 0xFFFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, counter);
            EmitStoreDataRegister(il, context, instruction.Register, counter, M68kOperandSize.Word);

            il.Emit(OpCodes.Ldloc, counter);
            il.Emit(OpCodes.Ldc_I4, 0xFFFF);
            il.Emit(OpCodes.Beq, expired);
            EmitSetProgramCounter(il, context, GetBranchTarget(instruction));
            EmitAddCycles(il, context, 10);
            il.Emit(OpCodes.Br, done);

            il.MarkLabel(expired);
            EmitAddCycles(il, context, 14);
            il.Emit(OpCodes.Br, done);

            il.MarkLabel(conditionTrue);
            EmitAddCycles(il, context, 12);
            il.MarkLabel(done);
        }

        private static void EmitLoadDataRegister(
            ILGenerator il,
            TraceEmitContext context,
            int register,
            M68kOperandSize size)
        {
            il.Emit(OpCodes.Ldloc, context.DataRegisters);
            il.Emit(OpCodes.Ldc_I4, register);
            il.Emit(OpCodes.Ldelem_U4);
            var mask = GetMask(size);
            if (mask != uint.MaxValue)
            {
                EmitLoadUIntConstant(il, mask);
                il.Emit(OpCodes.And);
            }
        }

        private static void EmitStoreDataRegister(
            ILGenerator il,
            TraceEmitContext context,
            int register,
            LocalBuilder value,
            M68kOperandSize size)
        {
            il.Emit(OpCodes.Ldloc, context.DataRegisters);
            il.Emit(OpCodes.Ldc_I4, register);
            if (size == M68kOperandSize.Long)
            {
                il.Emit(OpCodes.Ldloc, value);
            }
            else
            {
                var mask = GetMask(size);
                il.Emit(OpCodes.Ldloc, context.DataRegisters);
                il.Emit(OpCodes.Ldc_I4, register);
                il.Emit(OpCodes.Ldelem_U4);
                EmitLoadUIntConstant(il, ~mask);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Ldloc, value);
                EmitLoadUIntConstant(il, mask);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Or);
            }

            il.Emit(OpCodes.Stelem_I4);
        }

        private static void EmitSetLogicFlags(
            ILGenerator il,
            TraceEmitContext context,
            LocalBuilder value,
            M68kOperandSize size)
        {
            var status = il.DeclareLocal(typeof(int));
            var notZero = il.DefineLabel();
            var notNegative = il.DefineLabel();
            EmitLoadStatus(il, context);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)~LogicFlags));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, status);

            il.Emit(OpCodes.Ldloc, value);
            EmitLoadUIntConstant(il, GetMask(size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brtrue, notZero);
            EmitOrStatusFlag(il, status, M68kCpuState.Zero);
            il.MarkLabel(notZero);

            il.Emit(OpCodes.Ldloc, value);
            EmitLoadUIntConstant(il, GetSignBit(size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brfalse, notNegative);
            EmitOrStatusFlag(il, status, M68kCpuState.Negative);
            il.MarkLabel(notNegative);
            EmitStoreStatus(il, context, status);
        }

        private static void EmitArithmeticFlagsAndResult(
            ILGenerator il,
            TraceEmitContext context,
            LocalBuilder destination,
            LocalBuilder source,
            LocalBuilder result,
            M68kOperandSize size,
            bool add,
            bool setExtend)
        {
            var mask = GetMask(size);
            var sign = GetSignBit(size);
            var status = il.DeclareLocal(typeof(int));
            var noZero = il.DefineLabel();
            var noNegative = il.DefineLabel();
            var noOverflow = il.DefineLabel();
            var noCarry = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, destination);
            EmitLoadUIntConstant(il, mask);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, destination);
            il.Emit(OpCodes.Ldloc, source);
            EmitLoadUIntConstant(il, mask);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, source);

            if (add)
            {
                var full = il.DeclareLocal(typeof(ulong));
                il.Emit(OpCodes.Ldloc, destination);
                il.Emit(OpCodes.Conv_U8);
                il.Emit(OpCodes.Ldloc, source);
                il.Emit(OpCodes.Conv_U8);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, full);
                il.Emit(OpCodes.Ldloc, full);
                il.Emit(OpCodes.Conv_U4);
                EmitLoadUIntConstant(il, mask);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Stloc, result);

                EmitLoadStatus(il, context);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)~(setExtend ? ArithmeticFlags : LogicFlags)));
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Stloc, status);
                EmitNegativeZeroFlags(il, status, result, size, noZero, noNegative);

                il.Emit(OpCodes.Ldloc, destination);
                il.Emit(OpCodes.Ldloc, source);
                il.Emit(OpCodes.Xor);
                il.Emit(OpCodes.Not);
                il.Emit(OpCodes.Ldloc, destination);
                il.Emit(OpCodes.Ldloc, result);
                il.Emit(OpCodes.Xor);
                il.Emit(OpCodes.And);
                EmitLoadUIntConstant(il, sign);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Brfalse, noOverflow);
                EmitOrStatusFlag(il, status, M68kCpuState.Overflow);
                il.MarkLabel(noOverflow);

                il.Emit(OpCodes.Ldloc, full);
                il.Emit(OpCodes.Ldc_I8, unchecked((long)(ulong)mask));
                il.Emit(OpCodes.Cgt_Un);
                il.Emit(OpCodes.Brfalse, noCarry);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, destination);
                il.Emit(OpCodes.Ldloc, source);
                il.Emit(OpCodes.Sub);
                EmitLoadUIntConstant(il, mask);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Stloc, result);

                EmitLoadStatus(il, context);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)~(setExtend ? ArithmeticFlags : LogicFlags)));
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Stloc, status);
                EmitNegativeZeroFlags(il, status, result, size, noZero, noNegative);

                il.Emit(OpCodes.Ldloc, destination);
                il.Emit(OpCodes.Ldloc, source);
                il.Emit(OpCodes.Xor);
                il.Emit(OpCodes.Ldloc, destination);
                il.Emit(OpCodes.Ldloc, result);
                il.Emit(OpCodes.Xor);
                il.Emit(OpCodes.And);
                EmitLoadUIntConstant(il, sign);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Brfalse, noOverflow);
                EmitOrStatusFlag(il, status, M68kCpuState.Overflow);
                il.MarkLabel(noOverflow);

                il.Emit(OpCodes.Ldloc, source);
                il.Emit(OpCodes.Ldloc, destination);
                il.Emit(OpCodes.Cgt_Un);
                il.Emit(OpCodes.Brfalse, noCarry);
            }

            EmitOrStatusFlag(il, status, M68kCpuState.Carry);
            if (setExtend)
            {
                EmitOrStatusFlag(il, status, M68kCpuState.Extend);
            }

            il.MarkLabel(noCarry);
            EmitStoreStatus(il, context, status);
        }

        private static void EmitNegativeZeroFlags(
            ILGenerator il,
            LocalBuilder status,
            LocalBuilder value,
            M68kOperandSize size,
            Label noZero,
            Label noNegative)
        {
            il.Emit(OpCodes.Ldloc, value);
            EmitLoadUIntConstant(il, GetMask(size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brtrue, noZero);
            EmitOrStatusFlag(il, status, M68kCpuState.Zero);
            il.MarkLabel(noZero);

            il.Emit(OpCodes.Ldloc, value);
            EmitLoadUIntConstant(il, GetSignBit(size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brfalse, noNegative);
            EmitOrStatusFlag(il, status, M68kCpuState.Negative);
            il.MarkLabel(noNegative);
        }

        private static void EmitCondition(ILGenerator il, TraceEmitContext context, int condition)
        {
            switch (condition)
            {
                case 0x0:
                    il.Emit(OpCodes.Ldc_I4_1);
                    return;
                case 0x1:
                    il.Emit(OpCodes.Ldc_I4_0);
                    return;
                case 0x2:
                    EmitFlag(il, context, M68kCpuState.Carry);
                    EmitNotBool(il);
                    EmitFlag(il, context, M68kCpuState.Zero);
                    EmitNotBool(il);
                    il.Emit(OpCodes.And);
                    return;
                case 0x3:
                    EmitFlag(il, context, M68kCpuState.Carry);
                    EmitFlag(il, context, M68kCpuState.Zero);
                    il.Emit(OpCodes.Or);
                    return;
                case 0x4:
                    EmitFlag(il, context, M68kCpuState.Carry);
                    EmitNotBool(il);
                    return;
                case 0x5:
                    EmitFlag(il, context, M68kCpuState.Carry);
                    return;
                case 0x6:
                    EmitFlag(il, context, M68kCpuState.Zero);
                    EmitNotBool(il);
                    return;
                case 0x7:
                    EmitFlag(il, context, M68kCpuState.Zero);
                    return;
                case 0x8:
                    EmitFlag(il, context, M68kCpuState.Overflow);
                    EmitNotBool(il);
                    return;
                case 0x9:
                    EmitFlag(il, context, M68kCpuState.Overflow);
                    return;
                case 0xA:
                    EmitFlag(il, context, M68kCpuState.Negative);
                    EmitNotBool(il);
                    return;
                case 0xB:
                    EmitFlag(il, context, M68kCpuState.Negative);
                    return;
                case 0xC:
                    EmitNegativeEqualsOverflow(il, context);
                    return;
                case 0xD:
                    EmitNegativeEqualsOverflow(il, context);
                    EmitNotBool(il);
                    return;
                case 0xE:
                    EmitFlag(il, context, M68kCpuState.Zero);
                    EmitNotBool(il);
                    EmitNegativeEqualsOverflow(il, context);
                    il.Emit(OpCodes.And);
                    return;
                case 0xF:
                    EmitFlag(il, context, M68kCpuState.Zero);
                    EmitNegativeEqualsOverflow(il, context);
                    EmitNotBool(il);
                    il.Emit(OpCodes.Or);
                    return;
                default:
                    il.Emit(OpCodes.Ldc_I4_0);
                    return;
            }
        }

        private static void EmitNegativeEqualsOverflow(ILGenerator il, TraceEmitContext context)
        {
            EmitFlag(il, context, M68kCpuState.Negative);
            EmitFlag(il, context, M68kCpuState.Overflow);
            il.Emit(OpCodes.Ceq);
        }

        private static void EmitFlag(ILGenerator il, TraceEmitContext context, ushort flag)
        {
            EmitLoadStatus(il, context);
            il.Emit(OpCodes.Ldc_I4, flag);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Cgt_Un);
        }

        private static void EmitNotBool(ILGenerator il)
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
        }

        private static void EmitOrStatusFlag(ILGenerator il, LocalBuilder status, ushort flag)
        {
            il.Emit(OpCodes.Ldloc, status);
            il.Emit(OpCodes.Ldc_I4, flag);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Stloc, status);
        }

        private static void EmitLoadStatus(ILGenerator il, TraceEmitContext context)
        {
            il.Emit(OpCodes.Ldloc, context.State);
            il.Emit(OpCodes.Call, StatusRegisterProperty.GetMethod!);
        }

        private static void EmitStoreStatus(ILGenerator il, TraceEmitContext context, LocalBuilder status)
        {
            il.Emit(OpCodes.Ldloc, context.State);
            il.Emit(OpCodes.Ldloc, status);
            il.Emit(OpCodes.Conv_U2);
            il.Emit(OpCodes.Call, StatusRegisterProperty.SetMethod!);
        }

        private static void EmitSetProgramCounter(ILGenerator il, TraceEmitContext context, uint value)
        {
            il.Emit(OpCodes.Ldloc, context.State);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)value));
            il.Emit(OpCodes.Call, ProgramCounterProperty.SetMethod!);
        }

        private static void EmitAddCycles(ILGenerator il, TraceEmitContext context, int cycles)
        {
            il.Emit(OpCodes.Ldloc, context.State);
            il.Emit(OpCodes.Ldloc, context.State);
            il.Emit(OpCodes.Call, CyclesProperty.GetMethod!);
            il.Emit(OpCodes.Ldc_I8, (long)cycles);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Call, CyclesProperty.SetMethod!);
        }

        private static void EmitLoadUIntConstant(ILGenerator il, uint value)
        {
            il.Emit(OpCodes.Ldc_I4, unchecked((int)value));
        }

        private static uint GetMask(M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => 0xFF,
                M68kOperandSize.Word => 0xFFFF,
                _ => 0xFFFF_FFFF
            };
        }

        private static uint GetSignBit(M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => 0x80,
                M68kOperandSize.Word => 0x8000,
                _ => 0x8000_0000
            };
        }

        private static uint GetBranchTarget(M68kDecodedInstruction instruction)
            => Normalize(instruction.BranchBase + unchecked((uint)instruction.Displacement));

        private static uint Normalize(uint address)
            => address & 0x00FF_FFFF;

        private static PropertyInfo RequiredProperty(Type type, string name)
            => type.GetProperty(name) ?? throw new MissingMemberException(type.FullName, name);

        internal readonly struct TraceEmitContext
        {
            public TraceEmitContext(LocalBuilder state, LocalBuilder dataRegisters, LocalBuilder addressRegisters)
            {
                State = state;
                DataRegisters = dataRegisters;
                AddressRegisters = addressRegisters;
            }

            public LocalBuilder State { get; }

            public LocalBuilder DataRegisters { get; }

            public LocalBuilder AddressRegisters { get; }
        }
    }
}
