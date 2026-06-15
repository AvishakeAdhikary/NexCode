# Changelog

## Unreleased

### Added
- **Working shell navigation.** The left nav rail (Search, History, Plans, Memories,
  Plugins, Automations, Settings) was previously dead — the view model raised navigation
  events that nothing handled, and Search/Memories/Plugins/Automations had no command at
  all. The shell now routes every nav destination through a single `NavigationRequested`
  event to the shell frame, so those existing pages are reachable. A back button in the
  title bar returns to the chat (which is cached so its state is preserved).
- **AI provider configuration (end-to-end).** Settings → Providers was a stub ("wire-up
  pending"). It now loads the configured providers from the helper, and Add/Save/Remove/
  Set-default persist through `provider.list/upsert/remove/set_default` over IPC (a Save
  button was added; API keys are sent to the helper, which stores them encrypted).
- **~50 IPC client methods.** `HelperControlClient` exposed only ~11 of the ~80 backend
  handlers, leaving most pages unable to reach existing functionality. Added client methods
  (reusing the existing `NexCode.Shared.Contracts`) for memory, history, plans/todos, git
  (status/diff/revert), modes, personalities, MCP, environments, telemetry, plugins,
  automations, sub-agents, editor, terminal, and remote/cloud.
- **Wired panels** (real IPC, no more placeholders): **Memories** (list/write/delete),
  **History** (list/search/archive/delete/export), **Search** (history.search),
  **Plans** (list/get/confirm/request-changes), **Git** (status/diff/revert).
- **Wired settings** (real IPC): **Modes**, **Personalities**, **MCP servers**,
  **Environments**, **Privacy/Telemetry**, **Plugins**, **Automations** — each loads on
  activation and persists CRUD through its existing handler. Local stub records that
  shadowed the real contracts were removed.
- **System accent color.** The app now follows the Windows accent (Light/Dark/HighContrast
  aware), with live updates on `UISettings.ColorValuesChanged`.
- **Microsoft sign-in config.** Azure AD client/tenant + `http://localhost` loopback redirect
  wired through `appsettings.Local.json` (gitignored); the `common` authority allows personal
  and any-org accounts.

### Fixed
- **Chat send/stop did nothing.** `SessionViewModel` raised `SendMessageRequested`/
  `StopRequested` that `MainWindow` never subscribed to, so the chat loop was dead
  end-to-end. The composer now dispatches `session.send_message` / `session.cancel`, and the
  streamed token/checkpoint events render back into the transcript.

### Fixed
- **Helper "unavailable" flicker.** The background helper's named-pipe server accepted
  connections serially, so while any handler ran (e.g. a Store-backed account snapshot)
  no pipe was listening; the GUI's overlapping calls (2s event poll + health + snapshot)
  then timed out intermittently. The accept loop now serves each connection concurrently
  and always keeps a listener ready, and the GUI client retries the connect with a longer
  timeout. Verified with a 30s pipe-load probe (0 connection failures).
- **Auth-gate overlay pulsing.** The sign-in overlay restarted its fade-in animation on
  every 2-second event poll while visible, making it flicker. The fade now plays only on
  the hidden→visible transition.

### Changed
- **Modern Fluent shell.** Switched the window to **Mica Alt** with an extended custom
  title bar, and made the shell chrome (nav rail, context panel, page background)
  transparent so the Mica material reads through instead of a flat opaque fill.

### Removed
- **All scheduled (cron) GitHub Actions.** Deleted the failing weekly `dependency-update`
  workflow (GitHub Actions cannot open PRs in this repo) and removed the weekly `schedule`
  trigger from CodeQL. No workflow in this repo uses `on: schedule` / `cron`.

## 0.2.0-account-foundation

- Added solution bootstrap with GUI, service, CLI, data, shared, and remote projects.
- Added named-pipe helper foundation and account/subscription state plumbing.
- Added CI workflow skeletons and initial legal-document scaffolding.
