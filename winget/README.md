# winget manifests

Package manifests for [Windows Package Manager](https://learn.microsoft.com/windows/package-manager/),
so users can install with:

```powershell
winget install SaulatBaig.MagicMouseEnhancer
```

## Layout

The files mirror the exact directory structure the
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) repository requires:

```
winget/manifests/s/SaulatBaig/MagicMouseEnhancer/1.2.0/
├── SaulatBaig.MagicMouseEnhancer.yaml              # version
├── SaulatBaig.MagicMouseEnhancer.installer.yaml    # URLs, SHA256, switches
└── SaulatBaig.MagicMouseEnhancer.locale.en-US.yaml # display metadata
```

The leading `s` is required: winget-pkgs buckets packages under the first letter of the
publisher segment of the PackageIdentifier (`S`aulatBaig → `s`).

The three-file (multi-file) manifest format is what winget-pkgs now requires for new
packages. `ManifestVersion` is `1.12.0`, the current schema.

## Validate before submitting

```powershell
winget validate --manifest .\winget\manifests\s\SaulatBaig\MagicMouseEnhancer\1.2.0
winget show     --manifest .\winget\manifests\s\SaulatBaig\MagicMouseEnhancer\1.2.0
```

To exercise the real download + silent install end to end (installs the app on the
machine you run it on):

```powershell
winget install --manifest .\winget\manifests\s\SaulatBaig\MagicMouseEnhancer\1.2.0
```

## Submitting to winget-pkgs

**Option A — wingetcreate (recommended, handles fork and PR):**

```powershell
winget install Microsoft.WingetCreate
wingetcreate submit --pkg-id SaulatBaig.MagicMouseEnhancer `
  --version 1.2.0 `
  --urls https://github.com/saulatalibaig/magic-mouse-enhancer/releases/download/v1.2.0/MagicMouseEnhancer-Setup-1.2.0.exe
```

`wingetcreate` prompts for the publisher, licence and links, downloads the installer to
compute the hash itself, then opens the pull request from a fork.

**Option B — manual:**

1. Fork `microsoft/winget-pkgs` and clone your fork.
2. Copy this folder's `manifests/s/SaulatBaig/MagicMouseEnhancer/1.2.0/` into the fork at
   the same relative path (`<fork>/manifests/s/SaulatBaig/MagicMouseEnhancer/1.2.0/`).
3. Commit, push, and open a PR against `microsoft/winget-pkgs` `master`.

A bot validates the PR (`winget validate`, schema checks, an actual silent install in a
VM, plus a check that the installer's ARP entry matches `AppsAndFeaturesEntries`). A
moderator then merges it. Expect a few days.

## Notes on this package

| Field | Value | Source |
|---|---|---|
| PackageIdentifier | `SaulatBaig.MagicMouseEnhancer` | publisher + app name |
| InstallerType | `inno` | `installer/setup.iss` is an Inno Setup script |
| Scope | `user` | `PrivilegesRequired=lowest` |
| Architecture | `x64` | `ArchitecturesAllowed=x64compatible` |
| MinimumOSVersion | `10.0.17763.0` | `MinVersion=10.0.17763` |
| ProductCode | `{B7C0A9E1-4D2F-4E8A-9C3B-6F5D8A1E2B44}_is1` | Inno `AppId` + `_is1` suffix for per-user installs |
| Silent switches | `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` | Inno Setup convention |

## Updating for a new release

Bump `PackageVersion` in all three files, then update `InstallerUrl` and
`InstallerSha256` in the installer manifest to the new tag. To get the hash without
downloading by hand:

```powershell
$u = 'https://github.com/saulatalibaig/magic-mouse-enhancer/releases/download/v1.3.0/MagicMouseEnhancer-Setup-1.3.0.exe'
$f = Join-Path $env:TEMP 'setup.exe'
Invoke-WebRequest -Uri $u -OutFile $f -UseBasicParsing
(Get-FileHash -Algorithm SHA256 $f).Hash
```

Copy the new version folder (`1.2.0` → `1.3.0`) rather than editing in place, then verify
with `winget validate` before opening the PR.
