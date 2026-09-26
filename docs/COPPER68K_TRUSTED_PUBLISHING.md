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

The existing trusted-publishing policy is bound to the workflow file
`publish-copper68k-1.4.1.yml`; keep that path unchanged unless the NuGet policy
is updated. The workflow accepts only its explicit version tags. For the
experimental OCS 020 prerelease, review the release commit and then push
`copper68k-v1.4.2-ocs020.52`. It runs the Copper68k CPU tests, creates and
validates the Release `.nupkg` and `.snupkg`, then publishes both to NuGet.org.
The tag is intentionally not created by the workflow or by ordinary source
pushes. The `.52` package is a prerelease and does not certify full game or
physical accelerator compatibility.

If a `.52` run fails transiently or after a partial upload, push the next
explicit retry tag (`copper68k-v1.4.2-ocs020.52-retry-1` through
`-retry-3`) at the exact same reviewed release commit. Keep all earlier tags
intact. Each retry maps to the same immutable package version. If source or
workflow changes are needed, prepare a new candidate version rather than
reusing the `.52` retry tags.

The workflow uses `--skip-duplicate` so a retry after a partial upload can
finish the remaining package upload. NuGet package versions are immutable; a
retry never replaces an already published package.
