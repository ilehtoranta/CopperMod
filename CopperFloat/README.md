# CopperFloat

CopperFloat provides deterministic, allocation-free extended 80-bit floating-point
arithmetic for .NET. Values retain their complete 80-bit encoding, including NaN
payloads and noncanonical encodings, while arithmetic uses an explicit rounding
context and returns IEEE exception flags with each result.

The first public preview targets `net10.0`:

```sh
dotnet add package CopperFloat --version 0.1.0-preview.1
```

The architectural arithmetic and final rounding decisions use managed integer
arithmetic. A few common binary32/binary64 operations may use hardware instructions
only behind exactness checks; host rounding state never determines the result. The
package has no runtime dependencies.

## Accuracy and scope

CopperFloat preserves raw extF80 encodings and models zero, subnormal, normal,
infinity, quiet/signaling NaN, and unsupported values. Arithmetic supports explicit
24-, 53-, and 64-bit precision, four rounding modes, tininess selection, and
exception flags. It does not provide a process-global floating-point environment,
trap delivery, Motorola FPSR/FPCR policy, or a memory layout for 12-byte Motorola
extended slots; those belong in the CPU integration layer.

The committed CopperFloat test fixtures are generated from Berkeley TestFloat 3e.
The external level-2 oracle is optional and uses an independently installed
TestFloat/SoftFloat toolchain; neither is distributed in the package.

## Representation

```csharp
using CopperFloat;

var one = ExtF80.FromBits(0x3fff, 0x8000_0000_0000_0000);
Span<byte> encoded = stackalloc byte[ExtF80.EncodedSize];
one.WriteBigEndian(encoded);
```

`ExtF80` deliberately has no public in-memory layout contract. Use `FromBits`,
`ReadBigEndian`, `ReadLittleEndian`, `WriteBigEndian`, or `WriteLittleEndian` when
interchanging raw values.

## Arithmetic

```csharp
var context = ExtF80Context.Default;
var left = ExtF80Math.FromInt32(6);
var right = ExtF80Math.FromInt32(4);
var quotient = ExtF80Math.Divide(left, right, context);

Console.WriteLine(quotient.Value);
Console.WriteLine(quotient.Flags);
```

The rounding context is passed explicitly. CopperFloat does not use process-global
or thread-local floating-point state.
