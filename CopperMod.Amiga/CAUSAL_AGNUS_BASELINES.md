# Causal Agnus Regression Baselines

## Lemmings SR

The A500 PAL OCS unified causal-executor baseline is:

- framebuffer checksum: `0x03E3F1D1`
- final-frame audio checksum: `0xFA40F11D`
- final-frame audio shape: 883 stereo frames, 1626 nonzero samples, peak 0.2897

The former `0x6C7AC40D`/`0xCF07D11D` pair came from a legacy trace that first
granted a CPU access and then replaced that same physical slot with Copper.
It is retained as a historical migration checkpoint, but cannot be a causal
single-owner baseline. The explicit 1,208-case physical-bus matrix has no
causal-only failures and the legacy executor has two unique failures, including
the focused CPU/blitter completion-slot ownership test.

The earlier framebuffer checksum `0xDC562E0D` and audio checksum `0xB2EC211D`
depended on deferred sampling and retrospective slot insertion. The intermediate
audio checksum `0x80D1211D` used causal presentation while still forcing the
legacy bus executor. They are retained only as historical references and must
not be used to reintroduce out-of-order Chip RAM or custom-register execution.
The former causal audio checksum `0xE39DA91D` predates six-byte host gateways;
the 32-bit gateway token changes the deterministic CopperStart boot phase but
does not change the measured framebuffer or runtime DMA grant sequence.

Run the executable gate from the repository root:

```powershell
dotnet run --project .\CopperScreen.Benchmarks\CopperScreen.Benchmarks.csproj -c Release -- `
  --only "Lemmings SR" `
  --profile expanded-copperstart.json `
  --cpu interpreter `
  --warmup 600 `
  --frames 360 `
  --repeat 1 `
  --hardware-specialization `
  --expect-framebuffer-checksum 03E3F1D1 `
  --expect-audio-checksum FA40F11D
```

The gate requires the locally available Lemmings SR disk image. A missing image
is optional corpus coverage, not a hardware-test failure.
