# Berkeley TestFloat fixture

`testfloat-extf80-level1.tsv` was generated from Berkeley TestFloat 3e with
the default deterministic seed (`1`) and level-1 operand generation. It keeps
the first eight cases for each combination of:

- add, subtract, multiply, divide, square root, and round-to-integer;
- nearest-even, toward-zero, toward-negative, and toward-positive rounding;
- 24-bit, 53-bit, and 64-bit significand precision;
- tininess detection before and after rounding.

`testfloat-extf80-conversions-level1.tsv` covers signed 32/64-bit integer and
binary32/binary64 conversions for every rounding and tininess mode.

Regenerate it with `Tools/generate-testfloat-fixture.sh` and an external
`testfloat_gen` executable. Berkeley TestFloat and Berkeley SoftFloat are not
compiled into or distributed with CopperFloat.

Berkeley TestFloat Release 3e is Copyright 2011-2017 The Regents of the
University of California and distributed under the BSD 3-Clause license.
