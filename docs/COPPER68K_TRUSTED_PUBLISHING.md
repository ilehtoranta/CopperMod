# Copper68k NuGet Trusted Publishing

Copper68k packages are published from GitHub Actions using NuGet.org Trusted
Publishing. The workflow exchanges GitHub's OIDC identity for a short-lived
NuGet API key; do not create or store a long-lived NuGet API key.

## One-time account setup

On NuGet.org, open **Trusted Publishing** for the account that owns Copper68k
and create a policy with these values:

- Owner: `ilehtoranta`
- Repository: `CopperMod`
- Workflow file: `publish-copper68k-1.4.1.yml`
- Environment: leave empty; the workflow does not use a GitHub environment
- Scope: allow publishing new versions of the `Copper68k` package

The workflow supplies the public NuGet.org profile name `ilehtoranta` to the
login action. It is the account name shown on the existing CopperDisk package
owner profile, not an email address or credential; no GitHub secret is needed.

## Approved releases

The stable `1.5.4` release was authorized on 2026-10-22. Tag the reviewed stable
release commit as `copper68k-v1.5.4`. The existing policy and workflow path stay
unchanged. The workflow runs the CPU suite, validates required synthetic reports
and their exact case counts, tests AHX, validates package contents, and publishes
the NuGet package and symbols. It retains the approved known-coverage scope and
five deferred qualification areas. The source promotion adds no CPU execution
changes beyond the already merged qualified suite and memory BTST prefetch fix.

If an unchanged `1.5.4` release needs a transient/partial-upload retry, use the
next explicit `copper68k-v1.5.4-retry-1` through `-retry-3` tag at the exact same
reviewed commit. Never replace an existing package or tag; changed content needs
a new version.

The following notes retain the earlier approved release protocol.

The existing trusted-publishing policy is bound to the workflow file
`publish-copper68k-1.4.1.yml`; keep that path unchanged unless the NuGet policy
is updated. The workflow accepts only its explicit version tags. For the
authorized mainline `1.5.0` release, review the release commit and then push
`copper68k-v1.5.0`. It runs the Copper68k CPU tests, creates and
validates the Release `.nupkg` and `.snupkg`, then publishes both to NuGet.org.
The tag is intentionally not created by the workflow or by ordinary source
pushes. The normal package version replaces the historical `ocs020` development
suffix; advanced CPU profiles retain the experimental and diagnostic limitations
in the package README. It does not certify full game or physical accelerator
compatibility. Older explicitly approved `.52` and `.54` tags remain accepted.

If a `1.5.0` run fails transiently or after a partial upload, push the next
explicit retry tag (`copper68k-v1.5.0-retry-1` through
`-retry-3`) at the exact same reviewed release commit. Keep all earlier tags
intact. Each retry maps to the same immutable package version. If source or
workflow changes are needed, prepare a new candidate version rather than
reusing the `1.5.0` retry tags.

The workflow uses `--skip-duplicate` so a retry after a partial upload can
finish the remaining package upload. NuGet package versions are immutable; a
retry never replaces an already published package.

The normal packing gate runs the CPU suite and the retained AHX consumer suite.
The pre-extraction `CopperMod.Amiga.Tests` project still depends on removed
CopperStart components and is not the active consumer gate. CopperScreen owns
its separate engine, host and native-media validation against the public package.
