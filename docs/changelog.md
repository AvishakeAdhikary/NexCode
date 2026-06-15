# Changelog

## Unreleased

### Added
- **Working shell navigation.** The left nav rail (Search, History, Plans, Memories,
  Plugins, Automations, Settings) was previously dead — the view model raised navigation
  events that nothing handled, and Search/Memories/Plugins/Automations had no command at
  all. The shell now routes every nav destination through a single `NavigationRequested`
  event to the shell frame, so those existing pages are reachable. A back button in the
  title bar returns to the chat (which is cached so its state is preserved).

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
