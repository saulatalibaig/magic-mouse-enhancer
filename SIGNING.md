# Code signing

The installers on the Releases page are **not signed**. This file explains why that warning
appears, what actually removes it, and what to do the moment you have a certificate.

## Why SmartScreen warns

Windows shows *"Windows protected your PC"* for any executable whose publisher it cannot chain to a
trusted root **and** which has not yet accumulated download reputation. It is not a malware scan
result — it is the absence of a publisher identity.

There is no build flag, manifest entry or registry setting that removes it. The only thing that
removes it is a signature from a certificate issued by a publicly trusted Certificate Authority.

**A self-signed certificate makes this worse, not better.** The app would then show a publisher name
that Windows flags as untrusted, and the warning gains a scary "unknown publisher" label. Do not
ship a self-signed build.

## Does this machine have a certificate?

No. Verified:

```powershell
Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert
# returns nothing
```

`signtool.exe` is present (Windows SDK 10.0.26100) — so the tooling is ready, the identity is not.

## Options

| Route | Cost (approx.) | Works from Pakistan? | Removes the warning? |
|---|---|---|---|
| **OV code-signing cert** from a CA (Certum, SSL.com, Sectigo, DigiCert) | ~$100–400 / year | Yes — but the private key must live on a FIPS 140-2 Level 2 USB token (shipped to you) or the CA's cloud HSM service | Gradually. Reputation builds with downloads; early users still see it |
| **EV code-signing cert** | ~$300–600 / year + token | Yes, same hardware/HSM rule | **Immediately**, from the first download |
| **Azure Trusted / Artifact Signing** | ~$10 / month | **No.** Microsoft restricts Public Trust to a specific country list (individuals: US/CA; organisations: US, CA, EU, UK, AU, NZ, JP, KR, SG, CH, NO, IL). Pakistan is not on it. Confirm on the signup page before paying | Yes |
| **SignPath Foundation** | Free | Yes | Yes — but it requires the project to be **public open source** and signed through CI. These repos are private |
| **Microsoft Store (MSIX)** | Free–$19 one-time | Yes | Yes — the Store re-signs the package. Different distribution model |

Since June 2023 the CA/Browser Forum requires code-signing private keys to be held in FIPS 140-2
Level 2 hardware, so "just a .pfx file" is no longer how OV/EV certs are issued — you get a USB token
or a cloud HSM. Budget for that in the lead time.

**Practical recommendation:** if you want the warning gone for everyone immediately, an **EV**
certificate is the answer. If you want the cheapest legitimate route and can accept a slow reputation
build, **OV** from Certum is the usual choice for individuals. Prices move — check the CA's own
store page rather than a reseller's.

## Once you have the certificate

### Electron apps — `netatlas`, `gemini-desk`, `badger-suite`

`electron-builder` signs during the build when these environment variables are set. It signs both the
packaged app executable and the installer.

```powershell
# Do not commit these. Set them in the session only.
$env:CSC_LINK = "C:\path\to\certificate.pfx"   # or a base64 of the pfx
$env:CSC_KEY_PASSWORD = "<password>"

npm run dist
```

For a **hardware token / cloud HSM** instead of a PFX file, point electron-builder at the certificate
already in your store:

```powershell
$env:CSC_NAME = "Your Company Name"     # matches the certificate subject
npm run dist
```

`netatlas` additionally has `signAndEditExecutable: false` in `electron-builder.yml` — that switch
exists to work around the signing toolchain on cert-less builds. **Set it back to `true` (or delete
the line) before signing**, or electron-builder will package without applying the signature.

Then confirm:

```powershell
Get-AuthenticodeSignature .\release\NetAtlas-Setup-1.0.0.exe | Format-List Status, SignerCertificate
# Status should be: Valid
```

### C# / `csc.exe` build — `MagicMouseEnhancer`

There is no build-tool integration, so sign the output directly:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\sign-release.ps1 `
    -Path .\bin\*.exe `
    -Thumbprint <certificate-thumbprint> `
    -Description "Magic Mouse Enhancer"
```

## `tools/sign-release.ps1`

A wrapper around `signtool` that sets the options people get wrong: SHA-256 digest, RFC 3161
timestamp, and verification afterwards.

| Parameter | Meaning |
|---|---|
| `-Path` | files or globs (`.\release\*.exe`). Only real PE binaries are picked up — `.blockmap` and checksum files are skipped |
| `-Thumbprint` | SHA-1 thumbprint of a certificate in `CurrentUser\My` or `LocalMachine\My` |
| `-Subject` | alternative to the thumbprint — matches on subject name |
| `-Pfx` / `-PfxPassword` | sign from a `.pfx`; password falls back to `$env:SIGN_PFX_PASSWORD` |
| `-Description` | the description shown in the UAC / file-properties dialog |
| `-TimestampUrl` | defaults to `http://timestamp.digicert.com` |
| `-DryRun` | print the exact `signtool` command and change nothing |

**Timestamping matters.** Without `-TimestampUrl`, the signature becomes invalid the instant the
certificate expires and every previously shipped installer starts warning again. The script always
timestamps and has no option to skip it.

The script verifies every file it signs with `signtool verify /pa /all` and exits non-zero if any
signature does not chain to a trusted root — because a signature Windows does not trust is worse than
no signature.

Exit codes: `0` all signed and verified · `1` signtool missing or verification failed · `2` no usable
certificate.
