# NexCode Spec Index

This file maps implementation areas to the authoritative sections in `plan.md` and the planned code ownership.

## Foundation Map

| Area | Spec Sections | Planned Ownership |
|---|---|---|
| Product identity and component boundaries | 1.1, 1.2, 1.3 | `src/NexCode.Gui`, `src/NexCode.Service`, `src/NexCode.Cli`, `src/NexCode.Data`, `src/NexCode.Shared` |
| Process architecture and IPC | 2.1, 2.2, Appendix E | `src/NexCode.Service`, `src/NexCode.Shared`, `src/NexCode.Gui` |
| Database and encryption | 2.3, Appendix A | `src/NexCode.Data` |
| Authentication and subscription cache | 3.1, 3.2 | `src/NexCode.Service`, `src/NexCode.Gui`, `src/NexCode.Shared` |
| GUI shell and layout | 4.1, 4.2, 4.3, 4.4, 4.5 | `src/NexCode.Gui` |
| CLI engine and event schema | 5.1, 5.2, 5.3, 5.5 | `src/NexCode.Cli`, `src/NexCode.Shared` |
| Windows shell and terminal integration | 6.1, 6.2, 21 | `src/NexCode.Cli`, `src/NexCode.Gui` |
| Modes and personalities | 7, 8 | `src/NexCode.Cli`, `src/NexCode.Data`, `src/NexCode.Gui` |
| Memory | 9 | `src/NexCode.Cli`, `src/NexCode.Data`, `src/NexCode.Gui` |
| Plans and todos | 10, Appendix C | `src/NexCode.Cli`, `src/NexCode.Data`, `src/NexCode.Shared`, `src/NexCode.Gui` |
| Clarify module | 11, Appendix D | `src/NexCode.Cli`, `src/NexCode.Data`, `src/NexCode.Shared`, `src/NexCode.Gui` |
| Model switcher | 12 | `src/NexCode.Gui`, `src/NexCode.Shared` |
| Checkpoints and diff | 13 | `src/NexCode.Cli`, `src/NexCode.Gui`, `src/NexCode.Data` |
| Sandbox and permissions | 14, 26 | `src/NexCode.Cli`, `src/NexCode.Service`, `src/NexCode.Shared` |
| Built-in editor and LSP | 15 | `src/NexCode.Gui`, `src/NexCode.Cli` |
| Git manager | 16 | `src/NexCode.Cli`, `src/NexCode.Gui` |
| MCP and plugins | 17, 18, 22 | `src/NexCode.Cli`, `src/NexCode.Gui`, `src/NexCode.Marketplace.Sdk` |
| Sub-agents | 19 | `src/NexCode.Cli`, `src/NexCode.Service`, `src/NexCode.Shared` |
| Remote execution | 23 | `src/NexCode.Remote`, `src/NexCode.Shared`, `src/NexCode.Gui` |
| Providers and history | 24, 25 | `src/NexCode.Cli`, `src/NexCode.Data`, `src/NexCode.Gui` |
| CI/CD and docs | 27, 28, 37, 41 | `.github`, `docs`, `scripts` |
| Tray, telemetry, environments, settings | 29, 30, 31, 32 | `src/NexCode.Service`, `src/NexCode.Gui`, `src/NexCode.Data` |
| Compatibility, project management, security | 33, 34, 35, 36 | shared cross-cutting concern |
| Repository structure and roadmap | 38, 39, 40 | repository-wide |

## Immediate Slice Queue

1. Phase 0 control artifacts and spec normalization.
2. Phase 1 solution and project scaffolding.
3. Shared contracts for IPC and session lifecycle.
4. Data layer bootstrap with SQLCipher-ready EF Core foundation.
5. WinUI shell bootstrap with minimum sizing and placeholder 3-column layout.
