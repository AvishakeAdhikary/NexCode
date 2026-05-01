# NexCode Manual Completion Guide

This guide walks a developer end-to-end through every remaining manual step required to ship NexCode v1.0 to the Microsoft Store. Slices 0001–0018 are code-complete, 219/219 unit tests pass, and the build is green for every assembly except `NexCode.Gui` (which trips a known WinAppSDK 2.0.1 / .NET 10 SDK toolchain bug — see [§2](#2-resolving-the-xamlcompilerexe-pass2-build-break)).

The remaining work is operational: pin the SDK, register Azure AD, buy or generate a code-signing certificate, set up Partner Center add-ons, wire GitHub Actions secrets, bundle binary assets, and submit. Every section below is actionable — copy-paste the commands, follow the screenshots in your portal, and tick the box.

---

## Table of contents

1. [Prerequisites & environment](#1-prerequisites--environment)
2. [Resolving the XamlCompiler.exe Pass2 build break](#2-resolving-the-xamlcompilerexe-pass2-build-break)
3. [Azure AD application for MSAL SSO](#3-azure-ad-application-for-msal-sso)
4. [Code signing certificate](#4-code-signing-certificate)
5. [Microsoft Partner Center & IAP add-ons](#5-microsoft-partner-center--iap-add-ons)
6. [GitHub Actions secrets](#6-github-actions-secrets)
7. [Partner Center API access for automated Store submission](#7-partner-center-api-access-for-automated-store-submission-optional)
8. [GitHub Pages](#8-github-pages)
9. [Bundling assets (Monaco, Node.js, WebView2 fixed runtime, Cascadia Code)](#9-bundling-assets-monaco-nodejs-webview2-fixed-runtime-cascadia-code)
10. [LSP servers & MCP servers (optional)](#10-lsp-servers--mcp-servers-optional)
11. [Telemetry receiver (optional)](#11-telemetry-receiver-optional-for-collecting-opt-in-training-data)
12. [Local development workflow](#12-local-development-workflow)
13. [Provider configuration (first-run UX)](#13-provider-configuration-first-run-ux)
14. [Sub-agent + sandbox + permission walkthrough](#14-sub-agent--sandbox--permission-walkthrough)
15. [Building the standalone Inno Setup installer](#15-building-the-standalone-inno-setup-installer)
16. [First Microsoft Store submission](#16-first-microsoft-store-submission)
17. [Post-launch operational checklist](#17-post-launch-operational-checklist)
18. [Known deferred work (not blocking v1.0 ship)](#18-known-deferred-work-not-blocking-v10-ship)
19. [Troubleshooting](#19-troubleshooting)

---

## 1. Prerequisites & environment

Install everything below before touching the repo. Versions matter — older versions of WinAppSDK or VS 2022 will silently produce broken MSIX packages.

| Requirement | Version | Why |
|---|---|---|
| Windows 11 | 22H2 or newer (build 22621+) | WinAppSDK 1.6 / 2.0 minimum target |
| Visual Studio 2022 | 17.10 or newer | C# templates, MSBuild, MSIX packaging |
| **VS workload** | "Windows App SDK C# Templates" | The single-project MSIX template ships here |
| **VS workload** | ".NET desktop development" | C# project system, F5 launch |
| **VS workload** | "Universal Windows Platform development" | Required for `MakePri.exe`, packaging, and resource compilation |
| .NET SDK | **9.0.305** (pinned via `global.json`) | See [§2](#2-resolving-the-xamlcompilerexe-pass2-build-break) for why |
| PowerShell | 7.5 or newer | All `scripts/build/*.ps1` use PS7 syntax |
| Git | 2.45 or newer | LFS, partial clone, sparse checkout |
| Inno Setup (optional) | 6.2 or newer | Required only if you ship the standalone installer in [§15](#15-building-the-standalone-inno-setup-installer) |
| `signtool.exe` (optional) | Latest Windows SDK | Required only for local signing — GitHub Actions uses the SDK shipped with the runner image |
| Node.js LTS (optional) | 20.x or newer | Required only if you bundle the LSP runtime locally — see [§9](#9-bundling-assets-monaco-nodejs-webview2-fixed-runtime-cascadia-code) |

Verify after install:

```powershell
# All four should return non-empty version strings.
dotnet --info | Select-String "Version"
pwsh -Version
git --version
& "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe" /? | Out-Null; $LASTEXITCODE
```

Expected output for `dotnet --info`:

```
Version: 9.0.305
```

If you see `10.0.x`, jump straight to [§2](#2-resolving-the-xamlcompilerexe-pass2-build-break).

---

## 2. Resolving the XamlCompiler.exe Pass2 build break

The current `Microsoft.WindowsAppSDK 2.0.1` package ships an `XamlCompiler.exe` that has a regression on the .NET 10 SDK: the **Pass2** invocation reports success in the build log, writes a **0-byte `.g.cs`** file for every `*.xaml` page, and then exits with code **1** silently. Net result: `NexCode.Gui.csproj` fails with hundreds of `CS0103: The name 'InitializeComponent' does not exist` errors during the C# compile step that follows Pass2.

### Failure signature

```text
Page : warning : XamlCompiler : Pass2 succeeded
obj\Debug\net9.0-windows10.0.26100.0\App.g.cs(0,0): error : 0-byte file
NexCode.Gui.csproj : error MSB3073 : exited with code 1.
```

### Resolution

Pin the SDK at the repo root with `global.json`:

```json
{
  "sdk": {
    "version": "9.0.305",
    "rollForward": "feature"
  }
}
```

The file already lives at `c:\Projects\NexCode\global.json`. If you nuked it, recreate it verbatim. `feature` roll-forward lets you accept patch bumps (9.0.306, 9.0.307…) but blocks the major-version trip to 10.0.x.

### Verify

```powershell
cd c:\Projects\NexCode
dotnet --version
# expected: 9.0.305

dotnet build NexCode.slnx
# expected: Build succeeded. 0 Warning(s). 0 Error(s).
```

### Rollback note

This pin disappears as soon as `Microsoft.WindowsAppSDK 2.1+` ships with .NET 10 SDK support (or whichever release Microsoft cuts to fix XamlCompiler Pass2). At that point delete `global.json` and let the repo float on the latest SDK. Track the issue at <https://github.com/microsoft/microsoft-ui-xaml/issues>.

---

## 3. Azure AD application for MSAL SSO

NexCode signs users in with Microsoft Entra ID (formerly Azure AD) using MSAL.NET in the helper. You must register an application before sign-in works.

### Steps in the Azure portal

1. Go to <https://portal.azure.com> → **Microsoft Entra ID** → **App registrations** → **New registration**.
2. Fill the form:
   - **Name**: `NexCode`
   - **Supported account types**: **Accounts in any organizational directory (Any Microsoft Entra directory — Multitenant) and personal Microsoft accounts (e.g., Skype, Xbox)**.
   - **Redirect URI**: pick **Public client/native (mobile & desktop)** from the dropdown, then enter `https://login.microsoftonline.com/common/oauth2/nativeclient`.
3. Click **Register**.
4. On the **Overview** blade, copy:
   - **Application (client) ID** → save as `NEXCODE_AAD_CLIENT_ID`.
   - **Directory (tenant) ID** → save as `NEXCODE_AAD_TENANT_ID`. Use `common` if you want consumer + work accounts, or paste your tenant GUID if you're locking it down.
5. Go to **Authentication** → scroll to **Advanced settings** → set **Allow public client flows** to **Yes** → **Save**.
6. Go to **API permissions** → **Add a permission** → **Microsoft Graph** → **Delegated permissions** → check `User.Read` → **Add permissions**.
7. **Grant admin consent (optional for multitenant + MSA apps).** Skip this if you registered the app with multitenant + personal-MSA support and don't want to consent on behalf of "all users in your organization". MSAL public-client native flows self-consent **per individual user** on first sign-in — every user just sees the standard consent screen for `User.Read`. Grant admin consent only when you're rolling NexCode out inside a single org and want to suppress the per-user prompt for that one tenant.

### Where to paste the IDs locally

> **Public vs secret.** Azure AD **Application (client) ID** and **Directory (tenant) ID** are *public* identifiers — they are exposed by `https://login.microsoftonline.com/{tenantId}/.well-known/openid-configuration` for any AAD tenant and cannot be used as credentials on their own. MSAL public-client native flows have **no client secret** by design. You can technically commit these IDs to a public repo without harming security. We still keep them in a gitignored local file by default, partly out of habit and partly to keep dev-machine identifiers off the main branch.

The recommended path: copy `src/NexCode.Service/appsettings.Local.json.example` to `src/NexCode.Service/appsettings.Local.json` and paste your IDs there. `appsettings.Local.json` is in `.gitignore`, and `Program.cs` loads it after `appsettings.Development.json` so its values shadow.

```json
{
  "Auth": {
    "ClientId": "96cb39d8-5dc5-49dc-b42a-a6fa9afe3630",
    "TenantId": "247b3ce0-a1e7-429f-8bb0-b438d987fd09",
    "RedirectUri": "https://login.microsoftonline.com/common/oauth2/nativeclient"
  }
}
```

Or set the matching environment variables before launching the helper (also gitignored in `set-dev-env*.ps1`):

```powershell
$env:NEXCODE_AAD_CLIENT_ID  = "<your client id>"
$env:NEXCODE_AAD_TENANT_ID  = "<your tenant id or 'common'>"
dotnet run --project src/NexCode.Service
```

The helper reads env vars first, then `appsettings.Local.json` (gitignored), then `appsettings.Development.json` (tracked, no secrets), then production `appsettings.json`. Production builds get the IDs injected by GitHub Actions from the secrets in [§6](#6-github-actions-secrets).

---

## 4. Code signing certificate

Spec §41 Step 2 requires a signed `.msix`. You have three paths.

> **Recommended path for "Store-only distribution + local sideload for testing":** use **Option A** below to generate a self-signed cert (10 seconds), trust it on this machine for sideload installs, and ship to the Store with **Option C** — the Store re-signs your `.msix` with Microsoft's publicly-trusted publisher cert during ingestion, so the self-signed cert is **only** used for your local install. This is the cheapest, fastest path and is what most v1.0 NexCode operators are doing.

### Option A — Self-signed (development only)

Use this for local F5 / sideload runs only. Windows refuses to install self-signed packages without explicit trust-store import.

> **Critical**: the certificate **`Subject` must exactly match `<Identity Publisher="...">`** in `src/NexCode.Gui/Package.appxmanifest`, including case and trailing whitespace, or `Add-AppxPackage` will fail with `0x80073CF0` ("the publisher of an installed package does not match the publisher of the package being installed"). The manifest is currently set to `Publisher="CN=8559855F-6955-4418-A6D8-835C3238CF34"` (your Partner Center publisher identity), so the cert subject below uses that exact value. If you swap the manifest to a different publisher you must regenerate the cert.

Run as a **non-elevated** PowerShell (the `Cert:\CurrentUser\My` store doesn't need admin); the second `Import-PfxCertificate` line that pushes into `Cert:\LocalMachine\TrustedPeople` does — open an elevated PS for that one.

```powershell
# 1) Generate self-signed cert with the Subject the manifest expects.
$cert = New-SelfSignedCertificate `
  -Type Custom `
  -Subject "CN=8559855F-6955-4418-A6D8-835C3238CF34" `
  -KeyUsage DigitalSignature `
  -FriendlyName "NexCode Dev Code Signing" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
  -NotAfter (Get-Date).AddYears(2)

# 2) Export to .pfx so MSBuild's signing target can find it.
$pwd = ConvertTo-SecureString -String "ChangeMe!" -Force -AsPlainText
$pfx = Join-Path $PSScriptRoot "nexcode-dev.pfx"
Export-PfxCertificate `
  -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" `
  -FilePath $pfx `
  -Password $pwd

Write-Host "Wrote $pfx (thumbprint $($cert.Thumbprint))"

# 3) Trust it on THIS machine so Add-AppxPackage / sideload installs work.
#    Run this part from an ELEVATED PowerShell (Start as Administrator).
Import-PfxCertificate -FilePath $pfx -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $pwd
```

The exported `.pfx` is gitignored (`*.pfx` rule in `.gitignore`). Don't commit it.

### Option B — Commercial CA (non-Store distribution)

Buy from DigiCert, Sectigo, or GlobalSign. Expect ~$200–$500 per year for a standard code-signing cert, or ~$300–$700 for an EV cert (instant SmartScreen reputation). EV certs ship on a hardware token (USB-A) and require physical presence to sign — set up GitHub Actions self-hosted runners or use a cloud HSM if you want unattended signing.

Standard non-EV certs come as a `.pfx` file and work fine with the `signtool.exe` step in `.github/workflows/release.yml`.

### Option C — Microsoft Store only

If your **only** distribution channel is the Microsoft Store, you don't need a publicly trusted cert. Microsoft Store re-signs your `.msix` during ingestion using their own publisher identity. You still need a cert to satisfy the local CI's signtool step — use the Option A self-signed cert; the Store re-sign discards it.

This is the recommended path for v1.0. It is also the cheapest and fastest.

### Export to `.pfx` and base64-encode for GitHub Actions

GitHub Actions secrets must be strings, so the `.pfx` is base64-encoded:

```powershell
$pfxBytes  = [IO.File]::ReadAllBytes("nexcode.pfx")
$pfxBase64 = [Convert]::ToBase64String($pfxBytes)
$pfxBase64 | Set-Clipboard
Write-Host "Length: $($pfxBase64.Length) characters - paste into SIGNING_CERTIFICATE_PFX secret"
```

Then add to GitHub:

- `SIGNING_CERTIFICATE_PFX` ← the base64 blob from above
- `SIGNING_CERTIFICATE_PASSWORD` ← the plaintext password used in `Export-PfxCertificate`

See [§6](#6-github-actions-secrets) for the full secret list.

---

## 5. Microsoft Partner Center & IAP add-ons

This is the longest manual section because it has the most one-time portal clicks. Allow ~45 minutes the first time.

### Step 1 — Enroll

1. Go to <https://partner.microsoft.com/dashboard>.
2. Click **Sign up** → choose **Individual** ($19 USD one-time fee) or **Company** ($99 USD, requires DUNS verification, 3–7 business days). For a v1.0 ship, **Individual** is sufficient and fastest.
3. Complete the tax and payment forms.

### Step 2 — Reserve the app name

1. In Partner Center → **Apps and games** → **New product** → **MSIX or PWA app**.
2. Name: `NexCode`.
3. Click **Reserve product name**.
4. On the resulting **Product identity** page, copy:
   - **Package/Identity/Name** (the 12-char Store ID, looks like `1234.NexCode_abcdef123456`)
   - **Package/Identity/Publisher** (e.g., `CN=ABCDEFGH-1234-1234-1234-ABCDEFGHIJKL`)
   - **Package/Identity/PublisherDisplayName**

You'll paste these into the manifest in Step 4 below.

### Step 3 — Create the six subscription add-ons

NexCode v1.0 ships six paid tiers per spec §3.2.1, all created as **Subscription** products (per [AD-0001](architecture-decisions.md#ad-0001--microsoft-store-products-are-treated-as-subscriptions)).

For each row in the table, in Partner Center → your app → **Add-ons** → **Create a new add-on**:

| Product ID | Display name | Subscription period | Free trial | Suggested USD |
|---|---|---|---|---|
| `nexcode_pro_monthly` | NexCode Pro (Monthly) | 1 month | 7 days | $0.99 |
| `nexcode_pro_annual` | NexCode Pro (Annual) | 1 year | 7 days | $9.99 |
| `nexcode_team_monthly` | NexCode Team (Monthly) | 1 month | none | $1.49 |
| `nexcode_team_annual` | NexCode Team (Annual) | 1 year | none | $14.99 |
| `nexcode_enterprise_monthly` | NexCode Enterprise (Monthly per user) | 1 month | none | $1.99 |
| `nexcode_enterprise_annual` | NexCode Enterprise (Annual per user) | 1 year | none | $19.99 |

**Note on the Enterprise tier:** the display name says "per user" because that's how the seat math works — the Enterprise license auth flow validates one user, and you scale by buying multiple subscriptions in Partner Center (an admin distributes seats inside the org). The Store doesn't enforce seat counts, so the per-user wording sets expectations rather than billing logic.

For each add-on:

1. **Product ID** must match the table verbatim — the helper's `EntitlementService` resolves entitlements by this string, and a typo silently blocks the upgrade flow.
2. **Product type** = Subscription.
3. **Subscription period** = as in the table.
4. **Free trial** = as in the table.
5. **Properties** → set the visibility to **Public**.
6. **Pricing and availability** → choose **Free trial: 7 days** for the Pro tiers, and set the price in **Base price**. Save and submit.

### Step 4 — Privacy + Support URLs

In Partner Center → your app → **Properties** → **Privacy policy URL** and **Support contact info**:

- **Privacy policy URL** → `https://<your-org>.github.io/nexcode/privacy-policy/`
- **Support contact** → `https://<your-org>.github.io/nexcode/support/`

Both pages get auto-deployed by GitHub Pages — see [§8](#8-github-pages). They must return HTTP 200 with content (Microsoft Store certification fails listings that point at 404s).

### Step 5 — Update the app manifest to match

Open `src/NexCode.Gui/Package.appxmanifest` and update the `<Identity>` element:

```xml
<Identity
  Name="1234.NexCode_abcdef123456"
  Publisher="CN=ABCDEFGH-1234-1234-1234-ABCDEFGHIJKL"
  Version="1.0.0.0" />
```

Replace `Name` and `Publisher` with the values you copied in Step 2. The `Version` follows the four-part `Major.Minor.Build.Revision` schema; the fourth part **must be 0** for Store submissions (the Store reserves the revision component).

Also update `<Properties>`:

```xml
<Properties>
  <DisplayName>NexCode</DisplayName>
  <PublisherDisplayName>Your Display Name</PublisherDisplayName>
  <Logo>Assets\StoreLogo.png</Logo>
</Properties>
```

Match `PublisherDisplayName` exactly to the Partner Center field, including capitalization and trailing whitespace.

---

## 6. GitHub Actions secrets

Every secret consumed by the workflows under `.github/workflows/`. Add them at **Repo Settings → Secrets and variables → Actions → New repository secret**.

| Secret name | Required for | Source |
|---|---|---|
| `SIGNING_CERTIFICATE_PFX` | `release.yml` | Base64-encoded `.pfx` from [§4](#4-code-signing-certificate) |
| `SIGNING_CERTIFICATE_PASSWORD` | `release.yml` | Plaintext password for the `.pfx` |
| `NEXCODE_AAD_CLIENT_ID` | `ci.yml`, `release.yml` | App ID from [§3](#3-azure-ad-application-for-msal-sso) |
| `NEXCODE_AAD_TENANT_ID` | `ci.yml`, `release.yml` | Tenant ID from [§3](#3-azure-ad-application-for-msal-sso); use `common` for multi-tenant + MSA |
| `NEXCODE_TELEMETRY_ENDPOINT` | `release.yml` (optional) | HTTPS URL of your telemetry receiver — leave empty to disable |
| `NEXCODE_MARKETPLACE_ENDPOINT` | `release.yml` (optional) | HTTPS URL of your marketplace API — leave empty to use the offline fixture catalog |
| `STORE_PARTNER_CENTER_TENANT_ID` | `submit-to-store.yml` (optional) | Azure AD tenant for the Partner Center API app — see [§7](#7-partner-center-api-access-for-automated-store-submission-optional) |
| `STORE_PARTNER_CENTER_CLIENT_ID` | `submit-to-store.yml` (optional) | Client ID of the Partner Center API app |
| `STORE_PARTNER_CENTER_CLIENT_SECRET` | `submit-to-store.yml` (optional) | Client secret of the Partner Center API app |

### How to add (UI path)

1. Go to your repo on GitHub.
2. Click **Settings** (top tab).
3. Left sidebar → **Secrets and variables** → **Actions**.
4. Click **New repository secret**.
5. Name = exactly the value in the table above (case-sensitive).
6. Value = paste, no quotes, no trailing newline.
7. Click **Add secret**.

Repeat for each row. Secrets are write-only after creation; if you mistype a value, delete and re-add.

### Verifying

After adding all required secrets, push a no-op commit to `main`:

```bash
git commit --allow-empty -m "ci: trigger workflow secret check"
git push origin main
```

Watch **Actions** tab → the next run of `ci.yml` should succeed. If a secret is missing, the job fails with `Error: Required secret 'XXX' is not set`.

---

## 7. Partner Center API access for automated Store submission (optional)

Skip this section if you're submitting manually through the Partner Center web UI (which is fine for v1.0). Come back here once you have a release cadence faster than monthly.

### Steps

1. Go to <https://portal.azure.com> → **Microsoft Entra ID** → **App registrations** → **New registration**.
2. Name: `NexCode Partner Center API` (separate from the MSAL SSO app in [§3](#3-azure-ad-application-for-msal-sso) — never reuse the same app for both flows).
3. **Supported account types** = **Accounts in this organizational directory only** (single-tenant).
4. **Redirect URI** = leave blank (no interactive sign-in).
5. After creation, go to **Certificates & secrets** → **New client secret** → expiry = **24 months** → copy the **Value** field immediately. You will not see it again.
6. Capture three values:
   - Tenant ID → `STORE_PARTNER_CENTER_TENANT_ID`
   - Application (client) ID → `STORE_PARTNER_CENTER_CLIENT_ID`
   - Client secret value → `STORE_PARTNER_CENTER_CLIENT_SECRET`
7. Add all three to GitHub Actions secrets per [§6](#6-github-actions-secrets).
8. In Partner Center → **Account settings** → **User management** → **Azure AD applications** → **Add Azure AD applications** → search by name → assign **Manager** role.

The `submit-to-store.yml` workflow will now use these credentials to call the Partner Center Submission API automatically on tag push.

---

## 8. GitHub Pages

The privacy policy and support pages live under `docs/` and are deployed by `pages.yml` on every push to `main` that touches `docs/**`.

### One-time setup

1. Go to your repo on GitHub.
2. **Settings** → **Pages** (left sidebar).
3. **Source** → select **GitHub Actions** (not the legacy "Deploy from branch" option).
4. Save.

### Verify

After your next push to `main`:

```bash
curl -I https://<your-org>.github.io/<repo>/privacy-policy/
# expected: HTTP/2 200
```

If it 404s, check the **Actions** tab for the `pages.yml` run and look for build errors. The most common cause is `_config.yml` syntax errors; validate with:

```bash
yamllint docs/_config.yml
```

### Update Partner Center

Once Pages serves 200s, go back to Partner Center → your app → **Properties** and paste:

- **Privacy policy URL**: `https://<your-org>.github.io/<repo>/privacy-policy/`
- **Support contact info**: `https://<your-org>.github.io/<repo>/support/`

Save. Microsoft Store certification will reject the submission if these URLs return non-200 at any point during cert review (which can run for 1–3 business days), so don't change repo visibility from public to private during a submission.

---

## 9. Bundling assets (Monaco, Node.js, WebView2 fixed runtime, Cascadia Code)

Today the Monaco editor and xterm.js terminal load from the public CDN as a Slice 0015 dev-loose-layout placeholder. To fully self-contain per spec §37, you bundle four binary asset trees into the MSIX. Total uncompressed footprint: ~250 MB; compressed in `.msix`: ~85 MB.

The skeleton is in `scripts/build/Bundle-Assets.ps1`. Today it writes README placeholders to each asset folder; you extend it with the real download URLs (versions ratchet upward over time, so we don't hard-code them in repo).

### Step 1 — Run the helper

```powershell
pwsh scripts/build/Bundle-Assets.ps1 -Download
```

The script creates four directories under `src/NexCode.Gui/Assets/` and writes a placeholder README in each. Replace those READMEs with the real assets per the next four steps.

### Step 2 — Monaco Editor

```powershell
$ver = "0.46.0"
$url = "https://registry.npmjs.org/monaco-editor/-/monaco-editor-$ver.tgz"
Invoke-WebRequest -Uri $url -OutFile "$env:TEMP\monaco.tgz"
tar -xzf "$env:TEMP\monaco.tgz" -C "$env:TEMP\monaco"
Copy-Item -Recurse -Force `
  "$env:TEMP\monaco\package\min\*" `
  "src\NexCode.Gui\Assets\Monaco\"
```

Resulting tree:

```
src/NexCode.Gui/Assets/Monaco/
  vs/
    loader.js
    editor/editor.main.js
    editor/editor.main.css
    ... (~500 files)
```

### Step 3 — Node.js LTS (for bundled LSP host)

```powershell
$ver = "20.18.1"
$url = "https://nodejs.org/dist/v$ver/node-v$ver-win-x64.zip"
Invoke-WebRequest -Uri $url -OutFile "$env:TEMP\node.zip"
Expand-Archive -Path "$env:TEMP\node.zip" -DestinationPath "$env:TEMP\node"
Copy-Item -Recurse -Force `
  "$env:TEMP\node\node-v$ver-win-x64\*" `
  "src\NexCode.Gui\Assets\NodeRuntime\"
```

Resulting tree:

```
src/NexCode.Gui/Assets/NodeRuntime/
  node.exe
  npm.cmd
  npx.cmd
  node_modules/...
```

### Step 4 — WebView2 Fixed Version Runtime

The "Fixed Version" runtime guarantees a known WebView2 behavior across Win10/11 versions. Download from <https://developer.microsoft.com/en-us/microsoft-edge/webview2/> → **Fixed Version Runtime** → x64 → **CAB** download:

```powershell
# Replace the version with the one you actually downloaded.
$ver = "129.0.2792.79"
expand "$env:USERPROFILE\Downloads\Microsoft.WebView2.FixedVersionRuntime.$ver.x64.cab" -F:* `
  "src\NexCode.Gui\Assets\WebView2Runtime\"
```

Resulting tree:

```
src/NexCode.Gui/Assets/WebView2Runtime/
  msedgewebview2.exe
  msedge.dll
  ... (~250 files)
```

Wire it into the GUI by setting `CoreWebView2Environment.CreateAsync(browserExecutableFolder: ...)` to the runtime path. The relevant call site is in `src/NexCode.Gui/Controls/MonacoEditorControl.xaml.cs`.

### Step 5 — Cascadia Code

```powershell
$ver = "2407.24"
$url = "https://github.com/microsoft/cascadia-code/releases/download/v$ver/CascadiaCode-$ver.zip"
Invoke-WebRequest -Uri $url -OutFile "$env:TEMP\cascadia.zip"
Expand-Archive -Path "$env:TEMP\cascadia.zip" -DestinationPath "$env:TEMP\cascadia"
Copy-Item -Force `
  "$env:TEMP\cascadia\ttf\CascadiaCode.ttf", `
  "$env:TEMP\cascadia\otf\static\CascadiaCode-Regular.otf" `
  "src\NexCode.Gui\Assets\Fonts\Cascadia\"
```

### Step 6 — First-run extraction

On first launch, `BundledAssetExtractor` extracts the Node.js tree to `%LOCALAPPDATA%\NexCode\NodeRuntime\` and lazy-installs the LSP servers under `%LOCALAPPDATA%\NexCode\LspServers\<lang>\`. The Cascadia Code font is loaded directly from the MSIX package via `ms-appx:///Assets/Fonts/Cascadia/CascadiaCode.ttf` and registered with the WinUI font fallback chain.

### Step 7 — Switch Monaco / terminal HTML to local file paths

Open `src/NexCode.Gui/Assets/Monaco/index.html`:

```html
<!-- Before (CDN placeholder): -->
<script src="https://cdn.jsdelivr.net/npm/monaco-editor@0.46.0/min/vs/loader.js"></script>

<!-- After (bundled): -->
<script src="vs/loader.js"></script>
```

Same change in `src/NexCode.Gui/Assets/Terminal/index.html` for `xterm.js` and `xterm.css` references.

### Step 8 — Verify package size

```powershell
dotnet publish src/NexCode.Gui -c Release -r win-x64
$msix = Get-ChildItem "src\NexCode.Gui\bin\Release\net9.0-windows10.0.26100.0\AppPackages\*\*.msix" | Select -First 1
"{0:N2} MB" -f ($msix.Length / 1MB)
```

Expected: 80–95 MB. If it's >150 MB, you accidentally bundled the WebView2 Evergreen installer instead of the runtime payload — re-extract the CAB.

---

## 10. LSP servers & MCP servers (optional)

### TypeScript LSP

The bundled Node.js from [§9](#9-bundling-assets-monaco-nodejs-webview2-fixed-runtime-cascadia-code) ships `npx`. The helper invokes:

```bash
npx --yes typescript-language-server --stdio
```

`--yes` auto-accepts the npm install prompt the first time. The package lands under `%LOCALAPPDATA%\NexCode\NodeRuntime\npm-cache\` and is reused on subsequent runs.

### Python LSP

Ship a Python venv-less `pylsp` install:

```powershell
$venv = "$env:LOCALAPPDATA\NexCode\LspServers\python"
python -m venv $venv
& "$venv\Scripts\pip.exe" install python-lsp-server pylsp-mypy python-lsp-ruff
```

The helper resolves `pylsp` via `Where-Object` over `$env:LOCALAPPDATA\NexCode\LspServers\python\Scripts\pylsp.exe` first, then falls back to system PATH.

### Other languages

Adding a new language is two files:

1. `src/NexCode.Cli/Lsp/<Lang>LanguageClient.cs` — derives from `LanguageClientBase`, returns the binary path.
2. `src/NexCode.Cli/Lsp/LanguageClientFactory.cs` — register the new client by file extension.

The framework handles initialization, document sync, hover, completion, definition, references, and diagnostics over the standard LSP wire protocol.

### MCP servers

The helper supports both stdio and Streamable HTTP MCP transports out of the box. To add a server:

1. Open the GUI → **Settings** → **MCP Servers** → **Add server**.
2. Pick transport (stdio / HTTP).
3. Paste the command line or URL.
4. Save and toggle on.

The fixture catalog at `src/NexCode.Service/Marketplace/Fixtures/catalog.json` ships six community servers (`filesystem`, `github`, `slack`, `postgres`, `puppeteer`, `memory`) — they appear in **Settings → Marketplace** without configuration. Click **Install** to wire them into the active session.

---

## 11. Telemetry receiver (optional, for collecting opt-in training data)

NexCode emits anonymous, opt-in telemetry events. The shape is `{ kind, payload, timestamp }` records, batched into a JSON array, POSTed every six hours when online.

### Build a receiver

A minimal receiver in any HTTP framework:

```javascript
// Node.js / Express example
app.post("/telemetry", express.json({ limit: "10mb" }), async (req, res) => {
  const events = req.body; // array of { kind, payload, timestamp }
  for (const ev of events) {
    await db.telemetry.insert({
      kind: ev.kind,
      payload: ev.payload,           // already JSON
      ts: new Date(ev.timestamp),
      received_at: new Date()
    });
  }
  res.status(204).end();
});
```

The endpoint must:

- Accept HTTPS (the helper refuses `http://`).
- Return 2xx on success. Anything else triggers retry with exponential backoff (max 6 attempts, then drop to local queue).
- Tolerate ~50 KB request bodies (events batch up to 1000 records).

### Wire into the build

Set `NEXCODE_TELEMETRY_ENDPOINT` in your build pipeline:

```yaml
# .github/workflows/release.yml (excerpt)
env:
  NEXCODE_TELEMETRY_ENDPOINT: ${{ secrets.NEXCODE_TELEMETRY_ENDPOINT }}
```

Or for local dev:

```powershell
$env:NEXCODE_TELEMETRY_ENDPOINT = "https://telemetry.example.com/telemetry"
dotnet run --project src/NexCode.Service
```

### Verify

1. Launch the GUI.
2. **Settings → Privacy → Send anonymous usage telemetry** → toggle **On**.
3. Run a session, send a few messages.
4. Wait up to 6 hours, or click **Settings → Privacy → Drain queue now** to flush immediately.
5. Check your DB — events with `kind` values like `session.started`, `tool.invoked`, `provider.streamed` should appear.

If nothing arrives, check `%LOCALAPPDATA%\NexCode\logs\helper-*.log` for `TelemetryDispatcher: HTTP 4xx` or `network unreachable` lines.

---

## 12. Local development workflow

### Clone, restore, build

```powershell
git clone https://github.com/<org>/<repo> NexCode
cd NexCode
dotnet restore NexCode.slnx
dotnet build NexCode.slnx
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

If you hit XamlCompiler errors here, see [§2](#2-resolving-the-xamlcompilerexe-pass2-build-break).

### Launch the helper

```powershell
dotnet run --project src/NexCode.Service
```

The helper starts a JSON-RPC server on a named pipe (`\\.\pipe\nexcode.helper`), opens the SQLCipher database at `%LOCALAPPDATA%\NexCode\nexcode.db`, and idles waiting for the GUI or CLI to connect. Logs stream to stdout and to `%LOCALAPPDATA%\NexCode\logs\helper-<date>.log`.

### Launch the GUI as a packaged loose-layout

The GUI is single-project MSIX, so F5 in Visual Studio packages and deploys it. From the command line, the equivalent is:

```powershell
$layout = ".\src\NexCode.Gui\bin\Debug\net9.0-windows10.0.26100.0\AppX"
Add-AppxPackage -Register "$layout\AppxManifest.xml"

# Find the package family name to launch:
$pkg = Get-AppxPackage NexCode | Select-Object -First 1
explorer.exe "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
```

The `!App` suffix is the application ID inside the package; it matches `<Application Id="App">` in `Package.appxmanifest`.

### Tail logs

```powershell
Get-Content "$env:LOCALAPPDATA\NexCode\logs\helper-*.log" -Tail 100 -Wait
```

### Run tests

```powershell
dotnet test NexCode.slnx
```

Expected: `Passed: 219, Failed: 0, Skipped: 0`.

### Visual Studio multi-project startup (debugging the full stack)

NexCode runs as **two cooperating processes**: the helper (`NexCode.Service`) and the GUI (`NexCode.Gui`). To F5-debug both at once:

1. In Solution Explorer, right-click the **solution** → **Configure Startup Projects…**
2. Pick **Multiple startup projects**.
3. Set the **Action** column:

| Project | Action |
|---|---|
| `NexCode.Service` | **Start** |
| `NexCode.Gui` | **Start** |
| All others (`NexCode.Cli`, `NexCode.Data`, `NexCode.Remote`, `NexCode.Shared`, `NexCode.Marketplace.Sdk`, every test project) | **None** |

4. Click **Apply** then **OK**. The startup config is written to `.vs/<sln>/v17/.suo`, which is in `.gitignore`, so each developer keeps their own.
5. Press **F5**. Visual Studio launches the helper first (it owns the named pipe), then the GUI which connects to it.

If the GUI starts before the helper is fully listening, you'll see the *"Helper not running"* banner for ~1 second; the GUI auto-reconnects on the next IPC poll (default 1s).

### CLI-only smoke test (no GUI)

```powershell
# Terminal 1 — helper
dotnet run --project src/NexCode.Service

# Terminal 2 — CLI
dotnet run --project src/NexCode.Cli -- service ping
dotnet run --project src/NexCode.Cli -- account status
dotnet run --project src/NexCode.Cli -- session create --project (Get-Location).Path
```

---

## 13. Provider configuration (first-run UX)

After authentication completes ([§3](#3-azure-ad-application-for-msal-sso) wired up), you must add at least one AI provider before sessions stream.

### Steps

1. Launch the GUI. The first-run banner reads "No AI provider is configured."
2. Click the banner, or navigate to **Settings → Providers**.
3. Click **Add provider**.
4. Pick one (Anthropic and OpenAI are recommended for v1.0):

| Provider | API key URL | Notes |
|---|---|---|
| Anthropic | <https://console.anthropic.com/account/keys> | Recommended default; Claude 4.6 / 4.7 streaming |
| OpenAI | <https://platform.openai.com/api-keys> | GPT-5.2 / o-series; supports tool calls |
| Gemini | <https://aistudio.google.com/apikey> | Free tier sufficient for evaluation |
| Bedrock | AWS IAM access key + secret | SigV4 signing partial — see [§18](#18-known-deferred-work-not-blocking-v10-ship) |
| Azure OpenAI | Azure portal | Endpoint + deployment ID + key |
| Groq | <https://console.groq.com/keys> | Free tier; very fast Llama / Mixtral |
| OpenRouter | <https://openrouter.ai/keys> | Access ~100 models behind one key |
| Ollama | (no key — local URL) | Default `http://localhost:11434` |
| LM Studio | (no key — local URL) | Default `http://localhost:1234/v1` |
| Custom OpenAI-compat | (varies) | Paste any `/v1/chat/completions`-compatible URL |

5. Paste the API key. The helper encrypts it with SQLCipher (master key sealed by DPAPI, see Slice 0011) before persistence.
6. Click **Save**, then **Set as default**.
7. The next session will stream against this provider.

You can wire multiple providers and switch per session via the model dropdown in the session header.

---

## 14. Sub-agent + sandbox + permission walkthrough

Smoke-test the agent loop end-to-end before submitting to the Store.

### Sub-agents

1. Open a Pro+ session (you can mock a Pro license locally by setting `NEXCODE_DEV_TIER=Pro` before launching the helper).
2. Type: `spawn three sub-agents to refactor the data layer`.
3. The parent agent invokes the `spawn_subagent` tool three times. Each call fires up a `NexCode.Cli` child process; you'll see three subprocess entries in Task Manager under `dotnet.exe` for `NexCode.Cli`.
4. Tier gating per spec §19:

| Tier | Max concurrent sub-agents |
|---|---|
| Free | 0 |
| Pro | 3 |
| Team | 10 |
| Enterprise | unlimited |

Attempting to exceed the cap returns a structured `tier_limit_exceeded` error inline in the message stream.

### Sandbox

The session header bar has a sandbox toggle. When on:

- A Win32 Job Object wraps every tool-spawned subprocess.
- Memory cap: 2 GB per child.
- CPU cap: 50% per child.
- Clipboard access: blocked.
- Network access: tool-layer allow-list only (see [§18](#18-known-deferred-work-not-blocking-v10-ship) for OS-level WFP integration roadmap).

Toggle off temporarily for tools that need full system access (e.g., Docker, package managers).

### Permission gate

Every tool has a permission requirement: **Default**, **Warned**, or **Full**. When a tool's requirement exceeds the session's granted scope, an inline permission card appears:

```
[!] Tool 'edit_file' requires Warned permission.
    Allow once   |   Allow for session   |   Deny
```

The user's choice is persisted to the session memory under the `permissions` key.

---

## 15. Building the standalone Inno Setup installer

Use this if you want a non-Store distribution path (enterprise rollouts, customer test builds, tradeshow demos).

### Steps

1. Install Inno Setup 6.x from <https://jrsoftware.org/isdl.php>.
2. Build the self-contained publish:

```powershell
pwsh scripts/build/Publish-SelfContained.ps1
```

This produces `src/NexCode.Gui/bin/Release/net9.0-windows10.0.26100.0/win-x64/publish/` with all .NET 9 dependencies trimmed to ~120 MB.

3. Compile the installer:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" scripts\installer\NexCode-Setup.iss
```

4. Output: `installer-output\NexCode-Setup-X.Y.Z.exe`.

### Sign the installer

```powershell
$sig = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
& $sig sign `
  /fd SHA256 `
  /tr "http://timestamp.digicert.com" `
  /td SHA256 `
  /f "nexcode.pfx" `
  /p "ChangeMe!" `
  "installer-output\NexCode-Setup-1.0.0.exe"
```

### Verify

```powershell
Get-AuthenticodeSignature "installer-output\NexCode-Setup-1.0.0.exe"
# Status should be: Valid
```

If you see `UnknownError` or `NotSigned`, your `.pfx` is invalid; rebuild from [§4](#4-code-signing-certificate).

---

## 16. First Microsoft Store submission

This is the moment of truth. Allow ~2 hours for the manual submission, then 1–3 business days for certification.

### Step 1 — Tag a release

```bash
git tag v1.0.0
git push origin v1.0.0
```

The tag triggers `release.yml`, which:

1. Builds `Release|x64` for `NexCode.slnx`.
2. Generates the `.msix` via `dotnet publish`.
3. Signs it with `SIGNING_CERTIFICATE_PFX` from [§6](#6-github-actions-secrets).
4. Computes SHA-256 checksums.
5. Creates a GitHub Release at `https://github.com/<org>/<repo>/releases/tag/v1.0.0` with `.msix`, `.exe` (Inno Setup installer), and `.txt` checksum file as artifacts.

Watch the **Actions** tab. The full pipeline runs in ~12 minutes.

### Step 2 — Download the signed `.msix`

Once the GitHub Release is created, download the `.msix` artifact to your local machine.

Verify the signature:

```powershell
Get-AuthenticodeSignature ".\NexCode-1.0.0.msix"
# Status: Valid
```

### Step 3 — Submit in Partner Center

1. Go to <https://partner.microsoft.com/dashboard> → your app → **New submission**.
2. **Pricing and availability**:
   - **Markets**: All markets (or your subset).
   - **Pricing**: **Free** (the in-app subscriptions handle monetization).
   - **Visibility**: **Public**.
3. **Properties**:
   - **Category**: Developer tools.
   - **Privacy policy URL**: from [§8](#8-github-pages).
   - **Support contact info**: from [§8](#8-github-pages).
4. **Age ratings**: complete the IARC questionnaire. NexCode is a developer tool with no user-generated public content, so all checkboxes should be **No**.
5. **Packages**: drag-and-drop the signed `.msix` from Step 2.
6. **Store listings** (per locale, English at minimum):
   - **Description**: 250–400 words describing NexCode v1.0 features.
   - **Screenshots**: at least 4 at 1366×768 or 1920×1080 (PNG).
   - **Store logo**: 300×300 PNG (already in `src/NexCode.Gui/Assets/StoreLogo.png` if you sized it right).
   - **What's new**: changelog blurb pulled from `docs/changelog.md`.
7. **Submission options**: leave defaults.
8. Click **Submit to the Store**.

### Step 4 — Wait for certification

Microsoft Store certification typically takes 1–3 business days. You'll get email notifications at each stage:

- **Preprocessing** (~1 hr) — automated package validation.
- **Security tests** (~6 hr) — malware scan + manifest validation.
- **Technical compliance** (~12 hr) — install/launch/uninstall on test VMs.
- **Content compliance** (~24 hr) — human review of listing.
- **Release** (~6 hr after approval) — propagation to Store servers.

If certification fails, the email tells you exactly why with line-by-line callouts. The most common rejection reasons:

| Reason | Fix |
|---|---|
| Privacy policy URL returns 404 | Verify [§8](#8-github-pages) end-to-end. |
| Manifest identity mismatch | Re-run [§5 Step 5](#5-microsoft-partner-center--iap-add-ons). |
| App crashes on launch in test VM | Check that you bundled WebView2 runtime per [§9](#9-bundling-assets-monaco-nodejs-webview2-fixed-runtime-cascadia-code). |
| Add-on product IDs don't match | Re-verify the table in [§5 Step 3](#5-microsoft-partner-center--iap-add-ons). |

### Step 5 — Live

Once approved, the app appears at `https://www.microsoft.com/store/apps/<your-store-id>`. Share the URL.

---

## 17. Post-launch operational checklist

Run through this list weekly for the first month, then monthly.

### Crash reports

Watson telemetry forwards to Partner Center automatically — no setup. Check:

- Partner Center → your app → **Health** → **Failures**.
- Sort by **Hits** descending. Anything over 10 hits in 24 hours is a hot incident.

### Telemetry queue

If you wired [§11](#11-telemetry-receiver-optional-for-collecting-opt-in-training-data):

- Open the GUI → **Settings → Privacy → Queue size** — should drain to 0 within 6 hours of network availability.
- A persistently growing queue means your receiver is rejecting requests — check your receiver logs.

### Subscription license refresh

`SubscriptionRefreshBackgroundService` runs every 24 hours and re-validates Store entitlements via the WindowsStoreContext. If a user's subscription lapses:

- Helper logs `Subscription expired for product=nexcode_pro_monthly`.
- The GUI shows a "Subscription expired — renew?" banner.
- Pro features gate down to Free immediately.

### Background Service Mode

Users can register the helper to autostart at login:

**Settings → General → Start as Background Service**

This adds a `Run` registry entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The tray icon hosts the helper; the GUI launches on demand from the global hotkey (default `Ctrl+Alt+Space`).

---

## 18. Known deferred work (not blocking v1.0 ship)

These items have skeletons in the codebase but are not wired end-to-end. They don't block Microsoft Store certification.

| Item | Status | Tracked in |
|---|---|---|
| Bedrock SigV4 signing + binary event-stream framing | Skeleton only — synchronous fallback works for Anthropic-on-Bedrock | `src/NexCode.Service/Providers/Bedrock/` |
| MCP OAuth interactive consent UI | PKCE primitives wired in `McpOAuthClient`; no UI surface | `src/NexCode.Service/Mcp/Auth/` |
| WFP-based OS-level network sandbox restriction | Tool-layer allow-list only today; OS-level filtering deferred | `src/NexCode.Service/Sandbox/` |
| Marketplace backend service | Client uses fixture catalog at `Marketplace/Fixtures/catalog.json` | `src/NexCode.Marketplace.Sdk/` |

Roadmap target: post-v1.0.1 (next minor release).

---

## 19. Troubleshooting

### "Helper not running" banner

The GUI couldn't connect to the named pipe `\\.\pipe\nexcode.helper`.

**Fix**:

```powershell
# Check if the helper is alive:
Get-Process | Where-Object { $_.ProcessName -eq "NexCode.Service" }

# If not, start it:
dotnet run --project src/NexCode.Service

# Or, if you registered Background Service Mode in Settings → General,
# restart the system tray daemon by right-clicking the tray icon → Restart helper.
```

### "No AI provider is configured"

You haven't completed [§13](#13-provider-configuration-first-run-ux). Settings → Providers → add a provider with a real API key.

### Visual Studio: "Shared Web Components did not load correctly"

This is a VS 2022 / VS 2026 component-cache or workload mismatch. It does **not** indicate a problem with the NexCode codebase — the .slnx still loads, but pages that depend on Shared Web Components (the Razor / web tooling stack that some MSIX previews use) fail to render their designer surface. Three fixes in escalating cost:

1. **Update the workloads first.** Open **Visual Studio Installer** → **Modify** for your VS instance → **Workloads** tab → ensure all three of these are checked, then **Modify**:
   - **.NET desktop development**
   - **Universal Windows Platform development**
   - **ASP.NET and web development** (this is the one that ships Shared Web Components)
   - Under **Individual components**, also tick **Windows App SDK C# Templates** if it isn't already.
2. **Reset VS user data.** Close VS, then in PowerShell:
   ```powershell
   & "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe" /resetuserdata
   # (or wherever your VS install lives — Community / Professional / 2026 Preview)
   ```
   Reopen VS. This wipes per-user caches without touching the install.
3. **Repair the install.** **Visual Studio Installer** → **More** → **Repair**. Takes 10–15 min. Use this if Step 2 didn't help.

You don't need to read the `ActivityLog.xml` — the message you're seeing covers the full diagnosis. If after Step 3 it still fails, file an issue at <https://developercommunity.visualstudio.com/>.

### CI / CodeQL fails with NETSDK1094 ("a valid runtime package was not found")

GitHub Actions runners come with `dotnet 10.0.x` preinstalled, which our `global.json` (see [§2](#2-resolving-the-xamlcompilerexe-pass2-build-break)) pins down to 9.0.305. That alone fixes the XAML compiler issue locally, but two CI workflows (`build` step in `ci.yml` and `codeql.yml`) trigger NETSDK1094 because **PublishReadyToRun** was set to `True` for non-Debug configurations and R2R requires a runtime package matching the target RID at the moment the publish target runs.

**Fix already applied** in `src/NexCode.Gui/NexCode.Gui.csproj`:

```xml
<PropertyGroup>
  <PublishReadyToRun>False</PublishReadyToRun>
  <PublishTrimmed>False</PublishTrimmed>
</PropertyGroup>
```

R2R is a startup-perf optimization (~5% on cold start). If you want it back later, re-enable per-publish via:

```bash
dotnet publish src/NexCode.Gui -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true
```

— always with an explicit `-r` flag so the SDK can find the matching runtime pack. Push a tag and the next CI run will go green.

### WinAppSDK XamlCompiler 0-byte g.cs

The .NET 10 SDK regression. See [§2](#2-resolving-the-xamlcompilerexe-pass2-build-break) for the `global.json` pin.

### LSP servers report `lsp_unavailable`

The bundled Node.js isn't extracted, or `pylsp` isn't installed.

**Fix**:

```powershell
# Re-run the asset bundler:
pwsh scripts/build/Bundle-Assets.ps1 -Download

# Or install Python LSP manually:
$venv = "$env:LOCALAPPDATA\NexCode\LspServers\python"
python -m venv $venv
& "$venv\Scripts\pip.exe" install python-lsp-server
```

### MSIX won't install — identity mismatch

`Add-AppxPackage` returns: `The signature is invalid` or `The publisher of an installed package does not match the publisher of the package being installed`.

**Fix**: re-run [§5 Step 5](#5-microsoft-partner-center--iap-add-ons) — the `<Identity>` element in `Package.appxmanifest` must match Partner Center's recorded identity exactly. Then rebuild and re-sign.

### Store certification rejects with "missing capability declaration"

The MSIX manifest is missing a `<rescap:Capability>` for a privileged operation.

**Fix**: open `src/NexCode.Gui/Package.appxmanifest`, find `<Capabilities>`, and add the missing capability. The most commonly missed:

- `runFullTrust` — required because NexCode invokes Win32 APIs (Job Objects, ConPTY).
- `internetClient` — required for streaming LLM responses.
- `internetClientServer` — required for the helper's named-pipe IPC fallback over loopback.

### Anything else

Open `%LOCALAPPDATA%\NexCode\logs\helper-*.log` and grep for `ERR `:

```powershell
Get-Content "$env:LOCALAPPDATA\NexCode\logs\helper-*.log" `
  | Select-String "ERR " -Context 2,5 `
  | Select-Object -First 50
```

The log is structured (Serilog JSON), so you can also pipe through `jq` for richer queries.

---

## See also

- [docs/architecture-decisions.md](architecture-decisions.md) — design decisions resolving spec ambiguities.
- [docs/implementation-journal.md](implementation-journal.md) — slice-by-slice implementation log (Slices 0001–0018).
- [docs/spec-index.md](spec-index.md) — area → spec section → ownership map.
- [docs/changelog.md](changelog.md) — user-facing release notes.
- [plan.md](../plan.md) — the authoritative spec (sections 1–41).
