# Build Scripts

This folder contains the first Section 27 build assets for NexCode.

- `Generate-OssLicenses.ps1` refreshes `docs/oss-licenses.md` from the repository's current direct package references.
- `Sign-Msix.ps1` signs generated `.msix` or `.appx` files with the configured release certificate.
- `Install-SuperUserGrant.ps1` installs a privately issued signed superuser grant into the current user's DPAPI-protected local app data store.

Expected GitHub Actions secrets from Section 27.3:

- `SIGNING_CERTIFICATE_PFX`
- `SIGNING_CERTIFICATE_PASSWORD`
- `NEXCODE_AAD_CLIENT_ID`
- `NEXCODE_AAD_TENANT_ID`
- `NEXCODE_TELEMETRY_ENDPOINT`
- `NEXCODE_MARKETPLACE_ENDPOINT`
- `STORE_PARTNER_CENTER_TENANT_ID`
- `STORE_PARTNER_CENTER_CLIENT_ID`
