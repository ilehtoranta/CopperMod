# CopperFloat

CopperFloat provides deterministic, allocation-free extended 80-bit floating-point
arithmetic for .NET. Values retain their complete 80-bit encoding, including NaN
payloads and noncanonical encodings, while arithmetic uses an explicit rounding
context and returns IEEE exception flags with each result.

The implementation is written in managed integer arithmetic. It does not depend on
the host floating-point environment and has no runtime package dependencies.

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
