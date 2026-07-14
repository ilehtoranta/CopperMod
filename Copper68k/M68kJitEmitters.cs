/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Copper68k
{
    internal enum M68kIlInstructionKind
    {
        Helper,
        DirectCpu,
        DirectMemory,
        SpecializedHelper
    }

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

        private static readonly MethodInfo ExecuteDecodedOperation = RequiredMethod(
            typeof(M68kJitCore),
            "ExecuteCompiledDecodedOperation",
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(uint),
            typeof(ushort),
            typeof(ushort),
            typeof(uint),
            typeof(int),
            typeof(int),
            typeof(uint),
            typeof(ushort),
            typeof(ushort),
            typeof(uint),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(ushort),
            typeof(uint));
        private static readonly MethodInfo ExecuteM68040Fpu = RequiredMethod(
            typeof(M68kJitCore),
            "ExecuteCompiledM68040Fpu",
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(uint),
            typeof(ushort),
            typeof(ushort),
            typeof(uint),
            typeof(int),
            typeof(int),
            typeof(uint),
            typeof(ushort),
            typeof(ushort),
            typeof(uint),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(ushort));
        private static readonly MethodInfo ExecuteM68040FpuRegister = RequiredMethod(
            typeof(M68kJitCore),
            "ExecuteCompiledM68040FpuRegister",
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(ushort));
        private static readonly MethodInfo ExecuteMovem = RequiredMethod(
            typeof(M68kJitCore),
            "ExecuteCompiledMovem",
            typeof(int),
            typeof(int),
            typeof(uint),
            typeof(ushort),
            typeof(ushort),
            typeof(int),
            typeof(ushort),
            typeof(int));
        private static readonly MethodInfo ExecuteJumpTo = RequiredMethod(
            typeof(M68kJitCore),
            "ExecuteCompiledJumpTo",
            typeof(uint),
            typeof(bool),
            typeof(int));
        private static readonly MethodInfo ExecuteRegisterShift = RequiredMethod(
            typeof(M68kJitCore),
            "ExecuteCompiledRegisterShift",
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int));
        private static readonly MethodInfo ExecuteMultiplyDivideValue = RequiredMethod(
            typeof(M68kJitCore),
            "ExecuteCompiledMultiplyDivideValue",
            typeof(uint),
            typeof(int),
            typeof(bool),
            typeof(bool),
            typeof(int));
        private static readonly MethodInfo ReadMemoryByte = RequiredMethod(
            typeof(M68kJitCore),
            "ReadClassicCompiledMemoryByte",
            typeof(uint));
        private static readonly MethodInfo ReadMemoryWord = RequiredMethod(
            typeof(M68kJitCore),
            "ReadClassicCompiledMemoryWord",
            typeof(uint));
        private static readonly MethodInfo ReadMemoryLong = RequiredMethod(
            typeof(M68kJitCore),
            "ReadClassicCompiledMemoryLong",
            typeof(uint));
        private static readonly MethodInfo WriteMemoryByte = RequiredMethod(
            typeof(M68kJitCore),
            "WriteClassicCompiledMemoryByte",
            typeof(uint),
            typeof(uint));
        private static readonly MethodInfo WriteMemoryWord = RequiredMethod(
            typeof(M68kJitCore),
            "WriteClassicCompiledMemoryWord",
            typeof(uint),
            typeof(uint));
        private static readonly MethodInfo WriteMemoryLong = RequiredMethod(
            typeof(M68kJitCore),
            "WriteClassicCompiledMemoryLong",
            typeof(uint),
            typeof(uint));
        private static readonly MethodInfo SetAddressRegister = RequiredMethod(
            typeof(M68kJitCore),
            "SetAddressRegister",
            typeof(int),
            typeof(uint));
        private static readonly MethodInfo PushLong = RequiredMethod(typeof(M68kJitCore), "PushLong", typeof(uint));
        private static readonly MethodInfo PullLong = RequiredMethod(typeof(M68kJitCore), "PullLong");
        private static readonly MethodInfo ResolveIndex = RequiredMethod(typeof(M68kJitCore), "ResolveIndex", typeof(ushort));
        private static readonly MethodInfo CompleteCompiledInstructionCycles = RequiredMethod(
            typeof(M68kJitCore),
            "CompleteCompiledInstructionCycles",
            typeof(int));
        private static readonly MethodInfo Negx = RequiredStaticMethod(
            typeof(M68kIntegerSemantics),
            nameof(M68kIntegerSemantics.Negx),
            typeof(uint),
            typeof(int),
            typeof(int));

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

        public static M68kIlInstructionKind Emit(
            ILGenerator il,
            M68kDecodedInstruction instruction,
            TraceEmitContext context)
        {
            var kind = GetInstructionKind(instruction);
            switch (kind)
            {
                case M68kIlInstructionKind.DirectCpu:
                case M68kIlInstructionKind.DirectMemory:
                    EmitDirect(il, instruction, context);
                    break;
                case M68kIlInstructionKind.SpecializedHelper:
                    EmitSpecializedHelper(il, instruction);
                    break;
                default:
                    EmitHelper(il, instruction);
                    break;
            }

            return kind;
        }

        public static bool CanEmitDirect(M68kDecodedInstruction instruction)
            => GetInstructionKind(instruction) != M68kIlInstructionKind.Helper;

        public static bool CanEmitPureCpuBatch(M68kDecodedInstruction instruction)
            => GetInstructionKind(instruction) == M68kIlInstructionKind.DirectCpu;

        public static M68kIlInstructionKind GetInstructionKind(M68kDecodedInstruction instruction)
        {
            if (CanEmitDirectCpu(instruction))
            {
                return M68kIlInstructionKind.DirectCpu;
            }

            if (CanEmitDirectMemory(instruction))
            {
                return M68kIlInstructionKind.DirectMemory;
            }

            if (CanEmitSpecializedHelper(instruction))
            {
                return M68kIlInstructionKind.SpecializedHelper;
            }

            return M68kIlInstructionKind.Helper;
        }

        private static bool CanEmitDirectCpu(M68kDecodedInstruction instruction)
        {
            return instruction.Operation switch
            {
                M68kJitOperation.Moveq => true,
                M68kJitOperation.Move => instruction.Source.Kind == M68kJitEaKind.DataRegister &&
                    instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Addq or M68kJitOperation.Subq => instruction.Destination.Kind is
                    M68kJitEaKind.DataRegister,
                M68kJitOperation.Tst => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Cmpi => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Cmp => instruction.Source.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Not or M68kJitOperation.Neg or M68kJitOperation.Negx => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.M68040Fpu => !M68040FpuHelpers.UsesBus(instruction),
                M68kJitOperation.Bra or M68kJitOperation.Bcc or M68kJitOperation.Dbcc => true,
                _ => false
            };
        }

        private static bool CanEmitDirectMemory(M68kDecodedInstruction instruction)
        {
            return instruction.Operation switch
            {
                M68kJitOperation.Move => IsSupportedReadEa(instruction.Source) &&
                    IsSupportedWriteEa(instruction.Destination),
                M68kJitOperation.Movea => instruction.Destination.Kind == M68kJitEaKind.AddressRegister &&
                    IsSupportedReadEa(instruction.Source),
                M68kJitOperation.Lea => IsSupportedAddressEa(instruction.Source),
                M68kJitOperation.Addq or M68kJitOperation.Subq => instruction.Destination.Kind == M68kJitEaKind.AddressRegister ||
                    IsSupportedReadWriteEa(instruction.Destination),
                M68kJitOperation.Tst => IsSupportedReadEa(instruction.Destination),
                M68kJitOperation.Cmpi => IsSupportedReadEa(instruction.Destination),
                M68kJitOperation.Cmp => instruction.Source.Kind != M68kJitEaKind.AddressRegister &&
                    IsSupportedReadEa(instruction.Source),
                M68kJitOperation.Cmpa => IsSupportedReadEa(instruction.Source),
                M68kJitOperation.Cmpm => instruction.Source.Kind == M68kJitEaKind.AddressPostincrement &&
                    instruction.Destination.Kind == M68kJitEaKind.AddressPostincrement,
                M68kJitOperation.Add or M68kJitOperation.Sub => instruction.Variant == 2
                    ? IsSupportedReadEa(instruction.Source)
                    : IsSupportedBinaryArithmeticEa(instruction),
                M68kJitOperation.Addi or M68kJitOperation.Subi or
                M68kJitOperation.Andi or M68kJitOperation.Ori or M68kJitOperation.Eori => IsSupportedReadWriteEa(instruction.Destination),
                M68kJitOperation.And or M68kJitOperation.Or or M68kJitOperation.Eor => IsSupportedBinaryArithmeticEa(instruction),
                M68kJitOperation.Not or M68kJitOperation.Neg or M68kJitOperation.Negx => IsSupportedReadWriteEa(instruction.Destination),
                M68kJitOperation.M68040Move16 => true,
                M68kJitOperation.M68040Fpu => M68040FpuHelpers.UsesBus(instruction),
                _ => false
            };
        }

        private static bool CanEmitSpecializedHelper(M68kDecodedInstruction instruction)
            => instruction.Operation == M68kJitOperation.Movem;

        private static bool IsRegisterArithmetic(M68kDecodedInstruction instruction)
        {
            if (instruction.Variant != 0)
            {
                return instruction.Destination.Kind == M68kJitEaKind.DataRegister;
            }

            return IsRegisterOrImmediate(instruction.Source);
        }

        private static bool IsRegisterLogical(M68kDecodedInstruction instruction)
        {
            if (instruction.Operation == M68kJitOperation.Eor || instruction.Variant != 0)
            {
                return instruction.Destination.Kind == M68kJitEaKind.DataRegister;
            }

            return IsRegisterOrImmediate(instruction.Source);
        }

        private static bool IsSupportedBinaryArithmeticEa(M68kDecodedInstruction instruction)
        {
            if (instruction.Operation == M68kJitOperation.Eor || instruction.Variant != 0)
            {
                return IsSupportedReadWriteEa(instruction.Destination);
            }

            return IsSupportedReadEa(instruction.Source);
        }

        private static bool IsRegisterOrImmediate(M68kDecodedEa ea)
            => ea.Kind is M68kJitEaKind.DataRegister or M68kJitEaKind.AddressRegister or M68kJitEaKind.Immediate;

        private static bool IsSupportedReadEa(M68kDecodedEa ea)
            => IsRegisterOrImmediate(ea) || IsSupportedAddressEa(ea);

        private static bool IsSupportedWriteEa(M68kDecodedEa ea)
            => ea.Kind is M68kJitEaKind.DataRegister or M68kJitEaKind.AddressRegister ||
                (IsSupportedAddressEa(ea) && ea.Kind != M68kJitEaKind.PcDisplacement);

        private static bool IsSupportedReadWriteEa(M68kDecodedEa ea)
            => IsSupportedWriteEa(ea);

        private static bool IsSupportedAddressEa(M68kDecodedEa ea)
            => ea.IsMemory && ea.Kind is not M68kJitEaKind.AddressIndex and not M68kJitEaKind.PcIndex;

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

        private static void EmitSpecializedHelper(ILGenerator il, M68kDecodedInstruction instruction)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Source.Kind);
            il.Emit(OpCodes.Ldc_I4, instruction.Source.Register);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)instruction.Source.ExtensionAddress));
            il.Emit(OpCodes.Ldc_I4, instruction.Source.Extension0);
            il.Emit(OpCodes.Ldc_I4, instruction.Source.Extension1);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Size);
            il.Emit(OpCodes.Ldc_I4, instruction.RegisterMask);
            il.Emit(OpCodes.Ldc_I4, instruction.Variant);
            il.Emit(OpCodes.Call, ExecuteMovem);
        }

        private static void EmitDirect(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            switch (instruction.Operation)
            {
                case M68kJitOperation.Moveq:
                    EmitMoveq(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Move:
                    EmitMove(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Movea:
                    EmitMovea(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Lea:
                    EmitLea(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Addq:
                    EmitQuickArithmetic(il, instruction, context, add: true);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Subq:
                    EmitQuickArithmetic(il, instruction, context, add: false);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Tst:
                    EmitTst(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Cmp:
                    EmitCompare(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Cmpi:
                    EmitCompareImmediate(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Cmpa:
                    EmitAddressArithmetic(il, instruction, context, compareOnly: true, add: false);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Cmpm:
                    EmitCmpm(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Add:
                    if (instruction.Variant == 2)
                    {
                        EmitAddressArithmetic(il, instruction, context, compareOnly: false, add: true);
                    }
                    else
                    {
                        EmitBinaryArithmetic(il, instruction, context, add: true);
                    }

                    EmitTrue(il);
                    return;
                case M68kJitOperation.Sub:
                    if (instruction.Variant == 2)
                    {
                        EmitAddressArithmetic(il, instruction, context, compareOnly: false, add: false);
                    }
                    else
                    {
                        EmitBinaryArithmetic(il, instruction, context, add: false);
                    }

                    EmitTrue(il);
                    return;
                case M68kJitOperation.Addi:
                    EmitImmediateArithmetic(il, instruction, context, add: true, logicalOperation: 0);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Subi:
                    EmitImmediateArithmetic(il, instruction, context, add: false, logicalOperation: 0);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.And:
                    EmitBinaryLogical(il, instruction, context, operation: 0);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Or:
                    EmitBinaryLogical(il, instruction, context, operation: 1);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Eor:
                    EmitBinaryLogical(il, instruction, context, operation: 2);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Andi:
                    EmitImmediateArithmetic(il, instruction, context, add: false, logicalOperation: 1);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Ori:
                    EmitImmediateArithmetic(il, instruction, context, add: false, logicalOperation: 2);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Eori:
                    EmitImmediateArithmetic(il, instruction, context, add: false, logicalOperation: 3);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Not:
                    EmitNot(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Neg:
                    EmitNeg(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Negx:
                    EmitNegx(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.ExtWord:
                    EmitExt(il, instruction, context, M68kOperandSize.Word);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.ExtLong:
                    EmitExt(il, instruction, context, M68kOperandSize.Long);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Swap:
                    EmitSwap(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Exg:
                    EmitExchange(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.ShiftRegister:
                    EmitRegisterShift(il, instruction);
                    return;
                case M68kJitOperation.Mulu:
                    EmitMultiplyDivide(il, instruction, context, signed: false, divide: false);
                    return;
                case M68kJitOperation.Muls:
                    EmitMultiplyDivide(il, instruction, context, signed: true, divide: false);
                    return;
                case M68kJitOperation.Divu:
                    EmitMultiplyDivide(il, instruction, context, signed: false, divide: true);
                    return;
                case M68kJitOperation.Divs:
                    EmitMultiplyDivide(il, instruction, context, signed: true, divide: true);
                    return;
                case M68kJitOperation.Bra:
                    EmitBra(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Bcc:
                    EmitBcc(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Bsr:
                    EmitBsr(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Dbcc:
                    EmitDbcc(il, instruction, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.Jmp:
                    EmitJump(il, instruction, context, link: false);
                    return;
                case M68kJitOperation.Jsr:
                    EmitJump(il, instruction, context, link: true);
                    return;
                case M68kJitOperation.Rts:
                    EmitRts(il, context);
                    EmitTrue(il);
                    return;
                case M68kJitOperation.M68040Fpu:
                    EmitM68040Fpu(il, instruction);
                    return;
                default:
                    EmitHelper(il, instruction);
                    return;
            }
        }

        private static void EmitM68040Fpu(ILGenerator il, M68kDecodedInstruction instruction)
        {
            if ((M68040FpuJitKind)instruction.Variant == M68040FpuJitKind.Operation &&
                instruction.Displacement == 0)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
                il.Emit(OpCodes.Ldc_I4, instruction.Register);
                il.Emit(OpCodes.Ldc_I4, instruction.Condition);
                il.Emit(OpCodes.Ldc_I4, instruction.RegisterMask);
                il.Emit(OpCodes.Call, ExecuteM68040FpuRegister);
                return;
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, instruction.Variant);
            M68kEaEmitter.EmitEaArguments(il, instruction.Source);
            M68kEaEmitter.EmitEaArguments(il, instruction.Destination);
            il.Emit(OpCodes.Ldc_I4, instruction.Register);
            il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
            il.Emit(OpCodes.Ldc_I4, instruction.Condition);
            il.Emit(OpCodes.Ldc_I4, instruction.Displacement);
            il.Emit(OpCodes.Ldc_I4, instruction.RegisterMask);
            il.Emit(OpCodes.Call, ExecuteM68040Fpu);
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
            EmitAddCycles(il, context, M68kJitCore.EstimateMoveCycles(instruction.Source, instruction.Destination, instruction.Size));
        }

        private static void EmitQuickDataRegisterArithmetic(
            ILGenerator il,
            M68kDecodedInstruction instruction,
            TraceEmitContext context,
            bool add)
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
            EmitAddCycles(il, context, M68kJitCore.GetUnaryCyclesForTiming(M68kJitOperation.Tst, instruction.Destination, instruction.Size));
        }

        private static void EmitCompareImmediateDataRegister(
            ILGenerator il,
            M68kDecodedInstruction instruction,
            TraceEmitContext context)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Destination.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            EmitLoadUIntConstant(il, instruction.Source.Immediate);
            il.Emit(OpCodes.Stloc, source);
            EmitArithmeticFlagsAndResult(il, context, destination, source, result, instruction.Size, add: false, setExtend: false);
            EmitAddCycles(il, context, M68kJitCore.GetCmpiCyclesForTiming(instruction.Destination, instruction.Size));
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
            EmitAddCycles(il, context, M68kJitCore.GetCompareCyclesForTiming(instruction.Source, instruction.Size));
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
            EmitAddCycles(il, context, M68kJitCore.GetUnaryCyclesForTiming(M68kJitOperation.Not, instruction.Destination, instruction.Size));
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
            EmitAddCycles(il, context, M68kJitCore.GetUnaryCyclesForTiming(M68kJitOperation.Neg, instruction.Destination, instruction.Size));
        }

        private static void EmitMove(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var value = il.DeclareLocal(typeof(uint));
            EmitLoadEaValue(il, context, instruction.Source, instruction.Size, applySideEffects: true);
            il.Emit(OpCodes.Stloc, value);
            EmitStoreEaValue(il, context, instruction.Destination, instruction.Size, value);
            EmitSetLogicFlags(il, context, value, instruction.Size);
            EmitAddCycles(il, context, M68kJitCore.EstimateMoveCycles(instruction.Source, instruction.Destination, instruction.Size));
        }

        private static void EmitMovea(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var value = il.DeclareLocal(typeof(uint));
            EmitLoadEaValue(il, context, instruction.Source, instruction.Size, applySideEffects: true);
            if (instruction.Size == M68kOperandSize.Word)
            {
                il.Emit(OpCodes.Conv_I2);
                il.Emit(OpCodes.Conv_U4);
            }

            il.Emit(OpCodes.Stloc, value);
            EmitStoreAddressRegister(il, instruction.Destination.Register, value);
            EmitAddCycles(il, context, M68kJitCore.EstimateMoveCycles(instruction.Source, instruction.Destination, instruction.Size));
        }

        private static void EmitLea(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var address = il.DeclareLocal(typeof(uint));
            EmitResolveEaAddress(il, context, instruction.Source, instruction.Size, applySideEffects: false);
            il.Emit(OpCodes.Stloc, address);
            EmitStoreAddressRegister(il, instruction.Register, address);
            EmitAddCycles(il, context, M68kJitCore.GetLeaCyclesForTiming(instruction.Source));
        }

        private static void EmitQuickArithmetic(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context, bool add)
        {
            if (instruction.Destination.Kind == M68kJitEaKind.AddressRegister)
            {
                var value = il.DeclareLocal(typeof(uint));
                EmitLoadAddressRegister(il, context, instruction.Destination.Register);
                EmitLoadUIntConstant(il, M68kCpuState.SignExtend((uint)instruction.QuickValue, instruction.Size));
                il.Emit(add ? OpCodes.Add : OpCodes.Sub);
                il.Emit(OpCodes.Stloc, value);
                EmitStoreAddressRegister(il, instruction.Destination.Register, value);
                EmitAddCycles(il, context, 8);
                return;
            }

            EmitArithmeticReadModifyWrite(
                il,
                context,
                instruction.Destination,
                instruction.Size,
                emitSource: () => EmitLoadUIntConstant(il, (uint)instruction.QuickValue),
                add,
                setExtend: true,
                instruction.Size == M68kOperandSize.Long ? 8 : 4);
        }

        private static void EmitTst(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var value = il.DeclareLocal(typeof(uint));
            EmitLoadEaValue(il, context, instruction.Destination, instruction.Size, applySideEffects: true);
            il.Emit(OpCodes.Stloc, value);
            EmitSetLogicFlags(il, context, value, instruction.Size);
            EmitAddCycles(il, context, M68kJitCore.GetUnaryCyclesForTiming(M68kJitOperation.Tst, instruction.Destination, instruction.Size));
        }

        private static void EmitCompare(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            EmitLoadEaValue(il, context, instruction.Source, instruction.Size, applySideEffects: true);
            il.Emit(OpCodes.Stloc, source);
            EmitArithmeticFlagsAndResult(il, context, destination, source, result, instruction.Size, add: false, setExtend: false);
            EmitAddCycles(il, context, M68kJitCore.GetCompareCyclesForTiming(instruction.Source, instruction.Size));
        }

        private static void EmitCompareImmediate(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitLoadEaValue(il, context, instruction.Destination, instruction.Size, applySideEffects: true);
            il.Emit(OpCodes.Stloc, destination);
            EmitLoadUIntConstant(il, instruction.Source.Immediate);
            il.Emit(OpCodes.Stloc, source);
            EmitArithmeticFlagsAndResult(il, context, destination, source, result, instruction.Size, add: false, setExtend: false);
            EmitAddCycles(il, context, M68kJitCore.GetCmpiCyclesForTiming(instruction.Destination, instruction.Size));
        }

        private static void EmitAddressArithmetic(
            ILGenerator il,
            M68kDecodedInstruction instruction,
            TraceEmitContext context,
            bool compareOnly,
            bool add)
        {
            var source = il.DeclareLocal(typeof(uint));
            var destination = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitLoadEaValue(il, context, instruction.Source, instruction.Size, applySideEffects: true);
            if (instruction.Size == M68kOperandSize.Word)
            {
                il.Emit(OpCodes.Conv_I2);
                il.Emit(OpCodes.Conv_U4);
            }

            il.Emit(OpCodes.Stloc, source);
            EmitLoadAddressRegister(il, context, instruction.Register);
            il.Emit(OpCodes.Stloc, destination);
            if (compareOnly)
            {
                EmitArithmeticFlagsAndResult(il, context, destination, source, result, M68kOperandSize.Long, add: false, setExtend: false);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, destination);
                il.Emit(OpCodes.Ldloc, source);
                il.Emit(add ? OpCodes.Add : OpCodes.Sub);
                il.Emit(OpCodes.Stloc, result);
                EmitStoreAddressRegister(il, instruction.Register, result);
            }

            EmitAddCycles(
                il,
                context,
                compareOnly
                    ? M68kJitCore.GetCompareAddressCyclesForTiming(instruction.Source, instruction.Size)
                    : M68kJitCore.GetAddressArithmeticCyclesForTiming(instruction.Source, instruction.Size));
        }

        private static void EmitCmpm(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var source = il.DeclareLocal(typeof(uint));
            var destination = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitLoadEaValue(il, context, instruction.Source, instruction.Size, applySideEffects: true);
            il.Emit(OpCodes.Stloc, source);
            EmitLoadEaValue(il, context, instruction.Destination, instruction.Size, applySideEffects: true);
            il.Emit(OpCodes.Stloc, destination);
            EmitArithmeticFlagsAndResult(il, context, destination, source, result, instruction.Size, add: false, setExtend: false);
            EmitAddCycles(il, context, instruction.Size == M68kOperandSize.Long ? 20 : 12);
        }

        private static void EmitBinaryArithmetic(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context, bool add)
        {
            var regValue = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, regValue);
            if (instruction.Variant != 0)
            {
                EmitArithmeticReadModifyWrite(
                    il,
                    context,
                    instruction.Destination,
                    instruction.Size,
                    emitSource: () => il.Emit(OpCodes.Ldloc, regValue),
                    add,
                    setExtend: true,
                    M68kJitCore.GetAluDataToEaCyclesForTiming(instruction.Destination, instruction.Size));
                return;
            }

            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitLoadEaValue(il, context, instruction.Source, instruction.Size, applySideEffects: true);
            il.Emit(OpCodes.Stloc, source);
            EmitArithmeticFlagsAndResult(il, context, regValue, source, result, instruction.Size, add, setExtend: true);
            EmitStoreDataRegister(il, context, instruction.Register, result, instruction.Size);
            EmitAddCycles(il, context, M68kJitCore.GetAluEaToDataCyclesForTiming(instruction.Source, instruction.Size));
        }

        private static void EmitImmediateArithmetic(
            ILGenerator il,
            M68kDecodedInstruction instruction,
            TraceEmitContext context,
            bool add,
            int logicalOperation)
        {
            if (logicalOperation != 0)
            {
                EmitLogicalReadModifyWrite(
                    il,
                    context,
                    instruction.Destination,
                    instruction.Size,
                    emitSource: () => EmitLoadUIntConstant(il, instruction.Source.Immediate),
                    logicalOperation - 1,
                    M68kJitCore.GetImmediateAluCyclesForTiming(instruction.Destination, instruction.Size));
                return;
            }

            EmitArithmeticReadModifyWrite(
                il,
                context,
                instruction.Destination,
                instruction.Size,
                emitSource: () => EmitLoadUIntConstant(il, instruction.Source.Immediate),
                add,
                setExtend: true,
                M68kJitCore.GetImmediateAluCyclesForTiming(instruction.Destination, instruction.Size));
        }

        private static void EmitBinaryLogical(
            ILGenerator il,
            M68kDecodedInstruction instruction,
            TraceEmitContext context,
            int operation)
        {
            var regValue = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, regValue);
            if (instruction.Variant != 0 || instruction.Operation == M68kJitOperation.Eor)
            {
                EmitLogicalReadModifyWrite(
                    il,
                    context,
                    instruction.Destination,
                    instruction.Size,
                    emitSource: () => il.Emit(OpCodes.Ldloc, regValue),
                    operation,
                    M68kJitCore.GetAluDataToEaCyclesForTiming(instruction.Destination, instruction.Size));
                return;
            }

            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitLoadEaValue(il, context, instruction.Source, instruction.Size, applySideEffects: true);
            il.Emit(OpCodes.Stloc, source);
            il.Emit(OpCodes.Ldloc, regValue);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(operation == 0 ? OpCodes.And : OpCodes.Or);
            EmitLoadUIntConstant(il, GetMask(instruction.Size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, result);
            EmitStoreDataRegister(il, context, instruction.Register, result, instruction.Size);
            EmitSetLogicFlags(il, context, result, instruction.Size);
            EmitAddCycles(il, context, M68kJitCore.GetAluEaToDataCyclesForTiming(instruction.Source, instruction.Size));
        }

        private static void EmitNot(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var value = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitReadForModify(il, context, instruction.Destination, instruction.Size, out var address, out var memory);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Not);
            EmitLoadUIntConstant(il, GetMask(instruction.Size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, result);
            EmitWriteResolvedEa(il, context, instruction.Destination, instruction.Size, result, address, memory);
            EmitSetLogicFlags(il, context, result, instruction.Size);
            EmitAddCycles(il, context, M68kJitCore.GetUnaryCyclesForTiming(M68kJitOperation.Not, instruction.Destination, instruction.Size));
        }

        private static void EmitNeg(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitReadForModify(il, context, instruction.Destination, instruction.Size, out var address, out var memory);
            il.Emit(OpCodes.Stloc, source);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, destination);
            EmitArithmeticFlagsAndResult(il, context, destination, source, result, instruction.Size, add: false, setExtend: true);
            EmitWriteResolvedEa(il, context, instruction.Destination, instruction.Size, result, address, memory);
            EmitAddCycles(il, context, M68kJitCore.GetUnaryCyclesForTiming(M68kJitOperation.Neg, instruction.Destination, instruction.Size));
        }

        private static void EmitNegx(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var source = il.DeclareLocal(typeof(uint));
            var packed = il.DeclareLocal(typeof(ulong));
            var result = il.DeclareLocal(typeof(uint));
            var status = il.DeclareLocal(typeof(int));
            EmitReadForModify(il, context, instruction.Destination, instruction.Size, out var address, out var memory);
            il.Emit(OpCodes.Stloc, source);
            il.Emit(OpCodes.Ldloc, source);
            EmitLoadStatus(il, context);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Size);
            il.Emit(OpCodes.Call, Negx);
            il.Emit(OpCodes.Stloc, packed);
            il.Emit(OpCodes.Ldloc, packed);
            il.Emit(OpCodes.Conv_U4);
            il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Ldloc, packed);
            il.Emit(OpCodes.Ldc_I4, 32);
            il.Emit(OpCodes.Shr_Un);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, status);
            EmitStoreStatus(il, context, status);
            EmitWriteResolvedEa(il, context, instruction.Destination, instruction.Size, result, address, memory);
            EmitAddCycles(il, context, M68kJitCore.GetUnaryCyclesForTiming(M68kJitOperation.Negx, instruction.Destination, instruction.Size));
        }

        private static void EmitExt(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context, M68kOperandSize size)
        {
            var value = il.DeclareLocal(typeof(uint));
            if (size == M68kOperandSize.Word)
            {
                EmitLoadDataRegister(il, context, instruction.Register, M68kOperandSize.Byte);
                il.Emit(OpCodes.Conv_I1);
                il.Emit(OpCodes.Conv_U4);
                EmitLoadUIntConstant(il, 0xFFFF);
                il.Emit(OpCodes.And);
            }
            else
            {
                EmitLoadDataRegister(il, context, instruction.Register, M68kOperandSize.Word);
                il.Emit(OpCodes.Conv_I2);
                il.Emit(OpCodes.Conv_U4);
            }

            il.Emit(OpCodes.Stloc, value);
            EmitStoreDataRegister(il, context, instruction.Register, value, size);
            EmitSetLogicFlags(il, context, value, size);
            EmitAddCycles(il, context, 4);
        }

        private static void EmitSwap(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var value = il.DeclareLocal(typeof(uint));
            EmitLoadDataRegister(il, context, instruction.Register, M68kOperandSize.Long);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ldc_I4, 16);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ldc_I4, 16);
            il.Emit(OpCodes.Shr_Un);
            EmitLoadUIntConstant(il, 0xFFFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Stloc, value);
            EmitStoreDataRegister(il, context, instruction.Register, value, M68kOperandSize.Long);
            EmitSetLogicFlags(il, context, value, M68kOperandSize.Long);
            EmitAddCycles(il, context, 4);
        }

        private static void EmitExchange(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            var left = il.DeclareLocal(typeof(uint));
            var right = il.DeclareLocal(typeof(uint));
            if (instruction.Variant == 0)
            {
                EmitLoadDataRegister(il, context, instruction.Register, M68kOperandSize.Long);
                il.Emit(OpCodes.Stloc, left);
                EmitLoadDataRegister(il, context, instruction.QuickValue, M68kOperandSize.Long);
                il.Emit(OpCodes.Stloc, right);
                EmitStoreDataRegister(il, context, instruction.Register, right, M68kOperandSize.Long);
                EmitStoreDataRegister(il, context, instruction.QuickValue, left, M68kOperandSize.Long);
            }
            else if (instruction.Variant == 1)
            {
                EmitLoadAddressRegister(il, context, instruction.Register);
                il.Emit(OpCodes.Stloc, left);
                EmitLoadAddressRegister(il, context, instruction.QuickValue);
                il.Emit(OpCodes.Stloc, right);
                EmitStoreAddressRegister(il, instruction.Register, right);
                EmitStoreAddressRegister(il, instruction.QuickValue, left);
            }
            else
            {
                EmitLoadDataRegister(il, context, instruction.Register, M68kOperandSize.Long);
                il.Emit(OpCodes.Stloc, left);
                EmitLoadAddressRegister(il, context, instruction.QuickValue);
                il.Emit(OpCodes.Stloc, right);
                EmitStoreDataRegister(il, context, instruction.Register, right, M68kOperandSize.Long);
                EmitStoreAddressRegister(il, instruction.QuickValue, left);
            }

            EmitAddCycles(il, context, 6);
        }

        private static void EmitRegisterShift(ILGenerator il, M68kDecodedInstruction instruction)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, instruction.Register);
            il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
            il.Emit(OpCodes.Ldc_I4, instruction.Variant);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Size);
            il.Emit(OpCodes.Call, ExecuteRegisterShift);
        }

        private static void EmitMultiplyDivide(
            ILGenerator il,
            M68kDecodedInstruction instruction,
            TraceEmitContext context,
            bool signed,
            bool divide)
        {
            var source = il.DeclareLocal(typeof(uint));
            EmitLoadEaValue(il, context, instruction.Source, M68kOperandSize.Word, applySideEffects: true);
            il.Emit(OpCodes.Stloc, source);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Ldc_I4, instruction.Register);
            il.Emit(signed ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(divide ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4, M68kJitCore.GetEaOperandCyclesForTiming(instruction.Source, M68kOperandSize.Word));
            il.Emit(OpCodes.Call, ExecuteMultiplyDivideValue);
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

        private static void EmitBsr(ILGenerator il, M68kDecodedInstruction instruction, TraceEmitContext context)
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitLoadProgramCounter(il, context);
            il.Emit(OpCodes.Call, PushLong);
            EmitSetProgramCounter(il, context, GetBranchTarget(instruction));
            EmitAddCycles(il, context, 18);
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

        private static void EmitJump(
            ILGenerator il,
            M68kDecodedInstruction instruction,
            TraceEmitContext context,
            bool link)
        {
            var target = il.DeclareLocal(typeof(uint));
            EmitResolveEaAddress(il, context, instruction.Source, M68kOperandSize.Long, applySideEffects: false);
            il.Emit(OpCodes.Stloc, target);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, target);
            il.Emit(link ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4, link
                ? M68kJitCore.GetJsrCyclesForTiming(instruction.Source)
                : M68kJitCore.GetJmpCyclesForTiming(instruction.Source));
            il.Emit(OpCodes.Call, ExecuteJumpTo);
        }

        private static void EmitRts(ILGenerator il, TraceEmitContext context)
        {
            var target = il.DeclareLocal(typeof(uint));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, PullLong);
            il.Emit(OpCodes.Stloc, target);
            EmitSetProgramCounter(il, context, target);
            EmitAddCycles(il, context, 16);
        }

        private static void EmitArithmeticReadModifyWrite(
            ILGenerator il,
            TraceEmitContext context,
            M68kDecodedEa destinationEa,
            M68kOperandSize size,
            Action emitSource,
            bool add,
            bool setExtend,
            int cycles)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitReadForModify(il, context, destinationEa, size, out var address, out var memory);
            il.Emit(OpCodes.Stloc, destination);
            emitSource();
            il.Emit(OpCodes.Stloc, source);
            EmitArithmeticFlagsAndResult(il, context, destination, source, result, size, add, setExtend);
            EmitWriteResolvedEa(il, context, destinationEa, size, result, address, memory);
            EmitAddCycles(il, context, cycles);
        }

        private static void EmitLogicalReadModifyWrite(
            ILGenerator il,
            TraceEmitContext context,
            M68kDecodedEa destinationEa,
            M68kOperandSize size,
            Action emitSource,
            int operation,
            int cycles)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitReadForModify(il, context, destinationEa, size, out var address, out var memory);
            il.Emit(OpCodes.Stloc, destination);
            emitSource();
            il.Emit(OpCodes.Stloc, source);
            il.Emit(OpCodes.Ldloc, destination);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(operation == 0 ? OpCodes.And : operation == 1 ? OpCodes.Or : OpCodes.Xor);
            EmitLoadUIntConstant(il, GetMask(size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, result);
            EmitWriteResolvedEa(il, context, destinationEa, size, result, address, memory);
            EmitSetLogicFlags(il, context, result, size);
            EmitAddCycles(il, context, cycles);
        }

        private static void EmitReadForModify(
            ILGenerator il,
            TraceEmitContext context,
            M68kDecodedEa ea,
            M68kOperandSize size,
            out LocalBuilder? address,
            out bool memory)
        {
            if (ea.Kind == M68kJitEaKind.DataRegister || ea.Kind == M68kJitEaKind.AddressRegister)
            {
                address = null;
                memory = false;
                EmitLoadEaValue(il, context, ea, size, applySideEffects: true);
                return;
            }

            address = il.DeclareLocal(typeof(uint));
            memory = true;
            EmitResolveEaAddress(il, context, ea, size, applySideEffects: true);
            il.Emit(OpCodes.Stloc, address);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, address);
            il.Emit(OpCodes.Call, GetReadMemoryMethod(size));
        }

        private static void EmitWriteResolvedEa(
            ILGenerator il,
            TraceEmitContext context,
            M68kDecodedEa ea,
            M68kOperandSize size,
            LocalBuilder value,
            LocalBuilder? address,
            bool memory)
        {
            if (!memory)
            {
                EmitStoreEaValue(il, context, ea, size, value);
                return;
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, address!);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Call, GetWriteMemoryMethod(size));
        }

        private static void EmitLoadEaValue(
            ILGenerator il,
            TraceEmitContext context,
            M68kDecodedEa ea,
            M68kOperandSize size,
            bool applySideEffects)
        {
            switch (ea.Kind)
            {
                case M68kJitEaKind.DataRegister:
                    EmitLoadDataRegister(il, context, ea.Register, size);
                    return;
                case M68kJitEaKind.AddressRegister:
                    EmitLoadAddressRegister(il, context, ea.Register);
                    if (size == M68kOperandSize.Word)
                    {
                        EmitLoadUIntConstant(il, 0xFFFF);
                        il.Emit(OpCodes.And);
                    }

                    return;
                case M68kJitEaKind.Immediate:
                    EmitLoadUIntConstant(il, ea.Immediate & GetMask(size));
                    return;
                default:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitResolveEaAddress(il, context, ea, size, applySideEffects);
                    il.Emit(OpCodes.Call, GetReadMemoryMethod(size));
                    return;
            }
        }

        private static void EmitStoreEaValue(
            ILGenerator il,
            TraceEmitContext context,
            M68kDecodedEa ea,
            M68kOperandSize size,
            LocalBuilder value)
        {
            switch (ea.Kind)
            {
                case M68kJitEaKind.DataRegister:
                    EmitStoreDataRegister(il, context, ea.Register, value, size);
                    return;
                case M68kJitEaKind.AddressRegister:
                    if (size == M68kOperandSize.Word)
                    {
                        var extended = il.DeclareLocal(typeof(uint));
                        il.Emit(OpCodes.Ldloc, value);
                        il.Emit(OpCodes.Conv_I2);
                        il.Emit(OpCodes.Conv_U4);
                        il.Emit(OpCodes.Stloc, extended);
                        EmitStoreAddressRegister(il, ea.Register, extended);
                    }
                    else
                    {
                        EmitStoreAddressRegister(il, ea.Register, value);
                    }

                    return;
                default:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitResolveEaAddress(il, context, ea, size, applySideEffects: true);
                    il.Emit(OpCodes.Ldloc, value);
                    il.Emit(OpCodes.Call, GetWriteMemoryMethod(size));
                    return;
            }
        }

        private static MethodInfo GetReadMemoryMethod(M68kOperandSize size)
            => size switch
            {
                M68kOperandSize.Byte => ReadMemoryByte,
                M68kOperandSize.Word => ReadMemoryWord,
                _ => ReadMemoryLong
            };

        private static MethodInfo GetWriteMemoryMethod(M68kOperandSize size)
            => size switch
            {
                M68kOperandSize.Byte => WriteMemoryByte,
                M68kOperandSize.Word => WriteMemoryWord,
                _ => WriteMemoryLong
            };

        private static void EmitResolveEaAddress(
            ILGenerator il,
            TraceEmitContext context,
            M68kDecodedEa ea,
            M68kOperandSize size,
            bool applySideEffects)
        {
            switch (ea.Kind)
            {
                case M68kJitEaKind.AddressIndirect:
                    EmitLoadAddressRegister(il, context, ea.Register);
                    return;
                case M68kJitEaKind.AddressPostincrement:
                {
                    var address = il.DeclareLocal(typeof(uint));
                    EmitLoadAddressRegister(il, context, ea.Register);
                    il.Emit(OpCodes.Stloc, address);
                    if (applySideEffects)
                    {
                        var updated = il.DeclareLocal(typeof(uint));
                        il.Emit(OpCodes.Ldloc, address);
                        EmitLoadUIntConstant(il, AddressIncrement(ea.Register, size));
                        il.Emit(OpCodes.Add);
                        il.Emit(OpCodes.Stloc, updated);
                        EmitStoreAddressRegister(il, ea.Register, updated);
                    }

                    il.Emit(OpCodes.Ldloc, address);
                    return;
                }
                case M68kJitEaKind.AddressPredecrement:
                {
                    var address = il.DeclareLocal(typeof(uint));
                    EmitLoadAddressRegister(il, context, ea.Register);
                    if (applySideEffects)
                    {
                        EmitLoadUIntConstant(il, AddressIncrement(ea.Register, size));
                        il.Emit(OpCodes.Sub);
                        il.Emit(OpCodes.Stloc, address);
                        EmitStoreAddressRegister(il, ea.Register, address);
                    }
                    else
                    {
                        il.Emit(OpCodes.Stloc, address);
                    }

                    il.Emit(OpCodes.Ldloc, address);
                    return;
                }
                case M68kJitEaKind.AddressDisplacement:
                    EmitLoadAddressRegister(il, context, ea.Register);
                    il.Emit(OpCodes.Ldc_I4, (int)(short)ea.Extension0);
                    il.Emit(OpCodes.Add);
                    return;
                case M68kJitEaKind.AddressIndex:
                    EmitLoadAddressRegister(il, context, ea.Register);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4, ea.Extension0);
                    il.Emit(OpCodes.Call, ResolveIndex);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_I4, (sbyte)(ea.Extension0 & 0xFF));
                    il.Emit(OpCodes.Add);
                    return;
                case M68kJitEaKind.AbsoluteWord:
                    EmitLoadUIntConstant(il, unchecked((uint)(short)ea.Extension0));
                    return;
                case M68kJitEaKind.AbsoluteLong:
                    EmitLoadUIntConstant(il, ((uint)ea.Extension0 << 16) | ea.Extension1);
                    return;
                case M68kJitEaKind.PcDisplacement:
                    EmitLoadUIntConstant(il, unchecked((uint)(ea.ExtensionAddress + (short)ea.Extension0)));
                    return;
                case M68kJitEaKind.PcIndex:
                    EmitLoadUIntConstant(il, ea.ExtensionAddress);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4, ea.Extension0);
                    il.Emit(OpCodes.Call, ResolveIndex);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_I4, (sbyte)(ea.Extension0 & 0xFF));
                    il.Emit(OpCodes.Add);
                    return;
                default:
                    EmitLoadUIntConstant(il, 0);
                    return;
            }
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

        private static void EmitLoadAddressRegister(ILGenerator il, TraceEmitContext context, int register)
        {
            il.Emit(OpCodes.Ldloc, context.AddressRegisters);
            il.Emit(OpCodes.Ldc_I4, register);
            il.Emit(OpCodes.Ldelem_U4);
        }

        private static void EmitLoadProgramCounter(ILGenerator il, TraceEmitContext context)
        {
            il.Emit(OpCodes.Ldloc, context.State);
            il.Emit(OpCodes.Call, ProgramCounterProperty.GetMethod!);
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

        private static void EmitStoreAddressRegister(ILGenerator il, int register, LocalBuilder value)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, register);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Call, SetAddressRegister);
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

        private static void EmitSetProgramCounter(ILGenerator il, TraceEmitContext context, LocalBuilder value)
        {
            il.Emit(OpCodes.Ldloc, context.State);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Call, ProgramCounterProperty.SetMethod!);
        }

        private static void EmitAddCycles(ILGenerator il, TraceEmitContext context, int cycles)
        {
            _ = context;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, cycles);
            il.Emit(OpCodes.Call, CompleteCompiledInstructionCycles);
        }

        private static void EmitLoadUIntConstant(ILGenerator il, uint value)
        {
            il.Emit(OpCodes.Ldc_I4, unchecked((int)value));
        }

        private static void EmitTrue(ILGenerator il)
        {
            il.Emit(OpCodes.Ldc_I4_1);
        }

        private static uint GetMask(M68kOperandSize size)
            => M68kCpuState.Mask(size);

        private static uint GetSignBit(M68kOperandSize size)
            => M68kCpuState.SignBit(size);

        private static uint GetBranchTarget(M68kDecodedInstruction instruction)
            => Normalize(instruction.BranchBase + unchecked((uint)instruction.Displacement));

        private static uint AddressIncrement(int register, M68kOperandSize size)
            => M68kIntegerSemantics.AddressIncrement(register, size);

        private static uint Normalize(uint address)
            => address & 0x00FF_FFFF;

        private static PropertyInfo RequiredProperty(Type type, string name)
            => type.GetProperty(name) ?? throw new MissingMemberException(type.FullName, name);

        private static MethodInfo RequiredMethod(Type type, string name, params Type[] parameterTypes)
            => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, parameterTypes) ??
                throw new MissingMethodException(type.FullName, name);

        private static MethodInfo RequiredStaticMethod(Type type, string name, params Type[] parameterTypes)
            => type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, parameterTypes) ??
                throw new MissingMethodException(type.FullName, name);

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
