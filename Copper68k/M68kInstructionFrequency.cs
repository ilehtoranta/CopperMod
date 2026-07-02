/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace Copper68k
{
    internal enum M68kInstructionFamily
    {
        Move,
        MovePeripheral,
        Immediate,
        BitManipulation,
        Unary,
        StatusControl,
        SystemControl,
        ControlFlow,
        QuickArithmetic,
        ConditionSet,
        Arithmetic,
        AddressArithmetic,
        AddSubExtend,
        Compare,
        CompareMemory,
        MultiplyDivide,
        Logical,
        Exchange,
        ShiftRotate,
        Bcd,
        Movem,
        TrapException,
        Unknown
    }

    internal enum M68kJitTarget
    {
        None,
        MultiplyDivide,
        NegNot
    }

    internal readonly record struct M68kInstructionFamilyFrequency(
        M68kInstructionFamily Family,
        string FamilyName,
        long Count);

    internal readonly record struct M68kJitTargetFrequency(
        M68kJitTarget Target,
        string TargetName,
        long Count);

    internal readonly record struct M68kOpcodeFrequency(
        ushort Opcode,
        string Mnemonic,
        M68kInstructionFamily Family,
        string FamilyName,
        M68kJitTarget JitTarget,
        string JitTargetName,
        long Count);

    internal readonly record struct M68kPcFrequency(
        uint ProgramCounter,
        ushort Opcode,
        string Mnemonic,
        M68kInstructionFamily Family,
        string FamilyName,
        M68kJitTarget JitTarget,
        string JitTargetName,
        long Count);

    internal readonly record struct M68kHotLoopFrequency(
        uint StartProgramCounter,
        uint EndProgramCounter,
        uint BranchProgramCounter,
        uint TargetProgramCounter,
        ushort BranchOpcode,
        string BranchMnemonic,
        int ByteLength,
        long Count);

    internal readonly record struct M68kInstructionFrequencySnapshot(
        long TotalInstructions,
        IReadOnlyList<M68kInstructionFamilyFrequency> Families,
        IReadOnlyList<M68kJitTargetFrequency> JitTargets,
        IReadOnlyList<M68kOpcodeFrequency> Opcodes,
        IReadOnlyList<M68kPcFrequency> HotPcs,
        IReadOnlyList<M68kHotLoopFrequency> HotLoops)
    {
        public static M68kInstructionFrequencySnapshot Empty { get; } = new(
            0,
            Array.Empty<M68kInstructionFamilyFrequency>(),
            Array.Empty<M68kJitTargetFrequency>(),
            Array.Empty<M68kOpcodeFrequency>(),
            Array.Empty<M68kPcFrequency>(),
            Array.Empty<M68kHotLoopFrequency>());
    }

    internal interface IM68kInstructionFrequencyProvider
    {
        bool InstructionFrequencyEnabled { get; set; }

        M68kInstructionFrequencySnapshot CaptureInstructionFrequency();

        void ResetInstructionFrequency();
    }

    internal sealed class M68kInstructionFrequencyMatrix
    {
        private const int MaxHotLoopByteLength = 4096;
        private readonly long[] _familyCounts = new long[Enum.GetValues<M68kInstructionFamily>().Length];
        private readonly long[] _jitTargetCounts = new long[Enum.GetValues<M68kJitTarget>().Length];
        private readonly Dictionary<ushort, long> _opcodeCounts = new Dictionary<ushort, long>();
        private readonly Dictionary<ulong, long> _pcCounts = new Dictionary<ulong, long>();
        private readonly Dictionary<HotLoopKey, long> _hotLoopCounts = new Dictionary<HotLoopKey, long>();
        private long _totalInstructions;

        public bool Enabled { get; set; }

        public void Record(ushort opcode)
            => Record(0, opcode);

        public void Record(uint programCounter, ushort opcode)
        {
            if (!Enabled)
            {
                return;
            }

            var family = M68kInstructionClassifier.GetFamily(opcode);
            var jitTarget = M68kInstructionClassifier.GetJitTarget(opcode);
            _familyCounts[(int)family]++;
            _jitTargetCounts[(int)jitTarget]++;
            _opcodeCounts.TryGetValue(opcode, out var count);
            _opcodeCounts[opcode] = count + 1;
            var pcKey = CreatePcKey(programCounter, opcode);
            _pcCounts.TryGetValue(pcKey, out var pcCount);
            _pcCounts[pcKey] = pcCount + 1;
            _totalInstructions++;
        }

        public void RecordTakenBranch(
            uint branchProgramCounter,
            ushort branchOpcode,
            uint targetProgramCounter,
            int instructionByteLength)
        {
            if (!Enabled ||
                instructionByteLength <= 0 ||
                targetProgramCounter > branchProgramCounter)
            {
                return;
            }

            var endProgramCounter = unchecked(branchProgramCounter + (uint)instructionByteLength);
            if (endProgramCounter < branchProgramCounter ||
                targetProgramCounter >= endProgramCounter)
            {
                return;
            }

            var byteLength = endProgramCounter - targetProgramCounter;
            if (byteLength == 0 ||
                byteLength > MaxHotLoopByteLength)
            {
                return;
            }

            var key = new HotLoopKey(
                targetProgramCounter,
                endProgramCounter,
                branchProgramCounter,
                targetProgramCounter,
                branchOpcode,
                (int)byteLength);
            _hotLoopCounts.TryGetValue(key, out var count);
            _hotLoopCounts[key] = count + 1;
        }

        public M68kInstructionFrequencySnapshot CaptureSnapshot()
        {
            if (_totalInstructions == 0)
            {
                return M68kInstructionFrequencySnapshot.Empty;
            }

            var families = Enum.GetValues<M68kInstructionFamily>()
                .Select(family => new M68kInstructionFamilyFrequency(
                    family,
                    M68kInstructionClassifier.GetFamilyName(family),
                    _familyCounts[(int)family]))
                .Where(entry => entry.Count != 0)
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.FamilyName, StringComparer.Ordinal)
                .ToArray();

            var jitTargets = Enum.GetValues<M68kJitTarget>()
                .Where(target => target != M68kJitTarget.None)
                .Select(target => new M68kJitTargetFrequency(
                    target,
                    M68kInstructionClassifier.GetJitTargetName(target),
                    _jitTargetCounts[(int)target]))
                .Where(entry => entry.Count != 0)
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.TargetName, StringComparer.Ordinal)
                .ToArray();

            var opcodes = _opcodeCounts
                .Select(entry =>
                {
                    var family = M68kInstructionClassifier.GetFamily(entry.Key);
                    var jitTarget = M68kInstructionClassifier.GetJitTarget(entry.Key);
                    return new M68kOpcodeFrequency(
                        entry.Key,
                        M68kInstructionClassifier.GetMnemonic(entry.Key),
                        family,
                        M68kInstructionClassifier.GetFamilyName(family),
                        jitTarget,
                        M68kInstructionClassifier.GetJitTargetName(jitTarget),
                        entry.Value);
                })
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Opcode)
                .ToArray();

            var hotPcs = _pcCounts
                .Select(entry =>
                {
                    var programCounter = GetPcFromKey(entry.Key);
                    var opcode = GetOpcodeFromKey(entry.Key);
                    var family = M68kInstructionClassifier.GetFamily(opcode);
                    var jitTarget = M68kInstructionClassifier.GetJitTarget(opcode);
                    return new M68kPcFrequency(
                        programCounter,
                        opcode,
                        M68kInstructionClassifier.GetMnemonic(opcode),
                        family,
                        M68kInstructionClassifier.GetFamilyName(family),
                        jitTarget,
                        M68kInstructionClassifier.GetJitTargetName(jitTarget),
                        entry.Value);
                })
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.ProgramCounter)
                .ThenBy(entry => entry.Opcode)
                .ToArray();

            var hotLoops = _hotLoopCounts
                .Select(entry => new M68kHotLoopFrequency(
                    entry.Key.StartProgramCounter,
                    entry.Key.EndProgramCounter,
                    entry.Key.BranchProgramCounter,
                    entry.Key.TargetProgramCounter,
                    entry.Key.BranchOpcode,
                    M68kInstructionClassifier.GetMnemonic(entry.Key.BranchOpcode),
                    entry.Key.ByteLength,
                    entry.Value))
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.StartProgramCounter)
                .ThenBy(entry => entry.BranchProgramCounter)
                .ThenBy(entry => entry.BranchOpcode)
                .ToArray();

            return new M68kInstructionFrequencySnapshot(_totalInstructions, families, jitTargets, opcodes, hotPcs, hotLoops);
        }

        public void Reset()
        {
            Array.Clear(_familyCounts);
            Array.Clear(_jitTargetCounts);
            _opcodeCounts.Clear();
            _pcCounts.Clear();
            _hotLoopCounts.Clear();
            _totalInstructions = 0;
        }

        private static ulong CreatePcKey(uint programCounter, ushort opcode)
            => ((ulong)programCounter << 16) | opcode;

        private static uint GetPcFromKey(ulong key)
            => (uint)(key >> 16);

        private static ushort GetOpcodeFromKey(ulong key)
            => (ushort)key;

        private readonly record struct HotLoopKey(
            uint StartProgramCounter,
            uint EndProgramCounter,
            uint BranchProgramCounter,
            uint TargetProgramCounter,
            ushort BranchOpcode,
            int ByteLength);
    }

    internal static class M68kInstructionClassifier
    {
        public static M68kInstructionFamily GetFamily(ushort opcode)
        {
            var line = opcode >> 12;
            return line switch
            {
                0x0 => GetLine0Family(opcode),
                0x1 or 0x2 or 0x3 => M68kInstructionFamily.Move,
                0x4 => GetLine4Family(opcode),
                0x5 => GetLine5Family(opcode),
                0x6 => M68kInstructionFamily.ControlFlow,
                0x7 => (opcode & 0xF100) == 0x7000 ? M68kInstructionFamily.Move : M68kInstructionFamily.Unknown,
                0x8 => GetLine8Family(opcode),
                0x9 => GetAddSubFamily(opcode, addressFamily: M68kInstructionFamily.AddressArithmetic),
                0xA => M68kInstructionFamily.TrapException,
                0xB => GetLineBFamily(opcode),
                0xC => GetLineCFamily(opcode),
                0xD => GetAddSubFamily(opcode, addressFamily: M68kInstructionFamily.AddressArithmetic),
                0xE => M68kInstructionFamily.ShiftRotate,
                0xF => M68kInstructionFamily.TrapException,
                _ => M68kInstructionFamily.Unknown
            };
        }

        public static M68kJitTarget GetJitTarget(ushort opcode)
        {
            var family = GetFamily(opcode);
            if (family == M68kInstructionFamily.MultiplyDivide)
            {
                return M68kJitTarget.MultiplyDivide;
            }

            return family == M68kInstructionFamily.Unary &&
                ((opcode & 0xFF00) is 0x4400 or 0x4600)
                ? M68kJitTarget.NegNot
                : M68kJitTarget.None;
        }

        public static string GetMnemonic(ushort opcode)
        {
            var line = opcode >> 12;
            return line switch
            {
                0x0 => GetLine0Mnemonic(opcode),
                0x1 or 0x2 or 0x3 => ((opcode >> 6) & 7) == 1 ? "MOVEA" : "MOVE",
                0x4 => GetLine4Mnemonic(opcode),
                0x5 => GetLine5Mnemonic(opcode),
                0x6 => GetBranchMnemonic(opcode),
                0x7 => (opcode & 0xF100) == 0x7000 ? "MOVEQ" : "UNKNOWN",
                0x8 => GetLine8Mnemonic(opcode),
                0x9 => GetAddSubMnemonic(opcode, "SUB", "SUBA", "SUBX"),
                0xA => "Line-A",
                0xB => GetLineBMnemonic(opcode),
                0xC => GetLineCMnemonic(opcode),
                0xD => GetAddSubMnemonic(opcode, "ADD", "ADDA", "ADDX"),
                0xE => GetShiftRotateMnemonic(opcode),
                0xF => "Line-F",
                _ => "UNKNOWN"
            };
        }

        public static string GetJitTargetName(M68kJitTarget target)
        {
            return target switch
            {
                M68kJitTarget.MultiplyDivide => "MultiplyDivide",
                M68kJitTarget.NegNot => "NOT/NEG",
                _ => string.Empty
            };
        }

        public static string GetFamilyName(M68kInstructionFamily family)
        {
            return family switch
            {
                M68kInstructionFamily.Move => "Move",
                M68kInstructionFamily.MovePeripheral => "MovePeripheral",
                M68kInstructionFamily.Immediate => "Immediate",
                M68kInstructionFamily.BitManipulation => "BitManipulation",
                M68kInstructionFamily.Unary => "Unary",
                M68kInstructionFamily.StatusControl => "StatusControl",
                M68kInstructionFamily.SystemControl => "SystemControl",
                M68kInstructionFamily.ControlFlow => "ControlFlow",
                M68kInstructionFamily.QuickArithmetic => "QuickArithmetic",
                M68kInstructionFamily.ConditionSet => "ConditionSet",
                M68kInstructionFamily.Arithmetic => "Arithmetic",
                M68kInstructionFamily.AddressArithmetic => "AddressArithmetic",
                M68kInstructionFamily.AddSubExtend => "AddSubExtend",
                M68kInstructionFamily.Compare => "Compare",
                M68kInstructionFamily.CompareMemory => "CompareMemory",
                M68kInstructionFamily.MultiplyDivide => "MultiplyDivide",
                M68kInstructionFamily.Logical => "Logical",
                M68kInstructionFamily.Exchange => "Exchange",
                M68kInstructionFamily.ShiftRotate => "ShiftRotate",
                M68kInstructionFamily.Bcd => "Bcd",
                M68kInstructionFamily.Movem => "Movem",
                M68kInstructionFamily.TrapException => "TrapException",
                _ => "Unknown"
            };
        }

        private static M68kInstructionFamily GetLine0Family(ushort opcode)
        {
            if ((opcode & 0xF138) is 0x0108 or 0x0148 or 0x0188 or 0x01C8)
            {
                return M68kInstructionFamily.MovePeripheral;
            }

            if ((opcode & 0xFF00) is 0x0800 or 0x0840 or 0x0880 or 0x08C0 ||
                (opcode & 0xF100) == 0x0100)
            {
                return M68kInstructionFamily.BitManipulation;
            }

            if (opcode is 0x003C or 0x007C or 0x023C or 0x027C or 0x0A3C or 0x0A7C)
            {
                return M68kInstructionFamily.StatusControl;
            }

            return (opcode & 0xFF00) is 0x0000 or 0x0200 or 0x0400 or 0x0600 or 0x0A00 or 0x0C00
                ? M68kInstructionFamily.Immediate
                : M68kInstructionFamily.Unknown;
        }

        private static M68kInstructionFamily GetLine4Family(ushort opcode)
        {
            if (opcode is 0x4E70 or 0x4E71 or 0x4E72 or 0x4E76 or 0x4E77 or 0x4E7A or 0x4E7B ||
                (opcode & 0xFFF0) == 0x4E60)
            {
                return M68kInstructionFamily.SystemControl;
            }

            if (opcode is 0x4E73 or 0x4E75 ||
                (opcode & 0xFFF0) == 0x4E40 ||
                (opcode & 0xFFC0) is 0x4E80 or 0x4EC0)
            {
                return M68kInstructionFamily.ControlFlow;
            }

            if (opcode == 0x4AFC)
            {
                return M68kInstructionFamily.TrapException;
            }

            if (opcode is 0x44FC or 0x46FC ||
                (opcode & 0xFFC0) is 0x40C0 or 0x44C0 or 0x46C0)
            {
                return M68kInstructionFamily.StatusControl;
            }

            if ((opcode & 0xFFC0) == 0x4800)
            {
                return M68kInstructionFamily.Bcd;
            }

            if ((opcode & 0xFB80) == 0x4880 && ((opcode >> 3) & 7) != 0)
            {
                return M68kInstructionFamily.Movem;
            }

            if ((opcode & 0xFFF8) == 0x4840 ||
                (opcode & 0xFFC0) is 0x4880 or 0x48C0 ||
                (opcode & 0xFF00) is 0x4200 or 0x4400 or 0x4600 or 0x4A00)
            {
                return M68kInstructionFamily.Unary;
            }

            if ((opcode & 0xFFF8) is 0x4E50 or 0x4E58 ||
                (opcode & 0xFFC0) == 0x4840)
            {
                return M68kInstructionFamily.SystemControl;
            }

            if ((opcode & 0xF1C0) == 0x41C0)
            {
                return M68kInstructionFamily.AddressArithmetic;
            }

            return M68kInstructionFamily.Unknown;
        }

        private static M68kInstructionFamily GetLine5Family(ushort opcode)
        {
            if ((opcode & 0xF0F8) == 0x50C8)
            {
                return M68kInstructionFamily.ControlFlow;
            }

            if ((opcode & 0xF0C0) == 0x50C0)
            {
                return M68kInstructionFamily.ConditionSet;
            }

            return (opcode & 0xF000) == 0x5000
                ? M68kInstructionFamily.QuickArithmetic
                : M68kInstructionFamily.Unknown;
        }

        private static M68kInstructionFamily GetLine8Family(ushort opcode)
        {
            var opmode = (opcode >> 6) & 7;
            if (opmode is 3 or 7)
            {
                return M68kInstructionFamily.MultiplyDivide;
            }

            return (opcode & 0xF1F0) == 0x8100
                ? M68kInstructionFamily.Bcd
                : M68kInstructionFamily.Logical;
        }

        private static M68kInstructionFamily GetLineBFamily(ushort opcode)
        {
            if ((opcode & 0xF138) == 0xB108)
            {
                return M68kInstructionFamily.CompareMemory;
            }

            var opmode = (opcode >> 6) & 7;
            if (opmode is 3 or 7)
            {
                return M68kInstructionFamily.AddressArithmetic;
            }

            return opmode >= 4 ? M68kInstructionFamily.Logical : M68kInstructionFamily.Compare;
        }

        private static M68kInstructionFamily GetLineCFamily(ushort opcode)
        {
            var opmode = (opcode >> 6) & 7;
            if ((opcode & 0xF1F8) is 0xC140 or 0xC148 or 0xC188)
            {
                return M68kInstructionFamily.Exchange;
            }

            if (opmode is 3 or 7)
            {
                return M68kInstructionFamily.MultiplyDivide;
            }

            return (opcode & 0xF1F0) == 0xC100
                ? M68kInstructionFamily.Bcd
                : M68kInstructionFamily.Logical;
        }

        private static M68kInstructionFamily GetAddSubFamily(ushort opcode, M68kInstructionFamily addressFamily)
        {
            var opmode = (opcode >> 6) & 7;
            var mode = (opcode >> 3) & 7;
            if (opmode is 4 or 5 or 6 && mode is 0 or 1)
            {
                return M68kInstructionFamily.AddSubExtend;
            }

            return opmode is 3 or 7
                ? addressFamily
                : M68kInstructionFamily.Arithmetic;
        }

        private static string GetLine0Mnemonic(ushort opcode)
        {
            if ((opcode & 0xF138) is 0x0108 or 0x0148 or 0x0188 or 0x01C8)
            {
                return "MOVEP";
            }

            if ((opcode & 0xFF00) is 0x0800 or 0x0840 or 0x0880 or 0x08C0)
            {
                return ((opcode >> 6) & 3) switch
                {
                    0 => "BTST",
                    1 => "BCHG",
                    2 => "BCLR",
                    _ => "BSET"
                };
            }

            if ((opcode & 0xF100) == 0x0100)
            {
                return ((opcode >> 6) & 3) switch
                {
                    0 => "BTST",
                    1 => "BCHG",
                    2 => "BCLR",
                    _ => "BSET"
                };
            }

            return (opcode & 0xFF00) switch
            {
                0x0000 => opcode == 0x003C ? "ORI to CCR" : opcode == 0x007C ? "ORI to SR" : "ORI",
                0x0200 => opcode == 0x023C ? "ANDI to CCR" : opcode == 0x027C ? "ANDI to SR" : "ANDI",
                0x0400 => "SUBI",
                0x0600 => "ADDI",
                0x0A00 => opcode == 0x0A3C ? "EORI to CCR" : opcode == 0x0A7C ? "EORI to SR" : "EORI",
                0x0C00 => "CMPI",
                _ => "UNKNOWN"
            };
        }

        private static string GetLine4Mnemonic(ushort opcode)
        {
            return opcode switch
            {
                0x44FC => "MOVE to CCR",
                0x46FC => "MOVE to SR",
                0x4E70 => "RESET",
                0x4E71 => "NOP",
                0x4E72 => "STOP",
                0x4E73 => "RTE",
                0x4E75 => "RTS",
                0x4E76 => "TRAPV",
                0x4E77 => "RTR",
                0x4AFC => "ILLEGAL",
                0x4E7A or 0x4E7B => "MOVEC",
                _ when (opcode & 0xFFF0) == 0x4E60 => "MOVE USP",
                _ when (opcode & 0xFFC0) == 0x40C0 => "MOVE from SR",
                _ when (opcode & 0xFFC0) == 0x44C0 => "MOVE to CCR",
                _ when (opcode & 0xFFC0) == 0x46C0 => "MOVE to SR",
                _ when (opcode & 0xFFF8) == 0x4E50 => "LINK",
                _ when (opcode & 0xFFF0) == 0x4E40 => "TRAP",
                _ when (opcode & 0xFFF8) == 0x4E58 => "UNLK",
                _ when (opcode & 0xFB80) == 0x4880 && ((opcode >> 3) & 7) != 0 => "MOVEM",
                _ when (opcode & 0xFFF8) == 0x4840 => "SWAP",
                _ when (opcode & 0xFFC0) == 0x4840 => "PEA",
                _ when (opcode & 0xFFC0) == 0x4880 => "EXT",
                _ when (opcode & 0xFFC0) == 0x48C0 => "EXT",
                _ when (opcode & 0xF1C0) == 0x41C0 => "LEA",
                _ when (opcode & 0xFFF8) == 0x4E90 => "JSR",
                _ when (opcode & 0xFFC0) == 0x4E80 => "JSR",
                _ when (opcode & 0xFFC0) == 0x4EC0 => "JMP",
                _ when (opcode & 0xFFC0) == 0x4800 => "NBCD",
                _ when (opcode & 0xFF00) == 0x4200 => "CLR",
                _ when (opcode & 0xFF00) == 0x4400 => "NEG",
                _ when (opcode & 0xFF00) == 0x4600 => "NOT",
                _ when (opcode & 0xFF00) == 0x4A00 => "TST",
                _ => "UNKNOWN"
            };
        }

        private static string GetLine5Mnemonic(ushort opcode)
        {
            if ((opcode & 0xF0F8) == 0x50C8)
            {
                return "DBcc";
            }

            if ((opcode & 0xF0C0) == 0x50C0)
            {
                return "Scc";
            }

            return (opcode & 0x0100) == 0 ? "ADDQ" : "SUBQ";
        }

        private static string GetBranchMnemonic(ushort opcode)
        {
            return ((opcode >> 8) & 0x0F) switch
            {
                0 => "BRA",
                1 => "BSR",
                _ => "Bcc"
            };
        }

        private static string GetLine8Mnemonic(ushort opcode)
        {
            var opmode = (opcode >> 6) & 7;
            if (opmode == 3)
            {
                return "DIVU";
            }

            if (opmode == 7)
            {
                return "DIVS";
            }

            return (opcode & 0xF1F0) == 0x8100 ? "SBCD" : "OR";
        }

        private static string GetLineBMnemonic(ushort opcode)
        {
            if ((opcode & 0xF138) == 0xB108)
            {
                return "CMPM";
            }

            var opmode = (opcode >> 6) & 7;
            if (opmode is 3 or 7)
            {
                return "CMPA";
            }

            return opmode >= 4 ? "EOR" : "CMP";
        }

        private static string GetLineCMnemonic(ushort opcode)
        {
            var opmode = (opcode >> 6) & 7;
            if ((opcode & 0xF1F8) is 0xC140 or 0xC148 or 0xC188)
            {
                return "EXG";
            }

            if (opmode == 3)
            {
                return "MULU";
            }

            if (opmode == 7)
            {
                return "MULS";
            }

            return (opcode & 0xF1F0) == 0xC100 ? "ABCD" : "AND";
        }

        private static string GetAddSubMnemonic(ushort opcode, string data, string address, string extend)
        {
            var opmode = (opcode >> 6) & 7;
            var mode = (opcode >> 3) & 7;
            if (opmode is 4 or 5 or 6 && mode is 0 or 1)
            {
                return extend;
            }

            return opmode is 3 or 7 ? address : data;
        }

        private static string GetShiftRotateMnemonic(ushort opcode)
        {
            var type = (opcode >> 9) & 3;
            var memoryMode = (opcode & 0x00C0) == 0x00C0;
            if (!memoryMode)
            {
                type = (opcode >> 3) & 3;
            }

            return type switch
            {
                0 => ((opcode & 0x0100) == 0 ? "ASR" : "ASL"),
                1 => ((opcode & 0x0100) == 0 ? "LSR" : "LSL"),
                2 => ((opcode & 0x0100) == 0 ? "ROXR" : "ROXL"),
                _ => ((opcode & 0x0100) == 0 ? "ROR" : "ROL")
            };
        }
    }
}
