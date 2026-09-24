# Copper68k NuGet Trusted Publishing

Stable Copper68k releases are published from GitHub Actions using NuGet.org
Trusted Publishing. The workflow exchanges GitHub's OIDC identity for a
short-lived NuGet API key; do not create or store a long-lived NuGet API key.

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

## Copper68k 1.4.1 release

After the policy is configured, push the exact tag
`copper68k-v1.4.1` at the reviewed release commit. The workflow runs the
Copper68k CPU tests, creates and validates the Release `.nupkg` and `.snupkg`,
then publishes both to NuGet.org. The tag is intentionally not created by the
workflow or by ordinary source pushes.

If a workflow run fails before upload, correct the missing setup and push the
next explicit retry tag (`copper68k-v1.4.1-retry-1` through
`copper68k-v1.4.1-retry-3`) at the corrected workflow commit. Keep all earlier
tags intact.

The workflow uses `--skip-duplicate` so a retry after a partial upload can
finish the remaining package upload. NuGet package versions are immutable; a
retry never replaces an already published package.
