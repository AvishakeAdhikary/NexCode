# NexCode — Agent Context

Primary specification: `plan.md`

Execution rules:
- The spec is the source of truth. Re-read the relevant sections before each implementation slice and after any failed verification.
- Record every slice in `docs/implementation-journal.md` before and after work.
- Keep cross-process contracts in `src/NexCode.Shared`.
- Keep durable storage concerns in `src/NexCode.Data`.
- Treat `NexCode.Service` as a user-mode helper/tray host, not a Windows Service.
- Treat Microsoft Store IAP as the only paid licensing path.
- Treat session-scoped memory as runtime state, not durable database state.
- Treat privileged superuser access as a sealed local grant validated by the helper. Do not introduce checked-in account identifiers or environment-variable activation paths.
- Use guarded checkpoint restore semantics; do not rely on unsafe blanket resets.
- Prefer the WinUI template/tooling path over hand-rolled project bootstrapping.

Current phase:
- Phase 0/1 bootstrap: control artifacts, environment validation, solution scaffolding, shared contracts, data foundation.

Current known environment notes:
- WinUI bootstrap was applied via the bundled `winui-app` skill config.
- `dotnet new winui` required installing `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` before becoming available.
