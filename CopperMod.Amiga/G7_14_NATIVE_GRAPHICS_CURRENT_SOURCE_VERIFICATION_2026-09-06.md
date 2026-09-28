# Native-graphics current-source verification

Status: **8/8 selected cases PASS**. G6 STOP; production Legacy;
conditional authorization14 unused. No native-graphics source edited here.

The preceding182A full family failed three branch-audit rows and the accurate
68000 CoerceMode viewport test. Swapping only Amiga back to109B preserved all
four errors, ruling out the ECS origin correction as their cause.

Current source was then inspected, not assumed equal to that frozen family.
The audit fixtures already contain newer expected generated-code counts;
the viewport fixture explicitly constructs a ColorMap-associated source and
uses valid plane depths for its geometry checks. These changes pre-existed
this verification and were preserved. Their relevant files were last written
at04:17:32 local, after the older full-family build. Do not overwrite that work
or attribute it to this gate-verification step.

Fresh Release build79076 succeeded in28.04s,0 errors, one existing xUnit2013
warning at DosRunCommandRetirementInspectionTests.cs:267. Family:
`artifacts/g7-14-readiness-refresh-release-bin`.

- Amiga: `182A79179DC3B3417C7B780270E320D6F851E7AD1C73AD12033D98B16D6A2B48`.
- CPU: `9A9F0C758A97B4AC2987C5EAD140580EFFA495CD90377ADD95642220B2ABC120`.
- Emulator: `5AADFBBAE3ACFE2DA9BF228F7D56C74C73692CA08E53D0EFBEC6234C62F4F292`.
- Tests: `04C1B0FB02979683744CCF629D55E14F2C111D627E1210380B78C908C82DDCB1`.

`artifacts/g7-14-readiness-refresh-results/native-current-source.trx`:
8/8 PASS,657ms. Includes the four previously failing rows and four independent
native associated-source/ROM comparisons. Audit tests retain branch target,
alignment, range and emitted image checks in addition to count fingerprints.
This confirms the selected current-source cases, not every native-graphics
contract or the broader CopperStart experimental suite. The CPU skill informed
the scoped execution verification and separation from whole-build readiness.

Fresh full suite **80771** is running against this family, results target
`artifacts/g7-14-readiness-refresh-results/full.trx`, log
`artifacts/g7-14-readiness-refresh-full.log`. It includes the later ECS alpha,
matrix-origin, row-plan and disk/display test reconciliations. Wait for actual
terminal counts, classify remaining production-relevant failures and retain
the existing G6/performance gates. No formal throughput sample was run.
