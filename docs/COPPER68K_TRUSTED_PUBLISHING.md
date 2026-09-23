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

In GitHub repository **Settings → Secrets and variables → Actions**, add the
repository secret `NUGET_USER` with the NuGet.org profile username (not the
email address). This is the account name supplied to NuGet's login action; it
is not an API key.

## Copper68k 1.4.1 release

After the policy and `NUGET_USER` secret are configured, push the exact tag
`copper68k-v1.4.1` at the reviewed release commit. The workflow runs the
Copper68k CPU tests, creates and validates the Release `.nupkg` and `.snupkg`,
then publishes both to NuGet.org. The tag is intentionally not created by the
workflow or by ordinary source pushes.

The workflow uses `--skip-duplicate` so a retry after a partial upload can
finish the remaining package upload. NuGet package versions are immutable; a
retry never replaces an already published package.
