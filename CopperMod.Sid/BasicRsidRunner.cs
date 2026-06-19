using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CopperMod.Sid
{
    internal sealed class BasicRsidRunner
    {
        private const byte TokenEnd = 0x80;
        private const byte TokenFor = 0x81;
        private const byte TokenNext = 0x82;
        private const byte TokenData = 0x83;
        private const byte TokenInput = 0x85;
        private const byte TokenDim = 0x86;
        private const byte TokenRead = 0x87;
        private const byte TokenLet = 0x88;
        private const byte TokenGoto = 0x89;
        private const byte TokenIf = 0x8B;
        private const byte TokenRestore = 0x8C;
        private const byte TokenGosub = 0x8D;
        private const byte TokenReturn = 0x8E;
        private const byte TokenRem = 0x8F;
        private const byte TokenStop = 0x90;
        private const byte TokenOn = 0x91;
        private const byte TokenWait = 0x92;
        private const byte TokenPoke = 0x97;
        private const byte TokenPrint = 0x99;
        private const byte TokenClr = 0x9C;
        private const byte TokenSys = 0x9E;
        private const byte TokenGet = 0xA1;
        private const byte TokenTo = 0xA4;
        private const byte TokenThen = 0xA7;
        private const byte TokenStep = 0xA9;
        private const byte TokenPlus = 0xAA;
        private const byte TokenMinus = 0xAB;
        private const byte TokenMultiply = 0xAC;
        private const byte TokenDivide = 0xAD;
        private const byte TokenAnd = 0xAF;
        private const byte TokenOr = 0xB0;
        private const byte TokenGreater = 0xB1;
        private const byte TokenEqual = 0xB2;
        private const byte TokenLess = 0xB3;
        private const byte TokenSgn = 0xB4;
        private const byte TokenInt = 0xB5;
        private const byte TokenAbs = 0xB6;
        private const byte TokenRnd = 0xBB;
        private const byte TokenPeek = 0xC2;
        private const byte TokenChr = 0xC7;
        private const byte TokenGo = 0xCB;
        private const long StatementCycles = 600;
        private const long ExpressionCycles = 80;
        private const long PokeCycles = 220;
        private const long SysMaxCycles = 250_000;
        private const int WaitPollLimit = 4096;

        private readonly C64Machine _machine;
        private readonly List<BasicLine> _lines = new List<BasicLine>();
        private readonly Dictionary<int, int> _lineLookup = new Dictionary<int, int>();
        private readonly List<BasicValue> _data = new List<BasicValue>();
        private readonly Dictionary<string, double> _numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, BasicArray> _arrays = new Dictionary<string, BasicArray>(StringComparer.Ordinal);
        private readonly Stack<ForFrame> _forStack = new Stack<ForFrame>();
        private readonly Stack<ReturnFrame> _returnStack = new Stack<ReturnFrame>();
        private readonly Random _random = new Random(1);
        private int _lineIndex;
        private int _position;
        private int _dataIndex;
        private bool _ended;
        private bool _halted;
        private int _statementCount;
        private long _cyclesConsumed;
        private byte _lastUnsupportedToken;
        private string? _lastDiagnostic;

        public BasicRsidRunner(C64Machine machine, ushort programStart)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            ParseProgram(programStart);
            Reset();
        }

        public BasicRsidDebugState DebugState => new BasicRsidDebugState(
            enabled: true,
            active: !_ended && !_halted,
            ended: _ended,
            halted: _halted,
            currentLineNumber: _lineIndex >= 0 && _lineIndex < _lines.Count ? _lines[_lineIndex].LineNumber : 0,
            statementCount: _statementCount,
            cyclesConsumed: _cyclesConsumed,
            lastUnsupportedToken: _lastUnsupportedToken,
            lastDiagnostic: _lastDiagnostic);

        public void RunUntil(long targetCycle)
        {
            var guard = 0;
            while (_machine.Cpu.Cycles < targetCycle)
            {
                if (_ended || _halted || _lineIndex < 0 || _lineIndex >= _lines.Count)
                {
                    _machine.AdvanceNativeCycles(targetCycle - _machine.Cpu.Cycles);
                    return;
                }

                var before = _machine.Cpu.Cycles;
                ExecuteNextStatement();
                if (_machine.Cpu.Cycles == before)
                {
                    Consume(StatementCycles);
                }

                guard++;
                if (guard > 100_000)
                {
                    Halt("BASIC runner guard limit reached.");
                    return;
                }
            }
        }

        private void Reset()
        {
            _numbers.Clear();
            _strings.Clear();
            _arrays.Clear();
            _forStack.Clear();
            _returnStack.Clear();
            _lineIndex = 0;
            _position = 0;
            _dataIndex = 0;
            _ended = _lines.Count == 0;
            _halted = false;
            _statementCount = 0;
            _cyclesConsumed = 0;
            _lastUnsupportedToken = 0;
            _lastDiagnostic = null;
        }

        private void ParseProgram(ushort start)
        {
            var address = start;
            var visited = new HashSet<ushort>();
            while (address != 0 && visited.Add(address))
            {
                var next = ReadWord(address);
                if (next == 0)
                {
                    break;
                }

                if (next <= address + 4)
                {
                    _lastDiagnostic = "BASIC line link points backwards or into the current line.";
                    break;
                }

                var lineNumber = ReadWord((ushort)(address + 2));
                var tokenStart = address + 4;
                var tokenEnd = tokenStart;
                while (tokenEnd < next && tokenEnd < 0x10000 && _machine.Ram[tokenEnd] != 0)
                {
                    tokenEnd++;
                }

                var tokens = new byte[Math.Max(0, tokenEnd - tokenStart)];
                Array.Copy(_machine.Ram, tokenStart, tokens, 0, tokens.Length);
                _lineLookup[lineNumber] = _lines.Count;
                _lines.Add(new BasicLine(lineNumber, tokens));
                address = next;
            }

            CollectDataItems();
        }

        private void CollectDataItems()
        {
            foreach (var line in _lines)
            {
                var cursor = new Cursor(line.Tokens);
                while (!cursor.End)
                {
                    cursor.SkipSpacesAndColons();
                    if (cursor.End)
                    {
                        break;
                    }

                    if (!cursor.Match(TokenData))
                    {
                        cursor.SkipStatement();
                        continue;
                    }

                    while (!cursor.End && !cursor.AtColon)
                    {
                        var text = cursor.ReadDataItemText();
                        if (text.Length > 0)
                        {
                            _data.Add(ParseDataValue(text));
                        }

                        cursor.SkipSpaces();
                        if (!cursor.Match((byte)','))
                        {
                            break;
                        }
                    }
                }
            }
        }

        private static BasicValue ParseDataValue(string text)
        {
            text = text.Trim();
            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            {
                return BasicValue.FromString(text.Substring(1, text.Length - 2));
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? BasicValue.FromNumber(value)
                : BasicValue.FromString(text);
        }

        private void ExecuteNextStatement()
        {
            var line = _lines[_lineIndex];
            var cursor = new Cursor(line.Tokens, _position);
            cursor.SkipSpacesAndColons();
            if (cursor.End)
            {
                MoveToNextLine();
                return;
            }

            _statementCount++;
            var token = cursor.Peek();
            switch (token)
            {
                case TokenEnd:
                case TokenStop:
                    cursor.Read();
                    _ended = true;
                    _position = cursor.Position;
                    Consume(StatementCycles);
                    return;
                case TokenData:
                    cursor.SkipStatement();
                    SavePosition(cursor);
                    Consume(StatementCycles);
                    return;
                case TokenRem:
                    cursor.SkipLine();
                    SavePosition(cursor);
                    Consume(StatementCycles);
                    return;
                case TokenDim:
                    cursor.Read();
                    ExecuteDim(ref cursor);
                    break;
                case TokenFor:
                    cursor.Read();
                    ExecuteFor(ref cursor);
                    break;
                case TokenNext:
                    cursor.Read();
                    ExecuteNext(ref cursor);
                    return;
                case TokenRead:
                    cursor.Read();
                    ExecuteRead(ref cursor);
                    break;
                case TokenRestore:
                    cursor.Read();
                    ExecuteRestore(ref cursor);
                    break;
                case TokenGoto:
                    cursor.Read();
                    JumpToLine(ParseLineNumber(ref cursor));
                    Consume(StatementCycles);
                    return;
                case TokenGo:
                    cursor.Read();
                    cursor.SkipSpaces();
                    if (cursor.Match(TokenTo))
                    {
                        JumpToLine(ParseLineNumber(ref cursor));
                        Consume(StatementCycles);
                        return;
                    }

                    Unsupported(token);
                    return;
                case TokenGosub:
                    cursor.Read();
                    ExecuteGosub(ref cursor);
                    return;
                case TokenReturn:
                    cursor.Read();
                    ExecuteReturn();
                    return;
                case TokenIf:
                    cursor.Read();
                    ExecuteIf(ref cursor);
                    return;
                case TokenOn:
                    cursor.Read();
                    ExecuteOn(ref cursor);
                    return;
                case TokenPoke:
                    cursor.Read();
                    ExecutePoke(ref cursor);
                    break;
                case TokenSys:
                    cursor.Read();
                    ExecuteSys(ref cursor);
                    break;
                case TokenWait:
                    cursor.Read();
                    ExecuteWait(ref cursor);
                    break;
                case TokenPrint:
                    cursor.SkipStatement();
                    SavePosition(cursor);
                    Consume(StatementCycles);
                    return;
                case TokenInput:
                    cursor.Read();
                    if (!ExecuteInput(ref cursor))
                    {
                        return;
                    }

                    break;
                case TokenGet:
                    cursor.Read();
                    ExecuteGet(ref cursor);
                    break;
                case TokenClr:
                    cursor.Read();
                    ClearVariables();
                    break;
                case TokenLet:
                    cursor.Read();
                    ExecuteAssignment(ref cursor);
                    break;
                default:
                    if (IsNameStart(token))
                    {
                        ExecuteAssignment(ref cursor);
                        break;
                    }

                    Unsupported(token);
                    return;
            }

            SavePosition(cursor);
            Consume(StatementCycles);
        }

        private void ExecuteDim(ref Cursor cursor)
        {
            do
            {
                var name = ParseName(ref cursor);
                cursor.SkipSpaces();
                if (!cursor.Match((byte)'('))
                {
                    Unsupported(0);
                    return;
                }

                var dimensions = new List<int>();
                do
                {
                    dimensions.Add(Math.Max(0, ToInt(ParseNumericExpression(ref cursor))));
                    cursor.SkipSpaces();
                }
                while (cursor.Match((byte)','));

                if (!cursor.Match((byte)')'))
                {
                    Unsupported(0);
                    return;
                }

                _arrays[NormalizeNumericName(name)] = new BasicArray(dimensions.ToArray());
                cursor.SkipSpaces();
            }
            while (cursor.Match((byte)','));
        }

        private void ExecuteFor(ref Cursor cursor)
        {
            var variable = NormalizeNumericName(ParseName(ref cursor));
            cursor.SkipSpaces();
            if (!cursor.Match(TokenEqual) && !cursor.Match((byte)'='))
            {
                Unsupported(0);
                return;
            }

            var start = ParseNumericExpression(ref cursor);
            cursor.SkipSpaces();
            if (!cursor.Match(TokenTo))
            {
                Unsupported(0);
                return;
            }

            var end = ParseNumericExpression(ref cursor);
            var step = 1.0;
            cursor.SkipSpaces();
            if (cursor.Match(TokenStep))
            {
                step = ParseNumericExpression(ref cursor);
            }

            SetNumber(variable, start);
            cursor.SkipStatement();
            _forStack.Push(new ForFrame(variable, end, step, _lineIndex, cursor.Position));
        }

        private void ExecuteNext(ref Cursor cursor)
        {
            string? requested = null;
            cursor.SkipSpaces();
            if (!cursor.End && !cursor.AtColon)
            {
                requested = NormalizeNumericName(ParseName(ref cursor));
            }

            if (_forStack.Count == 0)
            {
                SavePosition(cursor);
                Consume(StatementCycles);
                return;
            }

            var frame = _forStack.Pop();
            if (requested != null && !StringComparer.Ordinal.Equals(requested, frame.Variable))
            {
                frame = FindForFrame(requested, frame);
            }

            var next = GetNumber(frame.Variable) + frame.Step;
            SetNumber(frame.Variable, next);
            var continues = frame.Step >= 0 ? next <= frame.End + 0.0000001 : next >= frame.End - 0.0000001;
            if (continues)
            {
                _forStack.Push(frame);
                _lineIndex = frame.LineIndex;
                _position = frame.Position;
            }
            else
            {
                SavePosition(cursor);
            }

            Consume(StatementCycles);
        }

        private ForFrame FindForFrame(string variable, ForFrame fallback)
        {
            var retained = new Stack<ForFrame>();
            var found = fallback;
            var matched = StringComparer.Ordinal.Equals(variable, fallback.Variable);
            while (!matched && _forStack.Count > 0)
            {
                var candidate = _forStack.Pop();
                if (StringComparer.Ordinal.Equals(variable, candidate.Variable))
                {
                    found = candidate;
                    matched = true;
                    break;
                }

                retained.Push(candidate);
            }

            while (retained.Count > 0)
            {
                _forStack.Push(retained.Pop());
            }

            return found;
        }

        private void ExecuteRead(ref Cursor cursor)
        {
            do
            {
                var target = ParseVariableReference(ref cursor);
                if (_dataIndex >= _data.Count)
                {
                    Halt("BASIC READ ran out of DATA items.");
                    return;
                }

                Assign(target, _data[_dataIndex++]);
                cursor.SkipSpaces();
            }
            while (cursor.Match((byte)','));
        }

        private void ExecuteRestore(ref Cursor cursor)
        {
            cursor.SkipSpaces();
            if (cursor.End || cursor.AtColon)
            {
                _dataIndex = 0;
                return;
            }

            var lineNumber = ParseLineNumber(ref cursor);
            _dataIndex = FindDataIndexForLine(lineNumber);
        }

        private int FindDataIndexForLine(int lineNumber)
        {
            var dataIndex = 0;
            foreach (var line in _lines)
            {
                if (line.LineNumber >= lineNumber)
                {
                    return dataIndex;
                }

                dataIndex += CountDataItems(line);
            }

            return _data.Count;
        }

        private int CountDataItems(BasicLine line)
        {
            var count = 0;
            var cursor = new Cursor(line.Tokens);
            while (!cursor.End)
            {
                cursor.SkipSpacesAndColons();
                if (cursor.End)
                {
                    break;
                }

                if (!cursor.Match(TokenData))
                {
                    cursor.SkipStatement();
                    continue;
                }

                while (!cursor.End && !cursor.AtColon)
                {
                    if (cursor.ReadDataItemText().Length > 0)
                    {
                        count++;
                    }

                    cursor.SkipSpaces();
                    if (!cursor.Match((byte)','))
                    {
                        break;
                    }
                }
            }

            return count;
        }

        private void ExecuteGosub(ref Cursor cursor)
        {
            var target = ParseLineNumber(ref cursor);
            cursor.SkipStatement();
            _returnStack.Push(new ReturnFrame(_lineIndex, cursor.Position));
            JumpToLine(target);
            Consume(StatementCycles);
        }

        private void ExecuteReturn()
        {
            if (_returnStack.Count == 0)
            {
                Halt("BASIC RETURN without GOSUB.");
                return;
            }

            var frame = _returnStack.Pop();
            _lineIndex = frame.LineIndex;
            _position = frame.Position;
            Consume(StatementCycles);
        }

        private void ExecuteIf(ref Cursor cursor)
        {
            var condition = ParseExpression(ref cursor).AsBoolean();
            cursor.SkipSpaces();
            if (!cursor.Match(TokenThen))
            {
                Unsupported(0);
                return;
            }

            cursor.SkipSpaces();
            if (!condition)
            {
                cursor.SkipLine();
                SavePosition(cursor);
                Consume(StatementCycles);
                return;
            }

            if (!cursor.End && char.IsDigit((char)cursor.Peek()))
            {
                JumpToLine(ParseLineNumber(ref cursor));
                Consume(StatementCycles);
                return;
            }

            _position = cursor.Position;
            Consume(StatementCycles);
        }

        private void ExecuteOn(ref Cursor cursor)
        {
            var index = ToInt(ParseNumericExpression(ref cursor));
            cursor.SkipSpaces();
            var gosub = false;
            if (cursor.Match(TokenGoto))
            {
                gosub = false;
            }
            else if (cursor.Match(TokenGosub))
            {
                gosub = true;
            }
            else
            {
                Unsupported(0);
                return;
            }

            var lines = new List<int>();
            do
            {
                lines.Add(ParseLineNumber(ref cursor));
                cursor.SkipSpaces();
            }
            while (cursor.Match((byte)','));

            if (index >= 1 && index <= lines.Count)
            {
                cursor.SkipStatement();
                if (gosub)
                {
                    _returnStack.Push(new ReturnFrame(_lineIndex, cursor.Position));
                }

                JumpToLine(lines[index - 1]);
            }
            else
            {
                SavePosition(cursor);
            }

            Consume(StatementCycles);
        }

        private void ExecutePoke(ref Cursor cursor)
        {
            var address = (ushort)(ToInt(ParseNumericExpression(ref cursor)) & 0xFFFF);
            cursor.SkipSpaces();
            cursor.Match((byte)',');
            var value = (byte)(ToInt(ParseNumericExpression(ref cursor)) & 0xFF);
            _machine.NativeWrite(address, value);
            Consume(PokeCycles);
        }

        private void ExecuteSys(ref Cursor cursor)
        {
            var address = (ushort)(ToInt(ParseNumericExpression(ref cursor)) & 0xFFFF);
            if (!_machine.RunNativeSubroutine(address, SysMaxCycles))
            {
                Halt("Unsupported or non-returning BASIC SYS $" + address.ToString("X4", CultureInfo.InvariantCulture) + ".");
                return;
            }

            cursor.SkipStatement();
        }

        private void ExecuteWait(ref Cursor cursor)
        {
            var address = (ushort)(ToInt(ParseNumericExpression(ref cursor)) & 0xFFFF);
            cursor.SkipSpaces();
            cursor.Match((byte)',');
            var mask = ToInt(ParseNumericExpression(ref cursor)) & 0xFF;
            var compare = 0;
            cursor.SkipSpaces();
            if (cursor.Match((byte)','))
            {
                compare = ToInt(ParseNumericExpression(ref cursor)) & 0xFF;
            }

            for (var i = 0; i < WaitPollLimit; i++)
            {
                var value = _machine.NativeRead(address);
                Consume(64);
                if (((value ^ compare) & mask) != 0)
                {
                    return;
                }
            }

            Halt("BASIC WAIT did not complete before the native runner poll limit.");
        }

        private bool ExecuteInput(ref Cursor cursor)
        {
            cursor.SkipSpaces();
            if (!cursor.End && cursor.Peek() == (byte)'"')
            {
                _ = cursor.ReadStringLiteral();
                cursor.SkipSpaces();
                if (!cursor.Match((byte)';'))
                {
                    cursor.Match((byte)',');
                }
            }

            if (!_machine.TryReadScheduledBasicInputLine(out var line))
            {
                Consume(StatementCycles);
                return false;
            }

            var fields = line.Split(',');
            var fieldIndex = 0;
            do
            {
                var target = ParseVariableReference(ref cursor);
                var text = fieldIndex < fields.Length ? fields[fieldIndex++] : string.Empty;
                if (target.IsString)
                {
                    Assign(target, BasicValue.FromString(text));
                }
                else
                {
                    Assign(
                        target,
                        BasicValue.FromNumber(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0));
                }

                cursor.SkipSpaces();
            }
            while (cursor.Match((byte)','));

            return true;
        }

        private void ExecuteGet(ref Cursor cursor)
        {
            var target = ParseVariableReference(ref cursor);
            Assign(target, BasicValue.FromString(string.Empty));
        }

        private void ExecuteAssignment(ref Cursor cursor)
        {
            var target = ParseVariableReference(ref cursor);
            cursor.SkipSpaces();
            if (!cursor.Match(TokenEqual) && !cursor.Match((byte)'='))
            {
                Unsupported(0);
                return;
            }

            Assign(target, ParseExpression(ref cursor));
        }

        private void ClearVariables()
        {
            _numbers.Clear();
            _strings.Clear();
            _arrays.Clear();
            _forStack.Clear();
            _returnStack.Clear();
            _dataIndex = 0;
        }

        private BasicValue ParseExpression(ref Cursor cursor)
        {
            var left = ParseOr(ref cursor);
            cursor.SkipSpaces();
            if (!TryReadComparator(ref cursor, out var comparison))
            {
                return left;
            }

            var right = ParseOr(ref cursor);
            return BasicValue.FromNumber(Compare(left, right, comparison) ? -1 : 0);
        }

        private BasicValue ParseOr(ref Cursor cursor)
        {
            var left = ParseAnd(ref cursor);
            while (true)
            {
                cursor.SkipSpaces();
                if (!cursor.Match(TokenOr))
                {
                    return left;
                }

                var right = ParseAnd(ref cursor);
                left = BasicValue.FromNumber(ToInt(left.Number) | ToInt(right.Number));
            }
        }

        private BasicValue ParseAnd(ref Cursor cursor)
        {
            var left = ParseAdditive(ref cursor);
            while (true)
            {
                cursor.SkipSpaces();
                if (!cursor.Match(TokenAnd))
                {
                    return left;
                }

                var right = ParseAdditive(ref cursor);
                left = BasicValue.FromNumber(ToInt(left.Number) & ToInt(right.Number));
            }
        }

        private BasicValue ParseAdditive(ref Cursor cursor)
        {
            var left = ParseMultiplicative(ref cursor);
            while (true)
            {
                cursor.SkipSpaces();
                if (cursor.Match(TokenPlus) || cursor.Match((byte)'+'))
                {
                    var right = ParseMultiplicative(ref cursor);
                    left = left.IsString || right.IsString
                        ? BasicValue.FromString(left.AsString() + right.AsString())
                        : BasicValue.FromNumber(left.Number + right.Number);
                    continue;
                }

                if (cursor.Match(TokenMinus) || cursor.Match((byte)'-'))
                {
                    var right = ParseMultiplicative(ref cursor);
                    left = BasicValue.FromNumber(left.Number - right.Number);
                    continue;
                }

                return left;
            }
        }

        private BasicValue ParseMultiplicative(ref Cursor cursor)
        {
            var left = ParseUnary(ref cursor);
            while (true)
            {
                cursor.SkipSpaces();
                if (cursor.Match(TokenMultiply) || cursor.Match((byte)'*'))
                {
                    var right = ParseUnary(ref cursor);
                    left = BasicValue.FromNumber(left.Number * right.Number);
                    continue;
                }

                if (cursor.Match(TokenDivide) || cursor.Match((byte)'/'))
                {
                    var right = ParseUnary(ref cursor);
                    left = BasicValue.FromNumber(Math.Abs(right.Number) < 0.0000001 ? 0 : left.Number / right.Number);
                    continue;
                }

                return left;
            }
        }

        private BasicValue ParseUnary(ref Cursor cursor)
        {
            cursor.SkipSpaces();
            if (cursor.Match(TokenPlus) || cursor.Match((byte)'+'))
            {
                return ParseUnary(ref cursor);
            }

            if (cursor.Match(TokenMinus) || cursor.Match((byte)'-'))
            {
                return BasicValue.FromNumber(-ParseUnary(ref cursor).Number);
            }

            return ParsePrimary(ref cursor);
        }

        private BasicValue ParsePrimary(ref Cursor cursor)
        {
            Consume(ExpressionCycles);
            cursor.SkipSpaces();
            if (cursor.End || cursor.AtColon)
            {
                return BasicValue.Zero;
            }

            if (cursor.Match((byte)'('))
            {
                var value = ParseExpression(ref cursor);
                cursor.SkipSpaces();
                cursor.Match((byte)')');
                return value;
            }

            if (cursor.Peek() == (byte)'"')
            {
                return BasicValue.FromString(cursor.ReadStringLiteral());
            }

            if (char.IsDigit((char)cursor.Peek()) || cursor.Peek() == (byte)'.')
            {
                return BasicValue.FromNumber(cursor.ReadNumber());
            }

            var token = cursor.Peek();
            switch (token)
            {
                case TokenPeek:
                    cursor.Read();
                    return BasicValue.FromNumber(_machine.NativeRead((ushort)(ToInt(ParseFunctionArgument(ref cursor)) & 0xFFFF)));
                case TokenInt:
                    cursor.Read();
                    return BasicValue.FromNumber(Math.Floor(ParseFunctionArgument(ref cursor)));
                case TokenAbs:
                    cursor.Read();
                    return BasicValue.FromNumber(Math.Abs(ParseFunctionArgument(ref cursor)));
                case TokenSgn:
                    cursor.Read();
                    return BasicValue.FromNumber(Math.Sign(ParseFunctionArgument(ref cursor)));
                case TokenRnd:
                    cursor.Read();
                    _ = ParseFunctionArgument(ref cursor);
                    return BasicValue.FromNumber(_random.NextDouble());
                case TokenChr:
                    cursor.Read();
                    return BasicValue.FromString(((char)(ToInt(ParseFunctionArgument(ref cursor)) & 0xFF)).ToString());
            }

            if (IsNameStart(token))
            {
                var reference = ParseVariableReference(ref cursor);
                return Read(reference);
            }

            Unsupported(token);
            cursor.Read();
            return BasicValue.Zero;
        }

        private double ParseFunctionArgument(ref Cursor cursor)
        {
            cursor.SkipSpaces();
            cursor.Match((byte)'(');
            var value = ParseNumericExpression(ref cursor);
            cursor.SkipSpaces();
            cursor.Match((byte)')');
            return value;
        }

        private double ParseNumericExpression(ref Cursor cursor)
        {
            return ParseExpression(ref cursor).Number;
        }

        private bool TryReadComparator(ref Cursor cursor, out string comparison)
        {
            comparison = string.Empty;
            cursor.SkipSpaces();
            if (cursor.End ||
                cursor.AtColon ||
                cursor.Peek() == TokenThen ||
                cursor.Peek() == TokenTo ||
                cursor.Peek() == TokenStep ||
                cursor.Peek() == (byte)',' ||
                cursor.Peek() == (byte)')')
            {
                return false;
            }

            var first = cursor.Peek();
            if (!IsComparator(first))
            {
                return false;
            }

            comparison = ComparatorText(cursor.Read());
            if (!cursor.End && IsComparator(cursor.Peek()))
            {
                comparison += ComparatorText(cursor.Read());
            }

            return true;
        }

        private static bool Compare(BasicValue left, BasicValue right, string comparison)
        {
            var order = left.IsString || right.IsString
                ? string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal)
                : left.Number.CompareTo(right.Number);

            return comparison switch
            {
                "=" => order == 0,
                "<" => order < 0,
                ">" => order > 0,
                "<>" or "><" => order != 0,
                "<=" or "=<" => order <= 0,
                ">=" or "=>" => order >= 0,
                _ => false
            };
        }

        private static bool IsComparator(byte value)
        {
            return value == TokenEqual || value == TokenLess || value == TokenGreater || value == (byte)'=' || value == (byte)'<' || value == (byte)'>';
        }

        private static string ComparatorText(byte value)
        {
            return value switch
            {
                TokenEqual => "=",
                TokenLess => "<",
                TokenGreater => ">",
                _ => ((char)value).ToString()
            };
        }

        private VariableReference ParseVariableReference(ref Cursor cursor)
        {
            var name = ParseName(ref cursor);
            cursor.SkipSpaces();
            if (!cursor.Match((byte)'('))
            {
                return new VariableReference(name, Array.Empty<int>());
            }

            var indices = new List<int>();
            do
            {
                indices.Add(Math.Max(0, ToInt(ParseNumericExpression(ref cursor))));
                cursor.SkipSpaces();
            }
            while (cursor.Match((byte)','));

            cursor.Match((byte)')');
            return new VariableReference(name, indices.ToArray());
        }

        private string ParseName(ref Cursor cursor)
        {
            cursor.SkipSpaces();
            var builder = new StringBuilder();
            while (!cursor.End)
            {
                var value = cursor.Peek();
                if (IsNameCharacter(value))
                {
                    builder.Append(char.ToUpperInvariant((char)cursor.Read()));
                    continue;
                }

                if (value == (byte)'$' || value == (byte)'%')
                {
                    builder.Append((char)cursor.Read());
                }

                break;
            }

            return builder.Length == 0 ? string.Empty : builder.ToString();
        }

        private BasicValue Read(VariableReference reference)
        {
            if (reference.IsString)
            {
                return BasicValue.FromString(_strings.TryGetValue(reference.Name, out var value) ? value : string.Empty);
            }

            if (reference.Indices.Length > 0)
            {
                var name = NormalizeNumericName(reference.Name);
                if (!_arrays.TryGetValue(name, out var array))
                {
                    array = new BasicArray(DefaultDimensions(reference.Indices.Length));
                    _arrays[name] = array;
                }

                return BasicValue.FromNumber(array.Get(reference.Indices));
            }

            return BasicValue.FromNumber(GetNumber(reference.Name));
        }

        private void Assign(VariableReference reference, BasicValue value)
        {
            if (reference.IsString)
            {
                _strings[reference.Name] = value.AsString();
                return;
            }

            if (reference.Indices.Length > 0)
            {
                var name = NormalizeNumericName(reference.Name);
                if (!_arrays.TryGetValue(name, out var array))
                {
                    array = new BasicArray(DefaultDimensions(reference.Indices.Length));
                    _arrays[name] = array;
                }

                array.Set(reference.Indices, CoerceNumber(reference.Name, value.Number));
                return;
            }

            SetNumber(reference.Name, value.Number);
        }

        private static int[] DefaultDimensions(int count)
        {
            var dimensions = new int[count];
            Array.Fill(dimensions, 10);
            return dimensions;
        }

        private void SetNumber(string name, double value)
        {
            _numbers[NormalizeNumericName(name)] = CoerceNumber(name, value);
        }

        private double GetNumber(string name)
        {
            return _numbers.TryGetValue(NormalizeNumericName(name), out var value) ? value : 0;
        }

        private static double CoerceNumber(string name, double value)
        {
            return name.EndsWith("%", StringComparison.Ordinal) ? Math.Truncate(value) : value;
        }

        private static string NormalizeNumericName(string name)
        {
            return name.EndsWith("$", StringComparison.Ordinal) ? name.Substring(0, name.Length - 1) : name;
        }

        private void SavePosition(Cursor cursor)
        {
            _position = cursor.Position;
            if (_lineIndex >= 0 && _lineIndex < _lines.Count && _position >= _lines[_lineIndex].Tokens.Length)
            {
                MoveToNextLine();
            }
        }

        private void MoveToNextLine()
        {
            _lineIndex++;
            _position = 0;
            if (_lineIndex >= _lines.Count)
            {
                _ended = true;
            }
        }

        private void JumpToLine(int lineNumber)
        {
            if (!_lineLookup.TryGetValue(lineNumber, out var index))
            {
                Halt("BASIC target line " + lineNumber.ToString(CultureInfo.InvariantCulture) + " was not found.");
                return;
            }

            _lineIndex = index;
            _position = 0;
        }

        private int ParseLineNumber(ref Cursor cursor)
        {
            cursor.SkipSpaces();
            return ToInt(ParseNumericExpression(ref cursor));
        }

        private ushort ReadWord(ushort address)
        {
            return (ushort)(_machine.Ram[address] | (_machine.Ram[(ushort)(address + 1)] << 8));
        }

        private void Consume(long cycles)
        {
            _cyclesConsumed += cycles;
            _machine.AdvanceNativeCycles(cycles);
        }

        private void Unsupported(byte token)
        {
            _lastUnsupportedToken = token;
            Halt(token == 0
                ? "Unsupported BASIC syntax."
                : "Unsupported BASIC token $" + token.ToString("X2", CultureInfo.InvariantCulture) + ".");
        }

        private void Halt(string diagnostic)
        {
            _halted = true;
            _lastDiagnostic = diagnostic;
        }

        private static int ToInt(double value)
        {
            return (int)Math.Truncate(value);
        }

        private static bool IsNameStart(byte value)
        {
            return value >= (byte)'A' && value <= (byte)'Z' || value >= (byte)'a' && value <= (byte)'z';
        }

        private static bool IsNameCharacter(byte value)
        {
            return IsNameStart(value) || value >= (byte)'0' && value <= (byte)'9';
        }

        private readonly struct BasicLine
        {
            public BasicLine(int lineNumber, byte[] tokens)
            {
                LineNumber = lineNumber;
                Tokens = tokens;
            }

            public int LineNumber { get; }

            public byte[] Tokens { get; }
        }

        private readonly struct VariableReference
        {
            public VariableReference(string name, int[] indices)
            {
                Name = name;
                Indices = indices;
            }

            public string Name { get; }

            public int[] Indices { get; }

            public bool IsString => Name.EndsWith("$", StringComparison.Ordinal);
        }

        private readonly struct ForFrame
        {
            public ForFrame(string variable, double end, double step, int lineIndex, int position)
            {
                Variable = variable;
                End = end;
                Step = step == 0 ? 1 : step;
                LineIndex = lineIndex;
                Position = position;
            }

            public string Variable { get; }

            public double End { get; }

            public double Step { get; }

            public int LineIndex { get; }

            public int Position { get; }
        }

        private readonly struct ReturnFrame
        {
            public ReturnFrame(int lineIndex, int position)
            {
                LineIndex = lineIndex;
                Position = position;
            }

            public int LineIndex { get; }

            public int Position { get; }
        }

        private readonly struct BasicValue
        {
            private BasicValue(double number, string? text)
            {
                Number = number;
                Text = text;
            }

            public double Number { get; }

            public string? Text { get; }

            public bool IsString => Text != null;

            public static BasicValue Zero { get; } = FromNumber(0);

            public static BasicValue FromNumber(double value)
            {
                return new BasicValue(value, null);
            }

            public static BasicValue FromString(string value)
            {
                return new BasicValue(0, value);
            }

            public string AsString()
            {
                return Text ?? Number.ToString(CultureInfo.InvariantCulture);
            }

            public bool AsBoolean()
            {
                return IsString ? !string.IsNullOrEmpty(Text) : Math.Abs(Number) > 0.0000001;
            }
        }

        private sealed class BasicArray
        {
            private readonly int[] _dimensions;
            private readonly double[] _values;

            public BasicArray(int[] dimensions)
            {
                _dimensions = new int[dimensions.Length];
                var count = 1;
                for (var i = 0; i < dimensions.Length; i++)
                {
                    _dimensions[i] = Math.Max(0, dimensions[i]);
                    count *= _dimensions[i] + 1;
                }

                _values = new double[Math.Max(1, count)];
            }

            public double Get(int[] indices)
            {
                return _values[GetOffset(indices)];
            }

            public void Set(int[] indices, double value)
            {
                _values[GetOffset(indices)] = value;
            }

            private int GetOffset(int[] indices)
            {
                var offset = 0;
                var stride = 1;
                for (var i = 0; i < _dimensions.Length; i++)
                {
                    var index = i < indices.Length ? Math.Clamp(indices[i], 0, _dimensions[i]) : 0;
                    offset += index * stride;
                    stride *= _dimensions[i] + 1;
                }

                return offset;
            }
        }

        private struct Cursor
        {
            private readonly byte[] _tokens;

            public Cursor(byte[] tokens, int position = 0)
            {
                _tokens = tokens;
                Position = Math.Clamp(position, 0, tokens.Length);
            }

            public int Position { get; private set; }

            public bool End => Position >= _tokens.Length;

            public bool AtColon => !End && _tokens[Position] == (byte)':';

            public byte Peek()
            {
                return End ? (byte)0 : _tokens[Position];
            }

            public byte Read()
            {
                return End ? (byte)0 : _tokens[Position++];
            }

            public bool Match(byte value)
            {
                SkipSpaces();
                if (End || _tokens[Position] != value)
                {
                    return false;
                }

                Position++;
                return true;
            }

            public void SkipSpaces()
            {
                while (!End && _tokens[Position] == (byte)' ')
                {
                    Position++;
                }
            }

            public void SkipSpacesAndColons()
            {
                while (!End && (_tokens[Position] == (byte)' ' || _tokens[Position] == (byte)':'))
                {
                    Position++;
                }
            }

            public void SkipStatement()
            {
                var inString = false;
                while (!End)
                {
                    if (_tokens[Position] == (byte)'"')
                    {
                        inString = !inString;
                        Position++;
                        continue;
                    }

                    if (!inString && _tokens[Position] == (byte)':')
                    {
                        break;
                    }

                    Position++;
                }
            }

            public void SkipLine()
            {
                Position = _tokens.Length;
            }

            public string ReadStringLiteral()
            {
                if (!Match((byte)'"'))
                {
                    return string.Empty;
                }

                var builder = new StringBuilder();
                while (!End)
                {
                    var value = Read();
                    if (value == (byte)'"')
                    {
                        break;
                    }

                    builder.Append((char)value);
                }

                return builder.ToString();
            }

            public double ReadNumber()
            {
                SkipSpaces();
                var start = Position;
                while (!End)
                {
                    var value = _tokens[Position];
                    if (!char.IsDigit((char)value) && value != (byte)'.')
                    {
                        break;
                    }

                    Position++;
                }

                var text = Encoding.ASCII.GetString(_tokens, start, Position - start);
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
            }

            public string ReadDataItemText()
            {
                SkipSpaces();
                var builder = new StringBuilder();
                var inString = false;
                while (!End)
                {
                    var value = _tokens[Position];
                    if (value == (byte)'"')
                    {
                        inString = !inString;
                        builder.Append((char)value);
                        Position++;
                        continue;
                    }

                    if (!inString && (value == (byte)',' || value == (byte)':'))
                    {
                        break;
                    }

                    builder.Append(value == TokenMinus ? '-' : (char)value);
                    Position++;
                }

                return builder.ToString();
            }
        }
    }
}
