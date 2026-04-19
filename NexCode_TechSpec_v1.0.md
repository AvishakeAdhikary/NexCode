# NexCode — Complete Technical Specification v1.0
### Agentic AI Coding Platform · WinUI 3 / .NET 9 / C# 13
**Version 1.0 — April 2026 | CONFIDENTIAL — INTERNAL USE ONLY**

> **Owner:** avhishe.adhikary11@gmail.com

---

## Table of Contents

1. [Product Overview](#1-product-overview)
2. [System Architecture](#2-system-architecture)
3. [Authentication & In-App Purchases (Microsoft Store)](#3-authentication--in-app-purchases-microsoft-store)
4. [GUI Architecture (WinUI 3)](#4-gui-architecture-winui-3)
5. [CLI Engine (nexcode-cli)](#5-cli-engine-nexcode-cli)
6. [Windows Terminal Integration](#6-windows-terminal-integration)
7. [Agent Modes](#7-agent-modes)
8. [Model Personalities](#8-model-personalities)
9. [AI Memory System](#9-ai-memory-system)
10. [Implementation Plan & Todo List System](#10-implementation-plan--todo-list-system)
11. [Clarifying Questions Module (MCQ)](#11-clarifying-questions-module-mcq)
12. [Model Switcher](#12-model-switcher)
13. [Checkpoint, Diff & Revert](#13-checkpoint-diff--revert)
14. [Sandbox Mode](#14-sandbox-mode)
15. [Built-in Code Editor](#15-built-in-code-editor)
16. [Git Manager](#16-git-manager)
17. [MCP Client](#17-mcp-client)
18. [MCP Server Creation & Marketplace](#18-mcp-server-creation--marketplace)
19. [Sub-Agent System](#19-sub-agent-system)
20. [Keyboard Shortcuts & Keybinding Manager](#20-keyboard-shortcuts--keybinding-manager)
21. [Integrated Terminal](#21-integrated-terminal)
22. [Plugins & Automations](#22-plugins--automations)
23. [Remote & Cloud Execution](#23-remote--cloud-execution)
24. [AI Provider Configuration](#24-ai-provider-configuration)
25. [Session & History Management](#25-session--history-management)
26. [Permissions System](#26-permissions-system)
27. [CI/CD Pipeline — GitHub Actions](#27-cicd-pipeline--github-actions)
28. [Legal Documents (GitHub Pages)](#28-legal-documents-github-pages)
29. [System Tray & Background Service](#29-system-tray--background-service)
30. [Telemetry Service](#30-telemetry-service)
31. [Environment Management](#31-environment-management)
32. [Settings Panels](#32-settings-panels)
33. [System Integration & Backward Compatibility](#33-system-integration--backward-compatibility)
34. [Multi-Project Management](#34-multi-project-management)
35. [Error Handling & Logging](#35-error-handling--logging)
36. [Security Overview](#36-security-overview)
37. [Self-Contained Distribution](#37-self-contained-distribution)
38. [Repository & Project Structure](#38-repository--project-structure)
39. [Implementation Roadmap](#39-implementation-roadmap)
40. [Prompting Guide — How to Use This Document with an AI Coder](#40-prompting-guide--how-to-use-this-document-with-an-ai-coder)
41. [Manual Setup Steps (What You Must Do Yourself)](#41-manual-setup-steps-what-you-must-do-yourself)
42. [Appendices](#42-appendices)

---

## 1. Product Overview

### 1.1 Product Identity

**NexCode** is a native Windows agentic AI coding platform built with WinUI 3 (Windows App SDK). It combines a fully-featured graphical user interface (GUI) with a standalone command-line interface (CLI) service that executes all AI agent operations. The GUI is a pure front-end shell — every operation it performs is translated into a CLI command dispatched to the background service. This strict separation guarantees that advanced users, CI/CD pipelines, and remote clients can all drive the same engine without the GUI.

NexCode is conceptually comparable to OpenAI Codex CLI, Claude Code, Google Antigravity, and OpenCode — but it ships as a first-class Windows desktop application with:
- Rich native WinUI 3 UI
- Microsoft Store In-App Purchases for subscriptions
- MCP client/server support
- Remote and cloud execution
- An AI Memory system
- Model personalities
- Per-thread Implementation Plans and Todo Lists (Antigravity-style, but plan-first)
- A structured Clarifying Questions module (Codex-style MCQ)
- A built-in Monaco-based code editor with LSP linting
- A built-in MCP Marketplace
- Fully self-contained — no external runtime installation required

### 1.2 Design Philosophy

- **GUI is a CLI skin.** The GUI never calls AI provider APIs directly. It only starts, stops, and monitors CLI processes.
- **Separation of concerns.** UI layer (WinUI 3), process management layer (background service), CLI engine (.NET), and data layer (SQLite + SQLCipher) are fully decoupled.
- **Virtualized rendering.** Every list uses UI virtualization (ItemsRepeater). Only visible items are in memory.
- **Security by default.** Minimum-privilege execution, permission prompts before destructive operations, MSAL SSO gating, fully encrypted database, and E2E encryption for remote sessions.
- **Extensibility.** Custom modes, plugins, MCP servers, automations, model personalities, memories, and themes are all first-class extensibility points.
- **Windows-native.** Exposes PowerShell, Command Prompt, and WSL natively.
- **Self-contained.** Ships as a single MSIX package. All runtimes (.NET 9 self-contained, WebView2 fixed version, ConPTY binaries) are bundled. No prerequisite installations required.

### 1.3 High-Level Component Map

| Component | Technology | Role |
|---|---|---|
| `nexcode-gui` | WinUI 3 / C# / .NET 9 | Native Windows desktop UI |
| `nexcode-service` | .NET 9 Worker Service | Background service: process lifecycle, IPC, file watcher, system tray |
| `nexcode-cli` | C# Console App .NET 9 | Core AI engine: agent loop, MCP, providers, git, terminal, memory |
| `nexcode-remote` | ASP.NET Core 9 gRPC | Optional self-hosted remote CLI server |
| `nexcode-marketplace-sdk` | C# / TypeScript SDK | SDK for marketplace MCP servers and plugins |
| `nexcode.db` | SQLite 3.46+ via SQLCipher (encrypted) | All persistent state |

---

## 2. System Architecture

### 2.1 Process Architecture

#### 2.1.1 nexcode-service (Background Worker Service)

The always-running background .NET Worker Service. Responsibilities:
- Start and stop `nexcode-cli` worker processes on behalf of the GUI or remote clients
- Maintain a registry of active CLI workers (PIDs, session IDs, health state)
- Own the named-pipe and local gRPC server that the GUI connects to
- Manage the system tray icon and its context menu
- Watch for file-system changes in active project directories
- Transmit telemetry over low-bandwidth connections (opt-in)
- Auto-start MCP servers configured for auto-connect
- Manage the AI Memory store (read, write, cross-session reference resolution)
- Host the IAP (In-App Purchase) validation cache so the CLI can query subscription state

#### 2.1.2 nexcode-gui (WinUI 3 Application)

A thin shell that connects to `nexcode-service` via named pipe. It renders UI, translates user interactions into structured JSON command messages, and renders streamed responses. Never holds AI session state directly. **Minimum window size: 800 × 600 logical pixels**, enforced in `MainWindow` constructor via `AppWindow.MinSize`.

#### 2.1.3 nexcode-cli (CLI Engine Worker)

One instance spawned per active agent session. Responsibilities:
- Agent loop: receive user messages, call provider APIs, use tools, return streamed responses
- Manage file-system tools (read, write, search, diff, cut-paste)
- Manage terminal tools (spawn PTY via ConPTY, stream output)
- Act as MCP client and MCP server
- Manage git operations
- Read and write AI memories
- Read and write **Implementation Plans and Todo Lists** for the current thread
- Emit `clarify_question` events when confused (MCQ module)
- Persist session state to SQLite (encrypted) after every turn

#### 2.1.4 nexcode-remote (Optional gRPC Remote Server)

Optional ASP.NET Core 9 service wrapping `nexcode-cli`. Remote clients connect over mTLS gRPC. All session content is end-to-end encrypted with per-session ECDH X25519 key exchange on top of TLS 1.3.

### 2.2 IPC Protocol

Named pipe, newline-delimited JSON-RPC 2.0. Each message is a single UTF-8 JSON line terminated by `\n`.

| Direction | Examples |
|---|---|
| GUI → Service | `session.create`, `session.send_message`, `session.cancel`, `agent.spawn`, `agent.kill`, `git.status`, `mcp.connect`, `memory.get`, `memory.set`, `plan.get`, `todo.get`, `clarify.respond` |
| Service → GUI | `session.event` (streaming token), `session.checkpoint`, `session.error`, `file.changed`, `agent.status_update`, `memory.updated`, `plan.updated`, `todo.updated`, `clarify.question` |

### 2.3 Database — SQLite with SQLCipher (Full Encryption)

NexCode uses **SQLite 3.46+** accessed through `Microsoft.Data.Sqlite` (the officially recommended SQLite library for WinUI 3 / Windows App SDK apps) with **SQLCipher** (`SQLitePCLRaw.bundle_sqlcipher`) for AES-256 page-level encryption. Every byte on disk is encrypted.

#### Encryption Key Derivation

1. On first launch, generate a 32-byte cryptographically random salt. Store in Windows Credential Manager as `NexCode_DbSalt_<InstallId>`, DPAPI-protected for the current user.
2. Retrieve DPAPI-protected machine key via `ProtectedData.Protect(userData, entropy, DataProtectionScope.CurrentUser)` where entropy is the application's AppId string.
3. Derive final 32-byte SQLCipher key: `key = PBKDF2(dpapi_key, db_salt, 300000, 32, SHA256)`.
4. Pass to SQLCipher via `PRAGMA key = '<hex_key>';` on every connection open. Key is never written to disk.

#### Core Database Tables

| Table | Purpose |
|---|---|
| `Users` | Local user profile, MSAL token cache ref, subscription tier (IAP-sourced), super-user flag |
| `Projects` | Project directory, display name, env configs, active session IDs |
| `Sessions` | Session ID, project FK, mode FK, personality FK, permissions level, execution mode, sandbox flag, created/updated, title, current_plan_id FK |
| `Messages` | Session FK, role, content, checkpoint ID, token counts |
| `Checkpoints` | Session FK, message FK, git commit hash, diff snapshot, timestamp |
| `Modes` | Name, system prompt, icon, color, is_built_in, allowed_tools JSON |
| `Personalities` | Name, description, system_prompt_fragment, tone, verbosity, is_default, scope |
| `Memories` | Memory ID, key, value, scope, session FK?, project FK?, created_at, last_accessed_at, tags[] |
| `ImplementationPlans` | Plan ID, session FK, title, content (Markdown), status (draft/confirmed/rejected/completed), created_at, updated_at, version |
| `TodoLists` | List ID, session FK, plan FK, title, created_at, updated_at |
| `TodoItems` | Item ID, list FK, text, status (pending/in_progress/done/skipped), order_index, updated_at |
| `ClarifyQuestions` | Question ID, session FK, message FK, prompt_text, status (pending/answered/cancelled), created_at |
| `ClarifyOptions` | Option ID, question FK, label, is_custom (bool), order_index |
| `ClarifyAnswers` | Answer ID, question FK, selected_option_ids[], custom_text? |
| `Plugins` | Plugin ID, manifest JSON, enabled, install path, sandbox flag |
| `Automations` | Name, trigger JSON, steps JSON, enabled |
| `MCPServers` | Server ID, name, type, connection config JSON, auto_connect |
| `MCPServerDefs` | User-authored server definitions |
| `Providers` | Provider ID, name, base_url, api_key (encrypted), model configs JSON |
| `Themes` | Theme ID, name, is_built_in, colors JSON, font settings JSON |
| `KeyBindings` | Action ID, primary_key_combo, secondary_key_combo, enabled, is_system |
| `TelemetryQueue` | Unsynced telemetry events |
| `SubAgents` | Agent ID, parent session FK, status, config JSON, sandbox flag, spawned_at, ended_at |
| `SSHKeys` | Key ID, name, public key, private key blob (encrypted), associated git hosts JSON |
| `AuditLog` | Timestamp, action, session FK, user, details — append only |
| `EnvironmentConfigs` | Project FK, name, env_vars JSON (values encrypted), overrides |
| `EditorState` | Open files, cursors, scroll positions per project |
| `LinterRules` | Language ID, rule set JSON, enabled, severity overrides |
| `IAPCache` | Validated IAP receipt data, expiry, product IDs — synced from Microsoft Store API |

---

## 3. Authentication & In-App Purchases (Microsoft Store)

### 3.1 MSAL SSO Authentication

Authentication uses **MSAL for .NET** (`Microsoft.Identity.Client`, latest stable). The app is registered as a public client in Azure Active Directory / Microsoft Entra ID.

#### 3.1.1 Authentication Flow

1. `nexcode-service` starts and checks the MSAL token cache.
2. If no valid token, the service emits `ServiceEvents.AuthRequired` on the IPC channel.
3. The GUI shows a full-screen modal WebView2 overlay with the Microsoft OAuth login page. No controls are accessible behind it.
4. The user authenticates. Token received via MSAL loopback redirect URI.
5. Token stored in MSAL token cache encrypted with DPAPI. `ServiceEvents.AuthSuccess` emitted.
6. GUI dismisses modal and loads main dashboard.
7. Tokens are silently refreshed by the service. Failed silent refresh triggers the modal again.

#### 3.1.2 Super-User Override

> **IMPORTANT: This behavior applies only to one specific account and must not be modified or removed during development.**

If the authenticated user's email claim matches the account identity encoded in a locally installed **sealed super-user grant**:
- `SubscriptionState` is forced to `SuperUser` regardless of IAP state
- All feature flags are set to `true`
- The subscription gate modal is never shown
- An additional developer panel is shown in Settings with raw session JSON, telemetry queue inspector, feature-flag override toggles, IAP state inspector, and simulated tier switcher
- This is enforced in both the GUI and CLI engine via a cryptographically validated helper-issued session flag derived from the sealed grant. No checked-in email literal or public environment-variable activation path is used.

### 3.2 Subscription via Microsoft Store In-App Purchases (IAP)

**NexCode uses Microsoft Store In-App Purchases exclusively for subscription management.** There is no external licensing API. All billing, subscription lifecycle, refunds, and receipt validation are handled by the Microsoft Store. This simplifies compliance, avoids building payment infrastructure, and leverages Microsoft's existing billing relationships with users.

#### 3.2.1 IAP Product IDs

The following Store add-on products are registered in Partner Center:

| Product ID | Type | Description |
|---|---|---|
| `nexcode_pro_monthly` | Durable (renewable subscription) | NexCode Pro — Monthly |
| `nexcode_pro_annual` | Durable (renewable subscription) | NexCode Pro — Annual |
| `nexcode_team_monthly` | Durable (renewable subscription) | NexCode Team — Monthly |
| `nexcode_team_annual` | Durable (renewable subscription) | NexCode Team — Annual |
| `nexcode_enterprise_monthly` | Durable (renewable subscription) | NexCode Enterprise — Monthly |
| `nexcode_enterprise_annual` | Durable (renewable subscription) | NexCode Enterprise — Annual |

> **Note:** Microsoft Store subscription add-ons require that your app's Store listing is published and the add-ons are configured in Partner Center before they can be purchased. See Section 41 for the step-by-step manual setup.

#### 3.2.2 IAP Implementation

The application uses the `Windows.Services.Store` namespace (WinRT, available in Windows App SDK apps via `Microsoft.Windows.Store`):

```csharp
// Retrieve subscription state at startup
var storeContext = StoreContext.GetDefault();
var result = await storeContext.GetAppLicenseAsync();
// Check result.AddOnLicenses for active subscription product IDs
```

**Validation flow:**
1. On startup and every 24 hours, `nexcode-service` calls `StoreContext.GetAppLicenseAsync()` to retrieve the current license state from the Microsoft Store client.
2. The active add-on license product IDs are mapped to subscription tiers: `nexcode_pro_*` → Pro, `nexcode_team_*` → Team, `nexcode_enterprise_*` → Enterprise.
3. The resolved tier is stored in `IAPCache` table with an expiry (24 hours). The CLI reads the tier from the service via IPC, not from the Store API directly.
4. If no active subscription add-on is found, the tier defaults to `Free`.
5. If the Store API call fails (offline, Store unavailable), the cached tier from `IAPCache` is used. If cache is expired, the app degrades to `Free` tier with an in-app banner indicating it cannot verify subscription.

#### 3.2.3 Purchase Flow (GUI)

When a user tries to use a feature gated behind a higher tier, a **Subscription Gate Bottom Sheet** slides up from the bottom. It shows:
- The feature they tried to access and the tier required
- A comparison of Free / Pro / Team / Enterprise tiers (feature grid)
- **Subscribe buttons** for each tier that call `storeContext.RequestPurchaseAsync(productId)`. This opens the native Microsoft Store purchase dialog managed entirely by Windows — NexCode never handles payment details.
- A "Restore Purchases" button that calls `storeContext.GetAppLicenseAsync()` to re-sync the license (useful after reinstall or device change)
- Links to the Terms of Service and Privacy Policy (GitHub Pages)

#### 3.2.4 Subscription Tiers & Feature Gating

| Feature | Free | Pro | Team | Enterprise | SuperUser |
|---|---|---|---|---|---|
| Projects | 1 | Unlimited | Unlimited | Unlimited | Unlimited |
| Concurrent sessions | 1 | 5 | 20 | Unlimited | Unlimited |
| Sub-agents per session | 0 | 3 | 10 | Unlimited | Unlimited |
| Sandbox mode | No | Yes | Yes | Yes | Yes |
| Remote execution | No | Yes | Yes | Yes | Yes |
| Cloud execution | No | No | Yes | Yes | Yes |
| Custom modes | 2 | Unlimited | Unlimited | Unlimited | Unlimited |
| MCP server creation | No | Yes | Yes | Yes | Yes |
| Memory system | 50 memories | Unlimited | Unlimited | Unlimited | Unlimited |
| Custom personalities | 2 | Unlimited | Unlimited | Unlimited | Unlimited |
| Marketplace publishing | No | Yes | Yes | Yes | Yes |
| Implementation Plans | Yes | Yes | Yes | Yes | Yes |
| Clarifying Questions | Yes | Yes | Yes | Yes | Yes |

---

## 4. GUI Architecture (WinUI 3)

### 4.1 Technology Stack (Latest Stable Versions)

> **Policy: all packages use "Latest stable NuGet/SDK" at time of development. No hardcoded version numbers in this spec — always target the latest GA release.**

| Component | Library / Package | Notes |
|---|---|---|
| UI Framework | WinUI 3 via Windows App SDK | Latest stable GA |
| Language | C# 13 / .NET 9 | Target `net9.0-windows10.0.22621.0` |
| MVVM | CommunityToolkit.Mvvm | Latest stable NuGet |
| DI | Microsoft.Extensions.DependencyInjection | Aligned with .NET 9 |
| Virtualized Lists | WinUI ItemsRepeater + IncrementalLoadingCollection | Included in Windows App SDK |
| WebView | Microsoft.Web.WebView2 | Latest stable Evergreen SDK NuGet |
| Terminal | VtNetCore + ConPTY (Win32 P/Invoke) | Latest VtNetCore NuGet |
| Code Editor | Monaco Editor (latest, WebView2 hosted) | Bundled locally — no CDN required |
| Auth | Microsoft.Identity.Client (MSAL) | Latest stable NuGet |
| IAP | Windows.Services.Store (WinRT) | Inbox Windows App SDK |
| IPC | System.IO.Pipes (named pipe) | Inbox .NET 9 |
| Database | Microsoft.Data.Sqlite + SQLCipher | `SQLitePCLRaw.bundle_sqlcipher` latest |
| ORM | Microsoft.EntityFrameworkCore.Sqlite | Latest stable NuGet |
| Notifications | Microsoft.Windows.AppNotifications | Bundled with Windows App SDK |
| gRPC | Grpc.AspNetCore + Grpc.Net.Client | Latest stable NuGet |
| Animations | Lottie-Windows (WinUI3) | Latest stable NuGet |
| Serialization | System.Text.Json | Inbox .NET 9 |
| Logging | Microsoft.Extensions.Logging + Serilog.Sinks.File | Latest stable NuGet |
| Cryptography | System.Security.Cryptography + BouncyCastle.NetCore | Latest stable NuGet |
| Git | LibGit2Sharp | Latest stable NuGet |
| Markdown | Markdig | Latest stable NuGet |
| LSP Bridge | (per-language server processes, auto-downloaded) | See Section 15.4 |

### 4.2 Window Size Constraints

All windows enforce a minimum size set in the constructor via `AppWindow.MinSize`:

| Window | Minimum Size |
|---|---|
| Main dashboard | 800 × 600 px |
| Detached editor | 800 × 600 px |
| Settings dialog | 700 × 500 px |
| Mode editor sheet | 640 × 480 px |
| Terminal window (detached) | 600 × 400 px |
| Plan/Todo sheet | 640 × 480 px |
| Clarify Question overlay | 520 × 320 px |

### 4.3 Application Shell Layout

#### Column A — Navigation Rail (left, fixed 220 px)
- Application logo and version at top
- Project list (virtualized `IncrementalLoadingCollection`): name, last-active time, colored indicator. Long-press: rename, archive, remove, settings
- Navigation items: New Chat, Search, Plugins, Automations, History, Memories, Plans
- Settings gear pinned at bottom

#### Column B — Session Panel (center, flex)
- **Session header bar:** mode badge (color-coded), personality avatar badge, model selector dropdown (see Section 12), execution mode selector (Local/Remote/Cloud), sandbox toggle, shell selector
- **Plan/Todo status bar** (shown when a plan is active): plan title truncated, todo progress indicator (e.g., `3/7 tasks done`), View Plan button, View Todo button
- **Messages list** (virtualized `ItemsRepeater`): user messages, assistant messages, tool call outputs, checkpoint cards, clarify question cards, plan artifact cards, todo artifact cards
- **Input area:** multi-line TextBox, @file mention autocomplete, attachment button, permission toggle, Send/Stop button

#### Column C — Context Panel (right, resizable 320–600 px)
- **Run** — live terminal output, tool call stream, sub-agent activity tree
- **Git** — embedded git manager
- **Code Diff** — Monaco diff view of checkpoint or current changes
- **Editor** — built-in code editor (Section 15)
- **Plans** — plan/todo viewer and editor for the current thread (Section 10)

### 4.4 Virtualization Architecture

All scrollable collections use `ItemsRepeater` with `VirtualizingStackLayout`. Message list backed by `IncrementalLoadingCollection<MessageViewModel>` (50 items/page). Item containers pooled and recycled by `DataTemplate` type. Streaming tokens update only the visible `AssistantMessage` container via `ObservableProperty`. Plan Artifact Cards and Todo Artifact Cards are also DataTemplate types in the recycled pool.

### 4.5 Theme System

`ThemeService` manages two layers: (1) Base theme: Dark (default), Light, High Contrast. (2) Semantic accent overrides: JSON key → color applied as `ResourceDictionary` overrides at runtime. All semantic keys prefixed `NexCode.*`. Themes export as `.nextheme` files.

---

## 5. CLI Engine (nexcode-cli)

### 5.1 Overview

`nexcode-cli` is a standalone .NET 9 self-contained console application. Invoked by the service on behalf of the GUI, or standalone from any terminal. All AI provider calls, tool executions, plan/todo management, and question-asking happen here.

### 5.2 CLI Command Structure

| Command | Description |
|---|---|
| `nexcode chat [--mode <m>] [--project <path>] [--sandbox]` | Start interactive session |
| `nexcode run <prompt> [--mode <m>] [--no-confirm]` | Non-interactive single prompt |
| `nexcode session list / load / export` | Session management |
| `nexcode plan get / confirm / reject` | Plan management |
| `nexcode todo get / check <id> / uncheck <id>` | Todo management |
| `nexcode mcp serve [--port <p>] [--stdio]` | Start as MCP server |
| `nexcode mcp connect <config>` | Connect to MCP server |
| `nexcode git <status|diff|commit|push|revert|branch|checkout|clone|remote|add|rm|mv|reset|log|show|stash|pop|apply|merge|tag|fetch|pull>` | Git commands |
| `nexcode agent spawn <config-json>` | Spawn sub-agent |
| `nexcode agent kill <agent-id>` | Kill sub-agent |
| `nexcode memory get/set/list` | Memory management |
| `nexcode remote serve` | Start gRPC remote server |
| `nexcode remote connect <host:port>` | Connect to remote server |
| `nexcode config providers / modes / personalities` | Configuration |
| `nexcode shell [--type powershell\|cmd\|custom]` | Interactive shell |
| `nexcode --version` | Version info |

### 5.3 Agent Loop

1. Receive user message (stdin in CLI mode, named pipe in service mode).
2. Construct request context: system prompt (mode + personality fragment), conversation history (SQLite), available tools (built-in + MCP + plugin), relevant memories (auto-injected), active plan + todo status injected as context, permission constraints.
3. Call the configured AI provider API with streaming enabled.
4. Stream tokens as JSON events: `{ type: 'token', content: '...' }`.
5. When model emits a tool call, execute within permission/sandbox constraints. Emit `tool_call` and `tool_result` events.
6. When model emits a `clarify_question` tool call, pause execution, emit a `clarify.question` event to the GUI/CLI, and wait for `clarify.respond`. See Section 11.
7. When model emits a `create_plan` or `update_plan` tool call, process plan changes and emit `plan.updated` event. See Section 10.
8. When model emits a `create_todo_list`, `check_todo_item`, or `delete_todo_list` tool call, process and emit `todo.updated` event. See Section 10.
9. If permission required and not pre-granted, emit `permission_request`. Wait for `permission_response` (timeout 30s).
10. After all tool calls resolve, continue streaming response.
11. At end of turn, emit `checkpoint` event with git diff and file change summary.
12. Persist full turn (messages, checkpoint, updated memories, updated plan/todo state) to encrypted SQLite.
13. Return to step 1.

### 5.4 Built-in Tools

| Tool | Description | Permission |
|---|---|---|
| `read_file` | Read file contents by path | Default |
| `write_file` | Write/overwrite a file | Default (warned) / Full (auto) |
| `create_file` | Create a new file | Default |
| `delete_file` | Delete a file | Full only |
| `cut_paste_file` | Cut lines X–Y from file A and insert at line Z in file B. See Section 5.6. | Default (warned) |
| `list_directory` | List directory with metadata | Default |
| `search_files` | Regex/glob search across project | Default |
| `execute_command` | Run shell command via session's configured shell | Full (or Default with `--no-confirm`) |
| `open_terminal` | Spawn interactive PTY terminal | Full |
| `git_status` | Get git status | Default |
| `git_diff` | Get diff | Default |
| `git_commit` | Create commit | Full |
| `git_push` | Push to remote | Full |
| `git_revert` | Revert to checkpoint | Full |
| `memory_read` | Read a memory value | Default |
| `memory_write` | Write a memory value | Default |
| `memory_list` | List memory keys | Default |
| `create_plan` | Create a new Implementation Plan for the current thread | Default |
| `update_plan` | Update an existing plan (status, content, version) | Default |
| `delete_plan` | Delete a plan | Default |
| `create_todo_list` | Create a new Todo List linked to current thread | Default |
| `add_todo_item` | Add item to a todo list | Default |
| `check_todo_item` | Mark a todo item as done | Default |
| `uncheck_todo_item` | Mark a todo item as pending | Default |
| `delete_todo_item` | Remove a todo item | Default |
| `delete_todo_list` | Delete an entire todo list | Default |
| `clarify_question` | Ask the user structured MCQ clarifying questions. See Section 11. | Default (always prompted) |
| `spawn_subagent` | Spawn a child agent | Pro+ |
| `kill_subagent` | Terminate a child agent | Pro+ |
| `mcp_call_tool` | Call a tool on a connected MCP server | Default (per-server config) |
| `web_fetch` | Fetch a URL and return content | Default |
| `clipboard_read` | Read from Windows clipboard | Prompted once per session |
| `clipboard_write` | Write to Windows clipboard | Default |
| `lsp_hover` | Get LSP hover info for a symbol | Default |
| `lsp_diagnostics` | Get linter diagnostics for a file | Default |

### 5.5 Output Event Schema

| Event Type | Key Fields | Description |
|---|---|---|
| `token` | `content`, `session_id` | Streamed token |
| `tool_call` | `tool_name`, `arguments`, `call_id` | Model requested tool |
| `tool_result` | `call_id`, `result`, `error?` | Tool result |
| `permission_request` | `tool_name`, `description`, `level_required` | Permission needed |
| `clarify.question` | `question_id`, `session_id`, `questions[]` | MCQ question emitted (see Section 11) |
| `plan.updated` | `session_id`, `plan_id`, `status`, `title` | Plan created/updated/deleted |
| `todo.updated` | `session_id`, `list_id`, `items[]` | Todo list changed |
| `memory_updated` | `key`, `scope`, `action` | Memory changed |
| `checkpoint` | `session_id`, `git_hash`, `diff_summary`, `files_changed[]` | End-of-turn checkpoint |
| `session_start` | `session_id`, `mode`, `personality`, `project_path`, `sandbox` | Session initialized |
| `session_end` | `session_id`, `reason` | Session ended |
| `agent_spawned` | `agent_id`, `parent_session_id`, `config`, `sandbox` | Sub-agent started |
| `agent_ended` | `agent_id`, `reason`, `summary` | Sub-agent terminated |
| `error` | `code`, `message`, `recoverable` | Error occurred |
| `status` | `message`, `level` | Info status message |
| `progress` | `percent`, `label` | Long-running progress |
| `linter_diagnostic` | `file`, `line`, `col`, `severity`, `message`, `rule_id` | LSP diagnostic |

### 5.6 cut_paste_file Tool — Specification

A custom NexCode tool that atomically moves a range of lines from one file to another (or within the same file). This reduces token cost for debugging/refactoring large files — the AI does not need to read and rewrite entire file contents.

#### Input Schema

| Parameter | Type | Required | Description |
|---|---|---|---|
| `source_file` | string (path) | Yes | Source file path (absolute or project-relative) |
| `source_start_line` | integer (1-indexed) | Yes | First line to cut (inclusive) |
| `source_end_line` | integer (1-indexed) | Yes | Last line to cut (inclusive) |
| `destination_file` | string (path) | Yes | Destination file (created if not exists) |
| `destination_insert_line` | integer (1-indexed) | Yes | Line before which cut lines are inserted. 0 = prepend, line_count+1 = append. |
| `leave_blank_lines` | boolean | No (default: false) | If true, replace cut lines in source with blank lines (preserves line numbering) |
| `comment_marker` | string | No | If provided, inserts a comment at the cut position, e.g. `// Moved to <file>:<line>` |

#### Behavior

- Atomically writes both files using double `MoveFileEx` rename (source_tmp → source, dest_tmp → dest). See Appendix B.
- Produces a unified diff of both file changes included in `tool_result`.
- Creates a sub-checkpoint tagged `tool_name: cut_paste_file`.
- Same-file moves: final positions calculated as if cut happened first, then insert.
- Validation: `source_start_line ≤ source_end_line`, both lines exist, `destination_insert_line` in range `[0, len(dest)+1]`. Fails without modifying files if invalid.

---

## 6. Windows Terminal Integration

### 6.1 Supported Shells

| Shell | Executable Resolution | Notes |
|---|---|---|
| PowerShell 7 (default) | Auto-detect: `pwsh.exe` from PATH, then `%ProgramFiles%\PowerShell\7\pwsh.exe` | Preferred on Windows 11 |
| Windows PowerShell 5.1 (fallback) | `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe` | Pre-installed on all Windows |
| Command Prompt | `cmd.exe` from `%ComSpec%` | Legacy support for batch scripts |
| Git Bash | Auto-detect from common Git install paths | POSIX tooling on Windows |
| WSL | `wsl.exe --distribution <distro>` | User selects installed WSL distro |
| Custom | User-specified absolute path | Custom startup args and env vars |

### 6.2 execute_command Shell Context

| Shell | Command Pattern |
|---|---|
| PowerShell 7 | `pwsh.exe -NonInteractive -Command <cmd>` |
| PowerShell 5.1 | `powershell.exe -NonInteractive -Command <cmd>` |
| Command Prompt | `cmd.exe /C <cmd>` |
| Git Bash | `git-bash.exe -c "<cmd>"` |
| WSL | `wsl.exe --distribution <distro> -- bash -c "<cmd>"` |

Working directory always set to project root (or `EnvironmentConfigs` override). Timeout: 30 seconds (configurable). Process killed on timeout.

---

## 7. Agent Modes

### 7.1 Built-in Modes

| Mode | System Prompt Focus | Write | Execute | Color |
|---|---|---|---|---|
| Plan | Analyze requirements, create Implementation Plans, suggest changes — do NOT modify files | Disabled | Disabled | `#1565C0` Blue |
| Code | Implement the requested feature or fix | Enabled | Limited | `#2E7D32` Green |
| Debug | Diagnose and fix bugs. Analyze traces, logs, test output | Enabled | Enabled | `#E65100` Orange |
| Ask | Answer questions about the codebase — do not modify files | Disabled | Disabled | `#6A1B9A` Purple |

### 7.2 Custom Mode Schema

| Field | Type | Description |
|---|---|---|
| `id` | UUID | Auto-generated |
| `name` | string (max 32) | Display name |
| `system_prompt` | string | Full system prompt for sessions using this mode |
| `icon` | string | Segoe Fluent Icons glyph or SVG |
| `accent_color` | hex | Mode badge color |
| `allowed_tools` | string[] | Whitelist. Empty = all. `'none'` = disabled. |
| `denied_tools` | string[] | Blacklist (overrides allowed_tools) |
| `default_permission_level` | Default\|Full | Overrides session permission level |
| `max_tokens` | integer | Override max tokens |
| `temperature` | float 0–2 | Override temperature |
| `provider_override` | provider ID | Force specific provider |
| `personality_override` | personality ID | Force specific personality |
| `inherit_from` | mode ID | Base on another mode |

---

## 8. Model Personalities

### 8.1 Overview

A **Personality** adjusts the model's communication style, verbosity, and tone. Separate from Modes — Mode controls *what* the agent can do; Personality controls *how* it communicates. A personality fragment is injected **before** the Mode's system prompt.

### 8.2 Built-in Personalities

| Name | Tone | Verbosity | Description |
|---|---|---|---|
| Default | Professional | Balanced | Clear, concise, accurate |
| Detailed | Academic | High | Thorough explanations with reasoning and trade-offs |
| Concise | Direct | Low | Minimal prose; prioritizes code and actionable output |
| Mentor | Encouraging | Medium-High | Step-by-step, asks clarifying questions |
| Senior Dev | Pragmatic | Medium | Assumes expertise; surfaces edge cases |

### 8.3 Custom Personality Schema

| Field | Type | Description |
|---|---|---|
| `id` | UUID | Auto-generated |
| `name` | string (max 32) | Display name |
| `description` | string (max 200) | Tooltip in personality picker |
| `system_prompt_fragment` | string | Injected before the Mode's system prompt |
| `tone` | enum | professional, academic, casual, encouraging, pragmatic, creative |
| `verbosity` | enum | minimal, low, medium, high, exhaustive |
| `scope` | enum | global, project, session |
| `project_id` | UUID? | Set when scope = project |
| `is_default` | boolean | Default for new sessions |
| `avatar_icon` | string? | Segoe glyph or emoji |
| `avatar_color` | hex? | Avatar badge background |

---

## 9. AI Memory System

### 9.1 Scopes

| Scope | Key Format | Visibility |
|---|---|---|
| `global` | `nexcode://memory/global/<key>` | All sessions across all projects |
| `project` | `nexcode://memory/project/<project_id>/<key>` | All sessions within a project |
| `session` | `nexcode://memory/session/<session_id>/<key>` | Current session only; not persisted |

### 9.2 Automatic Memory Injection

At each turn start, the Memory Engine runs a relevance query (semantic similarity or TF-IDF) against global and project-scoped memories. The top N (default 5, configurable) are injected into the system prompt under a `--- Relevant Memories ---` header.

### 9.3 Cross-Thread Reference

Any session can reference memories from another session by URI via `memory_read`. The **Memories** panel in the navigation rail shows all memories with filter, search, create, edit, tag, delete, export, import, and URI-copy capabilities.

### 9.4 Memory Limits

| Tier | Max Global | Max Per-Project | Auto-Eviction |
|---|---|---|---|
| Free | 50 total | 20 per project | Oldest by `last_accessed_at` |
| Pro+ | Unlimited | Unlimited | None |

---

## 10. Implementation Plan & Todo List System

### 10.1 Overview

This system gives each thread a structured, persistent workspace document layer inspired by Google Antigravity's Artifacts concept, but with NexCode's own workflow: **Implementation Plan first, then Todo List — never the reverse.** The AI creates plans as a natural part of its reasoning. Users confirm or reject them. Only after confirmation does the AI generate and manage a Todo List. Both documents are living — the AI can update, replace, or delete them at any point during the thread. Sub-agent threads have their own independent plans and todo lists.

This is implemented as a set of AI tools (`create_plan`, `update_plan`, `delete_plan`, `create_todo_list`, `add_todo_item`, `check_todo_item`, etc.) and a dedicated GUI panel in Column C (Plans tab) and as inline Artifact Cards in the message stream.

### 10.2 NexCode Plan-First Workflow

```
User sends request
      ↓
AI creates Implementation Plan (create_plan tool)
      ↓
[PLAN ARTIFACT CARD appears in message stream]
User reviews plan → Confirm | Reject | Request Changes
      ↓ (on Confirm)
AI creates Todo List (create_todo_list + add_todo_item tools)
      ↓
[TODO ARTIFACT CARD appears in message stream]
AI begins executing tasks one by one
      ↓
After each task: AI calls check_todo_item to mark it done
      ↓
[TODO ARTIFACT CARD updates live in message stream]
      ↓
All tasks done → AI summarizes and marks plan status = completed
```

> **Key difference from Antigravity:** Antigravity creates task list first, then plan. NexCode requires an **Implementation Plan to be confirmed before the Todo List is even created.** This ensures the overall approach is approved before granular task tracking begins.

### 10.3 Implementation Plan — Data Model

Stored in the `ImplementationPlans` table.

| Field | Type | Description |
|---|---|---|
| `id` | UUID | Auto-generated |
| `session_id` | UUID FK | The thread (session) this plan belongs to |
| `title` | string | Short plan title (e.g., "Implement OAuth login flow") |
| `content` | Markdown string | The full plan body (goal, tech stack, approach, proposed changes) |
| `status` | enum | `draft` → `pending_confirmation` → `confirmed` \| `rejected` \| `completed` |
| `version` | integer | Increments on each update |
| `created_at` | timestamp | — |
| `updated_at` | timestamp | — |
| `created_by` | enum | `ai` \| `user` |

**Status transitions:**
- `draft`: AI has called `create_plan` but not yet presented it. Not visible to user yet.
- `pending_confirmation`: AI has finished writing the plan. Plan Artifact Card appears in the message stream. Awaiting user response.
- `confirmed`: User clicked Confirm. AI proceeds to create Todo List.
- `rejected`: User clicked Reject. AI receives rejection reason and can create a revised plan.
- `completed`: All todo items are done. AI calls `update_plan(status: completed)`.

A session can have multiple plans over its lifetime (the AI can delete and create new ones). Only one plan is `pending_confirmation` at a time. Multiple plans can be `completed` or `rejected` in history.

### 10.4 Implementation Plan — AI Tool Contracts

#### `create_plan`
```json
{
  "title": "string (required)",
  "content": "string (Markdown, required) — full plan body",
  "auto_present": "boolean (default: true) — if true, immediately sets status to pending_confirmation"
}
```
Returns: `{ plan_id, status }`. Emits `plan.updated` IPC event. GUI renders Plan Artifact Card.

#### `update_plan`
```json
{
  "plan_id": "UUID (required)",
  "title": "string? (optional)",
  "content": "string? (optional)",
  "status": "enum? (optional) — valid transitions: pending_confirmation→confirmed/rejected; confirmed→completed"
}
```
Returns: `{ plan_id, version, status }`. Emits `plan.updated`.

#### `delete_plan`
```json
{ "plan_id": "UUID (required)" }
```
Soft-deletes (marks `status = deleted`). Emits `plan.updated`. The Plan Artifact Card in the message stream shows a "Plan deleted by AI" state.

### 10.5 Plan Artifact Card (GUI)

A Plan Artifact Card is a special message item rendered in the message stream. It is a card containing:
- **Title bar:** plan title, `v{version}` badge, status badge (color-coded: yellow = pending, green = confirmed, red = rejected, grey = completed)
- **Content preview:** the first ~150 characters of the Markdown content, with a "Show full plan" expand toggle that opens the full plan in the Plans tab in Column C
- **Action buttons (shown only when `status = pending_confirmation`):**
  - ✅ **Confirm** — calls the `plan.confirm` IPC message → service sends `clarify.respond`-style acknowledgment to the CLI → CLI calls `update_plan(status: confirmed)` and proceeds to create Todo List
  - ❌ **Reject** — opens a small text input inline where the user can type rejection reason (optional), then sends rejection to CLI
  - ✏️ **Request Changes** — opens an inline text box. User types desired changes. Sent to CLI as a system message that triggers the AI to update the plan
- **Version history link:** opens a side sheet showing all previous versions of the plan (title, content, timestamp, diff from previous version)

When status changes from `pending_confirmation`, the action buttons disappear and are replaced by the status badge.

### 10.6 Todo List — Data Model

Stored in `TodoLists` and `TodoItems` tables.

**TodoList:**
| Field | Description |
|---|---|
| `id` | UUID |
| `session_id` | FK to the thread |
| `plan_id` | FK to the confirmed Implementation Plan this list executes |
| `title` | Short title (e.g., "OAuth Implementation Tasks") |
| `created_at` | — |
| `updated_at` | — |

**TodoItem:**
| Field | Description |
|---|---|
| `id` | UUID |
| `list_id` | FK to TodoList |
| `text` | Task description (Markdown-capable inline) |
| `status` | `pending` \| `in_progress` \| `done` \| `skipped` |
| `order_index` | For manual reordering |
| `updated_at` | — |

### 10.7 Todo List — AI Tool Contracts

All todo tools emit `todo.updated` IPC events that cause the Todo Artifact Card to update live.

```
create_todo_list(title, plan_id?, items[]?) → { list_id }
add_todo_item(list_id, text, insert_after_id?) → { item_id }
check_todo_item(item_id) → marks status=done
uncheck_todo_item(item_id) → marks status=pending
set_todo_item_in_progress(item_id) → marks status=in_progress
skip_todo_item(item_id) → marks status=skipped
delete_todo_item(item_id) → removes item
delete_todo_list(list_id) → removes entire list
reorder_todo_items(list_id, ordered_ids[]) → reorders items
```

### 10.8 Todo Artifact Card (GUI)

Rendered as a card in the message stream. Contains:
- **Title:** todo list title
- **Progress bar:** visual fill representing `done_items / total_items` (excludes skipped)
- **Progress text:** `N/M tasks done (X skipped)`
- **Item list** (visible up to 5 items inline, rest collapsed with "Show all N tasks"):
  - Each item shows: status icon (⬜ pending, 🔄 in_progress, ✅ done, ⏭️ skipped), item text
  - Status icon is interactive: user can click to toggle pending/done manually
  - Right-click context menu: Edit text, Skip, Delete
- **Add task** button (user can manually add tasks)
- **Open in Plans tab** button (opens full list in Column C Plans tab)

The Todo Artifact Card updates in real-time as the AI calls todo tools — the checkmarks animate in as tasks are completed.

### 10.9 Plans Tab in Column C

The **Plans** tab in Column C shows the complete plan/todo workspace for the current session thread. It has two sub-panels:

**Left panel — Plan list:** All plans for the current session, with status badges. Click a plan to view it.

**Right panel — Plan/Todo detail view:**
- Full Markdown rendering of the selected plan's content (using Markdig)
- Inline edit button (opens the plan content in a Monaco editor instance within the tab)
- All Todo Lists linked to this plan displayed below the plan content
- Full expandable todo items with interactive checkboxes
- The AI (when it has write access to the thread) can update any of this at any time during execution

### 10.10 Sub-Agent Plans

Sub-agent threads have their **own independent** `ImplementationPlans` and `TodoLists`. The sub-agent's plans are visible in the parent session's Run tab under the sub-agent's activity entry, with a "View Plan" expandable. The parent agent can read (but cannot modify) a sub-agent's plan via `memory_read` on the sub-agent's context, or the sub-agent can write a summary memory that the parent reads.

### 10.11 Mode Interaction

- In **Plan mode** (the built-in mode): the agent is encouraged by its system prompt to always create an Implementation Plan before discussing any code changes. The Plan mode system prompt fragment reads: *"You are in Plan mode. Your primary tool is create_plan. Before suggesting any file changes or coding tasks, you MUST create and present an Implementation Plan using the create_plan tool. Wait for user confirmation before proceeding."*
- In **Code/Debug modes**: the agent may create plans when the task is complex (multiple files, architectural decisions) but is not required to for simple single-file edits.
- In **Ask mode**: plans and todos are disabled (no plan/todo tools available).

---

## 11. Clarifying Questions Module (MCQ)

### 11.1 Overview

Inspired by the `ask_user_question` tool proposed in the OpenAI Codex CLI (GitHub issue #9926) and the interactive question patterns used in Codex Plan mode. When the agent encounters genuine ambiguity that it cannot resolve by making a reasonable assumption, it can invoke the `clarify_question` tool to ask the user structured, constrained questions before proceeding.

**Design principles:**
- The AI should default to making reasonable assumptions and proceeding (bias to action). It should only invoke `clarify_question` when the ambiguity is *fundamental* — meaning different answers would lead to significantly different implementations.
- The questions are **structured** (MCQ + optional custom text) rather than free-form chat to minimize friction and keep the model's input structured.
- A maximum of **5 questions** can be asked in a single `clarify_question` call.
- Each question has a maximum of **4 MCQ options** plus one optional **"Other — type your own"** custom text box.
- The tool can be called at any point during any thread (main or sub-agent) at any time.

### 11.2 clarify_question Tool Contract

```json
{
  "context": "string (required) — brief explanation of why the AI is confused",
  "questions": [
    {
      "id": "string (unique within this call)",
      "prompt": "string (required) — the question text",
      "type": "single_choice | multi_choice",
      "options": [
        { "id": "opt_1", "label": "string (max 80 chars)" },
        { "id": "opt_2", "label": "string" },
        ...
        // 2–4 options max
      ],
      "allow_custom": "boolean — if true, shows a free-text 'Other' input",
      "required": "boolean (default: true)"
    }
    // 1–5 questions max
  ]
}
```

Returns: `{ question_id, status: presented }` immediately. The tool call **blocks** the agent loop (no further tokens are streamed, no further tool calls are made) until a `clarify.respond` event is received from the GUI/CLI.

### 11.3 Clarify Question Card (GUI)

When `clarify.question` event arrives, a **Clarify Question Card** is rendered inline in the message stream. The card:

- Shows the `context` text in italic at the top as explanation of why the AI is asking
- For each question:
  - Question prompt text
  - MCQ buttons: each option rendered as a clickable pill button (styled distinctly from regular UI buttons — uses a light bordered style). Selecting a `single_choice` option immediately highlights it and advances focus to the next question. Selecting a `multi_choice` option toggles it.
  - If `allow_custom = true`: an "Other..." pill button that, when clicked, expands inline into a single-line text input field
- A **Submit Answers** button at the bottom of the card (disabled until all `required` questions have an answer)
- A **Skip All** button that cancels the question (sends a `clarify.respond` with `cancelled: true`). The AI receives a cancellation and proceeds with best-guess assumptions, stating its assumptions in its next message.

**Keyboard navigation:** Tab/Shift-Tab between questions. Arrow keys within MCQ buttons. Space to select. Enter on Submit.

### 11.4 Clarify Question in CLI Mode

In pure CLI mode (no GUI), the question is rendered as a terminal UI overlay using a similar tabbed interface:
- Each question is shown with numbered options
- User navigates with keyboard (arrow keys, space to select, enter to advance)
- A "Submit" prompt at the end
- Text `[C]ancel` to skip

### 11.5 Clarify Answer Persistence

All clarify question/answer pairs are stored in `ClarifyQuestions`, `ClarifyOptions`, and `ClarifyAnswers` tables. They are included in session exports and are visible in the History panel's session view as collapsed "AI asked:" entries in the message stream. Sub-agent clarify questions are forwarded to the parent session's message stream, clearly labeled with the sub-agent ID.

### 11.6 Anti-Spam Rules

The system prompt for all modes includes the following guidance (injected automatically, not user-editable):

> *"You may use the clarify_question tool only when the user's intent is fundamentally ambiguous and different answers would lead to significantly different implementations. Do not ask clarifying questions for: stylistic preferences you can decide yourself, minor implementation details, information you can infer from the codebase, or anything you can handle with a reasonable assumption. Always state your assumption when you proceed without asking. Maximum 1 clarify_question call per turn."*

This prevents the AI from becoming annoying by asking trivial questions.

---

## 12. Model Switcher

### 12.1 Overview

The active AI model can be changed at any point during a session, including mid-conversation. The model switch takes effect from the next turn onwards. Previous turns in the conversation are unaffected. The model switcher is available in:
- The **session header bar** in Column B (always visible)
- The **Settings > Providers** panel (sets the default for new sessions)
- The **nexcode-cli** via the `/model` slash command (mirrors OpenAI Codex CLI's `/model` pattern)

### 12.2 Model Switcher Dropdown (GUI)

The model switcher is a compact **dropdown button** in the session header bar, positioned between the personality avatar and the execution mode selector. It shows:
- Current model display name (e.g., `claude-opus-4` or `gpt-4.1`)
- Provider icon (small favicon-sized logo) to the left of the model name
- A dropdown arrow

**Dropdown contents:**
- **Grouped by provider.** Each provider group shows the provider name as a non-selectable header, followed by its configured models.
- Each model row shows: model name, context window size, a "Vision" badge if the model supports image input, and a "Tools" badge if it supports tool use.
- A **⭐ Default** badge on the currently configured default model.
- A **Search box** at the top of the dropdown to filter models by name.
- An **Add Provider / Configure** link at the bottom that navigates to Settings > Providers.

**Behavior:**
- Selecting a model closes the dropdown and updates the session header.
- If the selected model does not support tool use, an inline warning banner appears below the session header: *"⚠️ This model does not support tool use. The agent cannot execute file operations or commands."*
- If the selected model has a smaller context window than the current conversation history, a warning badge appears: *"⚠️ Conversation may exceed context limit."*
- The model change is stored in `Sessions.model_override` (nullable FK to a `Providers.model_id`). `null` means "use the provider's configured default."

### 12.3 Model Switcher in CLI

```bash
/model                    # shows current model and available models list
/model claude-opus-4      # switch to claude-opus-4 for the rest of the session
/model ?                  # fuzzy-search models interactively
```

---

## 13. Checkpoint, Diff & Revert

### 13.1 What is a Checkpoint?

At the end of every agent turn, `nexcode-cli` creates a checkpoint: a git commit (`nexcode/checkpoint/<session-id>/<turn-number>`) of all file changes plus a diff snapshot in the `Checkpoints` table. Checkpoint Cards appear inline in the message stream.

### 13.2 Diff Viewer

Clicking **View Diff** on a Checkpoint Card opens the Code Diff tab in Column C. Monaco Editor in diff mode. Side-by-side and inline unified views. File-tree navigator. Line-level commenting.

### 13.3 Revert

Clicking **Revert** performs `git reset --hard` to the tagged checkpoint commit. Confirmation dialog lists affected files. After confirmation, `nexcode-cli` executes the revert and the GUI scrolls to the checkpoint with a **Reverted** badge.

### 13.4 Continue From Here (Branch)

Creates a new git branch from the checkpoint (`nexcode/branch/<session-id>/<timestamp>`), loads checkpoint's conversation history as starting context, and opens a new session. Original session preserved intact.

### 13.5 Retry at Any Turn

Every user message has a **Retry** button (hover). Clicking it: (1) reverts git changes to prior checkpoint; (2) deletes current turn's assistant messages and tool results from DB; (3) re-submits the user message. An **Edit** (pencil icon) button allows editing before retry.

---

## 14. Sandbox Mode

### 14.1 Constraints

When `sandbox_enabled = true` on a session, a Windows Job Object restricts the `nexcode-cli` process:

| Constraint | Sandboxed | Normal |
|---|---|---|
| File system | Read/write restricted to project root and `%TEMP%` only | Full user file system |
| Network | Outbound blocked except AI provider and MCP endpoints | Unrestricted |
| Process creation | Shell commands only via configured shell executable | Unrestricted |
| Registry | No read or write | User hive accessible |
| CPU time | Configurable (default: 5 CPU-minutes per turn) | No limit |
| Memory | Configurable (default: 2 GB working set) | No limit |
| Clipboard | Disabled | Enabled (with prompt) |
| Git remote operations | Disabled (local only) | Full git access |

Sub-agents inherit parent sandbox state and cannot override it to `false`.

---

## 15. Built-in Code Editor

### 15.1 Overview

Monaco Editor (VS Code's engine) hosted in WebView2. Accessible via the Editor tab in Column C. Detachable to a standalone window (minimum 800 × 600). All Monaco assets bundled locally — no CDN required.

### 15.2 Editor Features

| Feature | Implementation |
|---|---|
| Syntax highlighting | Monaco built-in, 40+ languages |
| IntelliSense | Monaco word completion + LSP |
| Inline diagnostics | LSP markers via Monaco API |
| Find & Replace | Monaco native (Ctrl+H) |
| Multi-cursor | Monaco native (Alt+Click, Ctrl+D) |
| Code folding | Monaco native |
| Minimap | Monaco native, toggleable |
| Diff view | Monaco diff editor |
| JSON schema validation | Monaco JSON language service with built-in NexCode schemas |
| Custom keybindings | Synchronized from KeyBindings table |
| Auto-save | Configurable: off / on focus change / 500ms interval |
| File tabs | Multiple open files, state persisted in `EditorState` table |
| Split editor | Up to 4 panes |
| Breadcrumb navigation | Monaco native |
| Terminal integration | Ctrl+` opens terminal split below editor |

### 15.3 Detached Editor Window

Standalone `AppWindow` instance. Shares Monaco model state with in-panel editor — edits reflect in both. Minimum size 800 × 600.

### 15.4 LSP Linter Bridge

| Language | Server | Download Source |
|---|---|---|
| TypeScript / JavaScript | typescript-language-server | npm (auto-install, bundled Node.js) |
| Python | pylsp | PyPI (bundled pip) |
| C# | OmniSharp / Roslyn LSP | GitHub Releases (auto-download) |
| Go | gopls | `go install` (auto-download) |
| Rust | rust-analyzer | GitHub Releases (auto-download) |
| PowerShell | PSLS (PowerShell Extension Language Service) | GitHub Releases |
| JSON | vscode-json-languageserver | npm (auto-install) |
| YAML | yaml-language-server | npm (auto-install) |
| Markdown | marksman | GitHub Releases |
| HTML/CSS | vscode-html/css-languageserver | npm (auto-install) |

LSP servers are downloaded lazily on first file open of that language and managed as child processes of the session's `nexcode-cli`. Node.js runtime is bundled with NexCode (see Section 37).

### 15.5 JSON Config File Editor

NexCode allows editing, saving, and reloading JSON config files within the GUI:
- MCP server connection configs
- MCP server definition files
- Custom mode files (`.nexmode`)
- Plugin manifests
- Environment variable files (`.env*`)
- Any arbitrary `.json` file in the project

Files are opened in Monaco with schema validation. Config panels (Plugins, MCP Servers, Modes) have **Edit JSON** buttons opening a side-sheet with Monaco editor, formatted preview, **Save**, **Reload**, and **Revert** buttons. Save emits `config.updated` IPC events triggering live reloads.

---

## 16. Git Manager

| Feature | Description |
|---|---|
| Status | Working tree status: staged, unstaged, untracked (color-coded) |
| Stage/Unstage | Per-file or per-hunk checkbox |
| Commit | Message input with emoji picker, one-click commit |
| Branch management | List, create, switch, merge, delete |
| Remote management | View, add, edit, remove remotes |
| Push/Pull/Fetch | One-click with progress |
| Log view | Commit graph with topology, messages, authors, dates |
| Stash | Create, view, apply, drop |
| Tag management | List, create (annotated/lightweight), push, delete |
| SSH Key Management | RSA 4096/Ed25519 via BouncyCastle. Stored encrypted in SQLCipher. Per-hostname assignment. `~/.ssh/config` auto-configuration. Test connection. |
| Credential store | Windows Credential Manager integration for HTTPS |
| Conflict resolution | Three-way Monaco diff editor |

---

## 17. MCP Client

NexCode implements the MCP client spec (2025-11-25, Agentic AI Foundation / Linux Foundation). Connects to multiple servers simultaneously over:
- **stdio** — local subprocess
- **HTTP/SSE** — remote servers (Streamable HTTP)
- **gRPC** — NexCode's own hosted servers

Features: auto-connect on startup, OAuth auth (MCP 2025-06-18 auth spec, PKCE), per-server per-tool permission overrides, exponential back-off reconnect, live status indicators.

---

## 18. MCP Server Creation & Marketplace

### 18.1 User-Created MCP Servers

From Plugins > Create MCP (Pro+). Wizard: name/version, transport type, tool/resource/prompt definitions. `nexcode-cli` generates TypeScript or Python scaffold using the official MCP SDK. Generated project opens in the built-in editor. Hosting: local stdio, local HTTP/SSE, or user-hosted (Docker guidance). JSON editor for server definition files (Section 15.5). Auto-connect support.

### 18.2 MCP Marketplace

WebView2-hosted web app (Plugins > Marketplace). Browse/search/filter; one-click install (signature-verified); publish (Pro+); update notifications. Offline/demo mode until backend is live.

> **Note:** Marketplace backend is a future service. Client-side is implemented with demo data and will activate when the endpoint constant `NEXCODE_MARKETPLACE_ENDPOINT` is configured.

---

## 19. Sub-Agent System

A parent session spawns sub-agents via `spawn_subagent` tool. Each sub-agent is an independent `nexcode-cli` process with its own session, context, plan, and tool set. Sub-agents:
- Inherit parent's permission level and sandbox flag (no escalation)
- Can themselves spawn sub-agents (up to configurable depth)
- Are monitored in the parent's Run tab with live status and kill buttons
- Have their own independent Implementation Plans and Todo Lists
- Forward `clarify_question` events to the parent session's message stream (labeled with sub-agent ID)

| Tier | Max Sub-Agents | Max Depth |
|---|---|---|
| Free | 0 | 0 |
| Pro | 3 | 2 |
| Team | 10 | 3 |
| Enterprise | Unlimited | 5 |
| SuperUser | Unlimited | Unlimited |

---

## 20. Keyboard Shortcuts & Keybinding Manager

### 20.1 Keybinding Manager UI

Found in Settings > Keyboard Shortcuts. Searchable table grouped by category. Per-row actions:
- **Edit** (pencil) — opens binding recorder dialog. Records modifier + key. Shows conflict warnings.
- **Remove** (X) — clears binding. System bindings cannot be fully removed (must have ≥1 binding).
- **Add secondary** (+) — records a second chord binding.
- **Reset to default** — restores factory binding.

Export as `.nexkeys` JSON / Import (merges with conflict resolution dialog).

Monaco Editor keybindings are synchronized with the KeyBindings table via the Monaco API.

### 20.2 Default Keybindings

| Action | Default Binding |
|---|---|
| New session | Ctrl+N |
| Close session | Ctrl+W |
| Previous session | Ctrl+Tab |
| Next session | Ctrl+Shift+Tab |
| Send message | Enter (Ctrl+Enter multi-line) |
| Cancel generation | Escape |
| Mode: Plan | Ctrl+1 |
| Mode: Code | Ctrl+2 |
| Mode: Debug | Ctrl+3 |
| Mode: Ask | Ctrl+4 |
| Toggle Column C | Ctrl+\ |
| Open terminal | Ctrl+` |
| Focus Git tab | Ctrl+G |
| Focus Diff tab | Ctrl+D |
| Focus Editor tab | Ctrl+E |
| Focus Plans tab | Ctrl+P |
| Toggle sandbox | Ctrl+Shift+S (confirmation required) |
| Toggle Full Access | Ctrl+Shift+A (confirmation required) |
| Open History | Ctrl+Shift+H |
| Open Memories | Ctrl+Shift+M |
| Open Plugins | Ctrl+Shift+P |
| Open Settings | Ctrl+, |
| Retry last turn | Ctrl+R |
| Edit last user message | Ctrl+Shift+E |
| Open Keybinding Manager | Ctrl+K Ctrl+S (chord) |
| Detach editor | Ctrl+Shift+D |
| Format document (editor) | Shift+Alt+F |
| Go to definition (editor) | F12 |
| Open model switcher | Ctrl+Shift+L |
| Show all diagnostics | Ctrl+Shift+M (editor focused) |
| Summon GUI (global hotkey, Background Service Mode) | Ctrl+Shift+N |

---

## 21. Integrated Terminal

ConPTY (Win32 P/Invoke) + VtNetCore rendering. Multiple tabs per project. Full 24-bit color. Configurable font (default: Cascadia Code) and line height. Scrollback: 10,000 lines (virtualized). Copy/paste: Ctrl+Shift+C/V. In-terminal search: Ctrl+Shift+F. Horizontal/vertical split panes. Shell selector per-tab from a dropdown in the tab header. Project-scoped terminal instances kept alive across project switches.

---

## 22. Plugins & Automations

### 22.1 Plugin System

Directory-based plugins with `nexusplugin.json` manifest. Run sandboxed (Job Object child process, stdio JSON-RPC hook protocol). Signature-verified before install. Hook types: `on_session_start`, `on_message_before_send`, `on_tool_call_before`, `on_tool_call_after`, `on_checkpoint`, `on_session_end`, `on_memory_write`, `on_plan_confirmed`, `on_todo_completed`.

### 22.2 Automations

Event-triggered workflows. Trigger types: `schedule` (cron), `file_change` (glob), `git_event`, `session_end`, `webhook`, `manual`. Step types: `run_session`, `run_command`, `send_notification`, `call_webhook`, `git_action`, `wait`, `conditional`.

---

## 23. Remote & Cloud Execution

- **Local** — `nexcode-cli` runs on user's machine. Default.
- **Remote** — `nexcode-cli` runs on user-hosted `nexcode-remote` server. GUI connects over mTLS gRPC with E2E AES-256-GCM per-session encryption (ECDH X25519) on top of TLS 1.3.
- **Cloud** — `nexcode-cli` runs on NexCode's managed cloud (Team+ subscription). Ephemeral containers, diff-based file sync.

Remote security: TLS 1.3 gRPC, mTLS client certificates, per-session E2E encryption overlay, MSAL JWT validation, allowed-OID list.

---

## 24. AI Provider Configuration

| Provider | Models | Auth |
|---|---|---|
| Anthropic | claude-opus-4, claude-sonnet-4, claude-haiku-4 | API Key |
| OpenAI | gpt-4.1, gpt-4.1-mini, o3, o4-mini | API Key or OAuth |
| Google Gemini | gemini-2.5-pro, gemini-2.5-flash | API Key or OAuth |
| AWS Bedrock | Claude on Bedrock, Titan, Llama | AWS credentials |
| Azure OpenAI | GPT-4 deployments | Azure subscription + key |
| Groq | llama-3.3-70b, mixtral-8x7b | API Key |
| OpenRouter | All OpenRouter models | API Key |
| Ollama | Any locally served model | Local endpoint |
| LM Studio | Any locally served model | Local endpoint |
| Custom OpenAI-compatible | Any compatible endpoint | Configurable |

All API keys stored encrypted (AES-256-GCM via SQLCipher). Test connection button per provider.

---

## 25. Session & History Management

Sessions: `Active`, `Idle`, `Archived`, `Deleted`. History panel: full-text search (SQLite FTS5), filter by project/date/mode/provider/personality, sort options. Per-session actions: Open, Archive, Unarchive, Export (JSON or Markdown), Rename, Delete, Permanently Delete. Auto-title generation after first response (5–7 word summary via secondary model call). Sessions persist personality, sandbox state, mode, execution mode, active plan, and model override on resume.

---

## 26. Permissions System

Two levels:
- **Default Access** — reads free; writes warned; destructive ops require per-call approval
- **Full Access** — all ops auto-approved; requires single consent dialog; persistent yellow lock indicator in input bar

Permission prompts appear as non-blocking cards in the message stream: **Allow Once**, **Allow for Session**, **Deny Once**, **Deny Always**. Sub-agents inherit parent level. Sandbox mode enforces OS-level constraints independently.

---

## 27. CI/CD Pipeline — GitHub Actions

### 27.1 Workflow Files

| File | Trigger | Jobs |
|---|---|---|
| `.github/workflows/ci.yml` | PR to main/develop | build-solution, run-tests, lint-code, validate-schemas |
| `.github/workflows/release.yml` | Push tag `v*.*.*` | build-solution, run-tests, sign-msix, create-github-release, upload-assets |
| `.github/workflows/pages.yml` | Push to main (docs/ changed) | build-jekyll, deploy-github-pages |
| `.github/workflows/dependency-update.yml` | Weekly (Monday 00:00 UTC) | nuget-update-check, npm-update-check, create-pr |
| `.github/workflows/codeql.yml` | Push to main + weekly | codeql-analyze (C#, JavaScript) |

### 27.2 MSIX Build & Sign Process

1. `actions/checkout` with full history
2. `actions/setup-dotnet` — .NET 9 SDK latest patch
3. `dotnet restore NexCode.sln`
4. `dotnet build NexCode.sln -c Release`
5. `dotnet test --no-build -c Release --logger trx`
6. `dotnet publish src/NexCode.Gui/NexCode.Gui.csproj -c Release -r win-x64 --self-contained`
7. `msbuild /t:Publish /p:Configuration=Release /p:AppxPackageDir=./msix-output/`
8. Sign using `signtool.exe` with PFX from `SIGNING_CERTIFICATE_PFX` secret (base64-decoded)
9. `softprops/action-gh-release` creates GitHub Release with signed `.msix`, standalone `.exe` installer, and SHA256 checksums

### 27.3 GitHub Actions Secrets Required

| Secret | Description |
|---|---|
| `SIGNING_CERTIFICATE_PFX` | Base64-encoded PFX for MSIX code signing |
| `SIGNING_CERTIFICATE_PASSWORD` | Password for PFX |
| `NEXCODE_AAD_CLIENT_ID` | Azure AD Application (client) ID for MSAL |
| `NEXCODE_AAD_TENANT_ID` | Azure AD tenant ID for MSAL |
| `NEXCODE_TELEMETRY_ENDPOINT` | Telemetry receiver URL (can be empty initially) |
| `NEXCODE_MARKETPLACE_ENDPOINT` | Marketplace backend URL (can be empty initially) |
| `STORE_PARTNER_CENTER_TENANT_ID` | Partner Center tenant ID (for Store submission) |
| `STORE_PARTNER_CENTER_CLIENT_ID` | Partner Center client ID |
| `STORE_PARTNER_CENTER_CLIENT_SECRET` | Partner Center client secret |

---

## 28. Legal Documents (GitHub Pages)

### 28.1 Overview

Microsoft Store requires publicly accessible Privacy Policy and Support URLs. NexCode hosts these on GitHub Pages at `https://<org>.github.io/nexcode/`. The `docs/` directory contains all legal documents deployed by `pages.yml`.

### 28.2 Required Documents

| Document | File | MS Store Field |
|---|---|---|
| Privacy Policy | `docs/privacy-policy.md` | Privacy policy URL (required) |
| Terms of Service | `docs/terms-of-service.md` | Support URL / Additional license terms |
| Terms and Conditions (EULA) | `docs/terms-and-conditions.md` | Additional license terms |
| Support / Contact | `docs/support.md` | Support contact URL (required) |
| Open Source Licenses | `docs/oss-licenses.md` | Acknowledgements |
| Changelog | `docs/changelog.md` | Referenced in release notes |
| Cookie Policy | `docs/cookie-policy.md` | Referenced from Privacy Policy |

### 28.3 Privacy Policy — Required Content (MS Store Policy 10.5.1 + GDPR/CCPA)

- What data is collected: AAD object ID/email, usage telemetry (if opted in), device info, crash diagnostics
- How collected: automatically on use (if opted in), user inputs
- Why collected: improve service, train AI models (opt-in), diagnose errors, enforce subscriptions
- Storage: locally encrypted (SQLCipher AES-256); telemetry transmitted to developer's servers when opted in
- Retention: local data until user deletes or uninstalls; telemetry up to 24 months
- Third-party sharing: AI provider APIs receive conversation content as part of normal operation. No sale of personal data.
- User rights: access, correct, delete (Settings > Privacy), opt out of telemetry at any time
- Children: not intended for users under 13
- Contact: developer email for privacy requests

### 28.4 Terms of Service — Required Content

Acceptance, subscription/billing (Microsoft Store IAP), license grant, user responsibilities, prohibited uses, intellectual property, warranty disclaimer (AS IS), limitation of liability, termination, governing law, 30-day notice for changes.

### 28.5 EULA — Required Content

License grant (personal, non-exclusive, non-transferable), restrictions (no reverse engineering, no redistribution), updates, telemetry disclosure with opt-out, open source component acknowledgement, feedback clause.

### 28.6 docs/ Structure

```
docs/
  _config.yml          # Jekyll config (site title, baseurl, theme)
  index.md             # Landing page linking to all documents
  privacy-policy.md
  terms-of-service.md
  terms-and-conditions.md
  support.md
  oss-licenses.md      # Auto-generated by dotnet-project-licenses in CI
  changelog.md
  cookie-policy.md
  assets/              # CSS, logo
```

---

## 29. System Tray & Background Service

### 29.1 Startup Modes

**Mode A — Foreground Application (Default):** Launches as standard WinUI 3 window. Close button prompts to quit or minimize to tray.

**Mode B — Background Service Mode:** Launches at Windows startup with no visible window. GUI summoned via: tray icon double-click, global hotkey (Ctrl+Shift+N, configurable), or toast notification click. Enabled by toggling "Start as Background Service" in Settings > General.

### 29.2 Tray States

| State | Appearance |
|---|---|
| Idle | NexCode logo, no badge |
| Active (local) | Logo + pulsing blue dot |
| Active (remote) | Logo + pulsing orange dot |
| Error | Logo + red X badge |
| Auth required | Logo + lock badge |
| Background Service Mode | Logo + gear badge |

**Context menu:** Open NexCode, New Session, Active Sessions submenu, Pause All, Resume All, Settings, Check for Updates, Quit.

---

## 30. Telemetry Service

> **IMPORTANT: This system is designed to collect data for AI model training. Implement exactly as specified.**

### 30.1 First-Login Consent

Full-screen consent sheet on first login. Explains data collection for model training. Large toggle defaulting to **OFF** (GDPR/CCPA compliant). "Learn More" links to Privacy Policy. Accept and Continue button. Shown only once. Stored in `Users.telemetry_enabled`.

### 30.2 Data Collected (When Enabled)

| Data Point | PII? |
|---|---|
| `session_id` (random UUID, not linked to email) | No |
| Mode name | No |
| Provider adapter type (not API key) | No |
| `model_id` | No |
| `message_role` (user/assistant) | No |
| `message_content` | Potentially |
| Tool calls (name + args + result) | Potentially |
| Checkpoint diff | Potentially |
| Plan titles and status transitions | Potentially |
| Clarify question prompts + answers | Potentially |
| `turn_latency_ms`, token counts | No |
| Permission prompt decisions | No |
| Plugin IDs enabled | No |
| Personality name used | No |
| Memory keys written (not values) | No |
| `app_version`, `os_version` | No |

### 30.3 Transmission

Events queued in `TelemetryQueue` (SQLCipher-encrypted). Transmission: on startup (if queued), every 6 hours, on session end. Rate limited to 50 KB/s. Acknowledged events deleted. Failed events retried with exponential back-off (max 7 days). Endpoint: `NEXCODE_TELEMETRY_ENDPOINT` injected at build time. Empty = no transmission, queue accumulates.

---

## 31. Environment Management

Each project has named `EnvironmentConfigs`: name (e.g., Development, Staging), `env_vars` (key-value pairs, secret values encrypted in SQLCipher), provider/model override, working directory override, `is_default` flag. nexcode-cli auto-loads `.env` files from project root (dotenv convention). GUI `.env` editor with masked values, validation, sync-to-file. Environment selector dropdown in session input bar.

---

## 32. Settings Panels

| Panel | Contents |
|---|---|
| General | Startup mode (Foreground/Background Service), default project, default mode, default shell, language, date format, auto-update channel, min window size override |
| Account | Logged-in user info, subscription details (from MS Store IAP), Restore Purchases button, sign out, super-user developer panel (super-user only) |
| Providers | AI provider list: add/edit/remove, API key management, test connection, default provider |
| Modes | Built-in + custom modes: enable/disable, edit (JSON editor), duplicate, delete, import/export `.nexmode` |
| Personalities | Personality grid: add, edit, duplicate, delete, set default, import/export `.nexpers` |
| Memory | Global/project memories, relevance injection settings, auto-eviction, max memories, export/import |
| Appearance | Theme selector, theme editor (color picker per semantic key), font size (chat), font family (editor/terminal), message density |
| Keyboard Shortcuts | Full keybinding manager: view, add, remove, remap, export, import, reset to defaults |
| Notifications | Per-category notification toggles |
| SSH Keys | Key generator (RSA 4096/Ed25519), import, assign to host, test, delete |
| MCP Servers | List, status, edit (JSON editor), test, delete, add |
| Plugins | Installed plugins: enable/disable, permissions, update, uninstall, install from file |
| Automations | List: run, enable/disable, edit, delete |
| Environments | Per-project environment configs: add, edit (`.env` editor), delete, set active |
| Editor | Auto-save settings, tab size, insert-spaces per language, JSON schema registrations, LSP server management |
| Privacy | Telemetry toggle, queue size, view sample payload, clear queue, data export (GDPR), account deletion request |
| Shell | Default shell, startup args, test shell button |
| Plans & Todos | Default plan auto-present behavior, todo auto-start-on-confirm, plans retention policy |
| About | Version info, links to Privacy Policy, ToS, T&C (GitHub Pages), OSS Licenses, Changelog |
| Advanced | Debug log viewer, IPC inspector, DB vacuum, reset to defaults (two-step confirmation) |

---

## 33. System Integration & Backward Compatibility

| Windows Version | Support Level | Notes |
|---|---|---|
| Windows 11 (22H2+) | Full | Primary target |
| Windows 11 (21H2) | Full | Minimum for Windows App SDK 1.7+ |
| Windows 10 (22H2) | Full | Requires Windows App SDK bootstrap |
| Windows 10 (20H2–21H2) | Partial | ConPTY limited; terminal fallback renderer |
| Windows 10 (1903–20H1) | Best effort | Some features disabled |
| Windows 10 (pre-1903) | Not supported | Upgrade notice; read-only history access |

**Permission Gated Pop-Ups:**

| Permission | When |
|---|---|
| File system (broad access) | First project root selection |
| Clipboard read | First agent clipboard attempt |
| Startup entry | First launch with startup enabled |
| Toast notifications | First notification event |
| Network (localhost) | First remote/gRPC connection |

---

## 34. Multi-Project Management

Projects map to directories on disk. Operations: Create (optional git init, optional template), Import (detect existing git/env/language), Switch (background processes continue), Rename, Archive, Remove, Settings. Each project can have an `AGENTS.md` file injected into session system prompts. `/init` command generates or updates `AGENTS.md`. Project list is virtualized (`IncrementalLoadingCollection`).

---

## 35. Error Handling & Logging

| Category | Recovery | Notification |
|---|---|---|
| Transient network (provider API) | Auto-retry x3, exponential back-off | Inline spinner with retry count |
| Rate limit (429) | Back off per Retry-After header | Yellow warning card |
| Invalid API key | Surface immediately; no retry | Red error card + Settings link |
| CLI process crash | Crash report; offer restart | Toast + error card |
| Git failure | Show libgit2 error in Git panel | Inline error |
| MCP server disconnected | Auto-reconnect x3; manual button | Orange badge + toast |
| Plan confirmation timeout | AI treats as "awaiting" and pauses | Plan Card shows "Waiting for confirmation..." |
| Clarify question timeout (60s) | AI cancels and proceeds with assumptions | Card shows "Timed out — AI used assumptions" |
| LSP server crash | Restart LSP server | Yellow info banner in editor |
| DB migration failure | Restore from backup; alert user | Full-screen modal with backup path |
| IAP validation failure (offline) | Use cached tier; show banner | Yellow banner: "Subscription status unverified" |

**Logging:** Microsoft.Extensions.Logging + Serilog.Sinks.File. Location: `%LOCALAPPDATA%\NexCode\logs\`. Daily rotation, 30 days, 50 MB/file. Default level: Information. Debug/Trace via Settings > Advanced > Debug Mode.

---

## 36. Security Overview

- **Credentials:** All API keys, OAuth tokens, SSH private keys stored in SQLCipher-encrypted SQLite. Key derived from DPAPI + Windows Credential Manager salt. Cannot be decrypted on another machine.
- **Process isolation:** Each `nexcode-cli` worker runs under user account (no elevation). Windows Job Objects restrict child process creation.
- **Plugins:** Sandboxed child process + Job Object. Signature-verified before install.
- **Sandbox mode:** Additional file, network, process, registry restrictions (Section 14).
- **Prompt injection mitigation:** Tool arguments validated against JSON Schema before execution. MCP tool descriptions sanitized before display. Human-in-the-loop via permission system.
- **Plan confirmation gate:** The AI cannot begin executing a plan's tasks without a confirmed `status = confirmed` in the `ImplementationPlans` table. The CLI enforces this: `create_todo_list` will fail with a `plan_not_confirmed` error if called before any plan is confirmed for the session.

---

## 37. Self-Contained Distribution

**NexCode ships as a single MSIX package with zero external dependencies required from the user.** All runtimes and tools are bundled.

| Bundled Component | Location in Package | Notes |
|---|---|---|
| .NET 9 Runtime | `runtimes/win-x64/` | Published as self-contained (`--self-contained true`). No .NET install required. |
| WebView2 Fixed Version Runtime | `WebView2Runtime/` | Uses the fixed/standalone WebView2 distribution (not Evergreen). Version pinned and bundled. No separate WebView2 install required. |
| Monaco Editor assets | `Assets/Monaco/` | Full Monaco bundle (JS, CSS, workers) copied locally. No CDN. |
| Cascadia Code font | `Assets/Fonts/` | Bundled and registered by the app |
| Node.js runtime | `Assets/NodeRuntime/` | LTS version bundled for LSP server process management (typescript-language-server, yaml-language-server, etc.) |
| ConPTY binaries | Inbox Windows (Win10 1903+) | No bundle needed for modern Windows; legacy fallback terminal provided for older |
| SQLCipher | NuGet `SQLitePCLRaw.bundle_sqlcipher` | Published as NativeLibrary, included in self-contained publish output |
| LibGit2 native | NuGet `LibGit2Sharp.NativeBinaries` | Included in self-contained publish output |
| BouncyCastle | Pure managed .NET | No native dependency |
| VtNetCore | Pure managed .NET | No native dependency |

**MSIX packaging configuration** (`NexCode.Gui.wapproj`):
```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>false</PublishSingleFile>
<!-- Single file is NOT used because MSIX packaging handles the layout -->
```

**Installer fallback:** For non-Store distribution, the standalone Inno Setup installer (`scripts/installer/NexCode-Setup.iss`) bundles the same self-contained output and installs to `%ProgramFiles%\NexCode\`. No prerequisites page.

**Node.js management:** The bundled Node.js runtime is used exclusively for running LSP servers and MCP server processes. It is stored in `%LOCALAPPDATA%\NexCode\NodeRuntime\` on first run (extracted from the MSIX package assets). LSP server npm packages are installed on first language use to `%LOCALAPPDATA%\NexCode\LspServers\<language>\`.

---

## 38. Repository & Project Structure

```
NexCode.sln
src/
  NexCode.Gui/                    # WinUI 3 application project
    Views/                        # XAML pages and controls
    ViewModels/                   # MVVM ViewModels (CommunityToolkit.Mvvm)
    Controls/                     # VirtualMessageList, TerminalView, MonacoHost,
                                  #   PlanArtifactCard, TodoArtifactCard,
                                  #   ClarifyQuestionCard, ModelSwitcherDropdown,
                                  #   JsonEditorSheet
    Services/                     # ThemeService, IpcClientService,
                                  #   NavigationService, KeyBindingService,
                                  #   IAPService, PlanService, TodoService
    Assets/
      Monaco/                     # Bundled Monaco Editor
      NodeRuntime/                # Bundled Node.js LTS
      Fonts/                      # Cascadia Code
      WebView2Runtime/            # Fixed WebView2 distribution
  NexCode.Service/                # .NET Worker Service project
  NexCode.Cli/
    Agents/                       # AgentLoop, SubAgentManager,
                                  #   PermissionManager, SandboxManager
    Tools/                        # All built-in tool implementations
                                  #   including CutPasteFileTool,
                                  #   PlanTools, TodoTools, ClarifyTool
    Providers/                    # ProviderAdapter implementations
    Mcp/                          # MCP client and server
    Git/                          # LibGit2Sharp wrapper + SSH
    Terminal/                     # ConPTY backend + shell selector
    Memory/                       # MemoryEngine: read, write, relevance
    Lsp/                          # LSP client, server manager, diagnostics
    Plans/                        # PlanManager, TodoManager
    Clarify/                      # ClarifyEngine: question emit, answer wait
  NexCode.Remote/                 # ASP.NET Core 9 gRPC remote server
  NexCode.Shared/                 # DTOs, IPC event schemas, constants
  NexCode.Data/                   # EF Core DbContext (SQLCipher), migrations
  NexCode.Marketplace.Sdk/        # SDK for marketplace development
tests/
  NexCode.Cli.Tests/              # CLI unit + integration tests
  NexCode.Gui.Tests/              # WinAppDriver / Appium UI automation tests
  NexCode.Data.Tests/             # DB migration and query tests
  NexCode.Plans.Tests/            # Plan/Todo system unit tests
  NexCode.Clarify.Tests/          # MCQ module unit tests
docs/
  _config.yml                     # Jekyll config
  index.md
  privacy-policy.md
  terms-of-service.md
  terms-and-conditions.md
  support.md
  oss-licenses.md                 # Auto-generated in CI
  changelog.md
  cookie-policy.md
  assets/
scripts/
  installer/
    NexCode-Setup.iss             # Inno Setup standalone installer script
  build/
    Sign-Msix.ps1                 # PowerShell signing helper
    Generate-OssLicenses.ps1     # dotnet-project-licenses runner
.github/
  workflows/
    ci.yml
    release.yml
    pages.yml
    dependency-update.yml
    codeql.yml
```

---

## 39. Implementation Roadmap

### Phase 1 — Foundation (Weeks 1–4)
- Solution structure, CI/CD skeleton (ci.yml, release.yml, pages.yml), code-signing cert setup
- `nexcode-service`: IPC named pipe hub, startup registration, system tray (Modes A and B)
- `nexcode-cli` core: arg parsing, single-turn non-streaming prompt (Anthropic), SQLCipher DB setup
- EF Core data layer: all tables from Section 2.3 + migrations
- MSAL SSO GUI: login modal, token cache, super-user override
- Microsoft Store IAP: `IAPService`, subscription gate bottom sheet, Restore Purchases
- Application shell: 3-column layout, navigation rail, 800×600 minimum enforcement
- `docs/` directory: initial legal documents. GitHub Pages workflow.

### Phase 2 — Core Agent Experience (Weeks 5–10)
- Streaming agent loop: all built-in tools (including `cut_paste_file`)
- Virtualized message list + checkpoint cards
- Monaco Editor integration: WebView2 host, diff view, JSON editor
- Checkpoint, revert, Continue From Here, Retry at any turn
- Permission prompt cards
- Windows shell integration: PowerShell 7 / CMD / WSL / Custom shell selector
- Embedded terminal: ConPTY + VtNetCore, multi-tab, shell selector per tab
- All four built-in modes. Model Switcher dropdown.
- Subscription IAP gating.

### Phase 3 — Plans, Todos & Questions (Weeks 11–16)
- **Implementation Plan system:** `create_plan` / `update_plan` / `delete_plan` tools, `ImplementationPlans` table, Plan Artifact Card, plan confirmation flow, Plans tab in Column C
- **Todo List system:** all todo tools, `TodoLists`/`TodoItems` tables, Todo Artifact Card with live updates, Plans tab integration
- **Clarifying Questions module:** `clarify_question` tool, `ClarifyQuestions` tables, Clarify Question Card with MCQ buttons + custom text, CLI tabbed UI, anti-spam enforcement
- Plan version history sheet
- Sub-agent plan isolation

### Phase 4 — Extensibility & Intelligence (Weeks 17–22)
- Model Personalities: built-in personalities, custom editor, per-session selection
- AI Memory System: memory store, tools, relevance injection, Memory panel
- Sandbox mode: Job Object constraints, per-session toggle, sub-agent inheritance
- MCP client: stdio + SSE transports, OAuth
- Plugins system: manifest loader, sandboxed runner, hook dispatching (including `on_plan_confirmed`, `on_todo_completed`)
- Custom mode editor. Git Manager with SSH.
- Multi-provider support. Automations engine. History panel with FTS.

### Phase 5 — Advanced Features (Weeks 23–28)
- LSP linter bridge: LSP client, per-language server auto-download (using bundled Node.js), Monaco markers
- Built-in code editor: full Monaco features, file tabs, split panes, detached window, auto-save
- JSON config file editor integration
- Sub-agent system: spawn, manage, kill, permission + sandbox inheritance
- Remote execution: `nexcode-remote` gRPC server, mTLS, E2E encryption
- MCP server creation wizard and scaffolding. Marketplace client (demo mode).
- Telemetry service: queue, background transmission, consent screen.
- Theme editor. Environment management. Background Service Mode.
- Self-contained packaging: bundle Node.js, Monaco, fixed WebView2 runtime, all native deps.

### Phase 6 — Polish & Ship (Weeks 29–32)
- Full accessibility audit (keyboard navigation, Narrator, High Contrast)
- Performance: cold start < 3s, message rendering 60 fps
- Complete MSIX + Inno Setup standalone installer
- OSS license auto-generation in CI (`dotnet-project-licenses`)
- End-to-end test suite (WinAppDriver)
- Microsoft Store submission prep

---

## 40. Prompting Guide — How to Use This Document with an AI Coder

This section is written **for you** — the developer — explaining exactly how to hand this specification to an AI coding assistant (OpenAI Codex, Claude Code, Google Antigravity, or similar) to implement NexCode.

### 40.1 Why Prompting Strategy Matters Here

This document describes a large, complex system. An AI coder cannot implement all of it in one shot. You must decompose the work into phases and sections, give the AI the right slice of context for each task, and use the AI's own plan/todo mechanism (ironic, but true) to track progress.

### 40.2 How to Load This Document

**Option A — File reference (recommended for tools that support it):**
```
@NexCode_TechSpec_v1.0.md

Implement [specific section/task].
```
Tools like Claude Code, OpenAI Codex CLI, and Google Antigravity support `@file` references to load large context files. The AI will read the full spec before answering.

**Option B — Paste the relevant section:**
For each coding task, paste only the relevant section(s) from this document. Example: if implementing the Plan system, paste Section 10 in full. This reduces context window usage.

**Option C — AGENTS.md (for repository-level context):**
Create an `AGENTS.md` in the repo root that summarizes the architecture and points to this spec:
```markdown
# NexCode — Agent Context

This is the NexCode agentic coding platform. Full specification: `NexCode_TechSpec_v1.0.md`.

Architecture:
- nexcode-gui: WinUI 3 thin shell (src/NexCode.Gui/)
- nexcode-service: background Worker Service (src/NexCode.Service/)
- nexcode-cli: core AI engine (src/NexCode.Cli/)
- nexcode-remote: gRPC remote server (src/NexCode.Remote/)
- Database: SQLite + SQLCipher, EF Core, all schemas in spec Section 2.3

Key rules:
- GUI never calls AI APIs directly — all AI calls go through nexcode-cli
- All secrets stored in SQLCipher-encrypted SQLite
- Minimum window size 800×600 enforced in MainWindow constructor
- Super-user access: sealed local grant only — never expose the privileged account identifier in public source
- All packages use latest stable NuGet versions
- Self-contained publish: bundle .NET 9, fixed WebView2, bundled Node.js, Monaco

Current phase: [update this as you progress through the roadmap]
```

### 40.3 Phased Prompting Strategy

Use the roadmap in Section 39 as your work order. For each phase, use this prompt structure:

```
You are implementing NexCode, a WinUI 3 agentic AI coding platform.
Reference: @NexCode_TechSpec_v1.0.md

Current task: [Phase X, specific feature from the roadmap]

Relevant spec sections: [list section numbers]

Context:
- What's already built: [describe completed components]
- What I need now: [specific deliverable]
- Constraints: [any specific notes, e.g., "do not use Newtonsoft", "use latest NuGet"]

Please:
1. Create an Implementation Plan for this task
2. Wait for my confirmation before writing code
3. After I confirm, create a Todo List and execute tasks one by one
4. Mark each task done as you complete it

Start by asking any clarifying questions you have (max 3 questions).
```

This prompt mirrors NexCode's own workflow — the AI you're using to build NexCode should behave the same way NexCode will work.

### 40.4 Section-by-Section Prompting Guide

| What to build | Sections to provide | Example prompt focus |
|---|---|---|
| Project structure + CI | Sections 38, 27 | "Create the solution structure and all GitHub Actions workflows" |
| SQLite + SQLCipher DB | Section 2.3 | "Set up EF Core with SQLCipher. All tables from the schema. Encryption key derivation as described." |
| MSAL Auth + IAP | Section 3 | "Implement MSAL SSO login flow AND Microsoft Store IAP. Super-user override via a sealed local grant." |
| GUI shell + navigation | Section 4.3, 4.4 | "3-column WinUI 3 shell. Virtualized project list. Minimum 800×600." |
| Agent loop + tools | Section 5.3, 5.4 | "Full streaming agent loop. All built-in tools including cut_paste_file." |
| Plan system | Section 10 | "Complete implementation plan and todo system as described in Section 10." |
| MCQ clarify module | Section 11 | "clarify_question tool, ClarifyQuestionCard GUI component, CLI tabbed UI." |
| Model switcher | Section 12 | "Model switcher dropdown in session header, /model CLI command." |
| Memory system | Section 9 | "Memory store, tools, relevance injection engine, Memory panel." |
| Personalities | Section 8 | "Personality system, built-in personalities, custom editor, per-session selection." |
| Monaco editor | Section 15 | "Bundle Monaco locally. LSP bridge for TypeScript and Python first. JSON editor." |
| Git manager | Section 16 | "Full Git Manager using LibGit2Sharp. SSH key management with BouncyCastle." |
| Terminal | Section 21, 6 | "ConPTY terminal with PowerShell/CMD/WSL shell selector." |
| MCP client | Section 17 | "Full MCP client spec 2025-11-25. stdio and SSE transports." |
| Sandbox mode | Section 14 | "Windows Job Object sandbox per session." |
| Legal docs | Section 28 | "Generate all docs/ legal documents. Privacy policy, ToS, EULA." |
| Self-contained packaging | Section 37 | "MSIX self-contained build. Bundle .NET 9, WebView2 fixed, Node.js, Monaco." |

### 40.5 Tips for Best Results

1. **Always ask for a plan first.** Start every session with "Create an implementation plan before writing any code."
2. **One section at a time.** Don't ask the AI to implement Sections 10, 11, and 12 in a single prompt. Do them separately.
3. **Reference the existing code.** Once you have code, use `@<file>` references to give the AI the files it needs to modify.
4. **Use the AI's clarify feature.** If the AI asks clarifying questions (as it should per this spec), answer them completely.
5. **Commit after each section.** This gives the AI clean git history to diff against and prevents context contamination between sections.
6. **Test after each phase** before moving to the next. Tell the AI: "Write unit tests for [component] before we move on."
7. **Update AGENTS.md** after each phase to keep the context fresh.

---

## 41. Manual Setup Steps (What You Must Do Yourself)

These are tasks that cannot be automated or done by an AI coder. You must perform them manually, in order.

---

### Step 1 — Create Microsoft Azure AD Application (for MSAL SSO)

1. Go to [portal.azure.com](https://portal.azure.com) and sign in with your Microsoft account (avhishe.adhikary11@gmail.com).
2. Navigate to **Azure Active Directory** → **App registrations** → **New registration**.
3. Fill in:
   - **Name:** `NexCode`
   - **Supported account types:** "Accounts in any organizational directory and personal Microsoft accounts"
   - **Redirect URI:** Platform = "Public client/native (mobile & desktop)", URI = `https://login.microsoftonline.com/common/oauth2/nativeclient`
4. Click **Register**.
5. On the app overview page, copy:
   - **Application (client) ID** → save as `NEXCODE_AAD_CLIENT_ID` (GitHub Actions secret)
   - **Directory (tenant) ID** → save as `NEXCODE_AAD_TENANT_ID` (GitHub Actions secret)
6. Go to **Authentication** in the left pane. Under "Advanced settings", set "Allow public client flows" to **Yes**. Save.
7. Go to **API permissions** → **Add a permission** → **Microsoft Graph** → **Delegated** → add `User.Read`. Click **Grant admin consent**.
8. Add the `Application (client) ID` and `Directory (tenant) ID` as GitHub Actions repository secrets.

---

### Step 2 — Get a Code Signing Certificate (for MSIX)

**Option A — Self-signed (for development/testing only, not for Store):**
```powershell
New-SelfSignedCertificate -Type CodeSigning -Subject "CN=NexCode Dev" -KeyUsage DigitalSignature -FriendlyName "NexCode Dev Cert" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
```

**Option B — Trusted certificate (required for non-Store distribution):**
1. Purchase a code signing certificate from DigiCert, Sectigo, or GlobalSign. Cost: ~$200–$500/year.
2. Complete identity verification (takes 1–3 business days).
3. Download the certificate as a `.pfx` file.
4. Base64-encode it: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))` in PowerShell.
5. Add to GitHub Actions secrets:
   - `SIGNING_CERTIFICATE_PFX` = the base64 string
   - `SIGNING_CERTIFICATE_PASSWORD` = your PFX password

**Option C — Microsoft Store certificate (for Store distribution):**
You do not need a separate certificate. The Microsoft Store automatically re-signs MSIX packages with Microsoft's trusted certificate during Store ingestion. For Store submission, you can use a self-signed cert for CI builds and let the Store handle signing.

---

### Step 3 — Create Microsoft Partner Center Account (for Microsoft Store)

1. Go to [partner.microsoft.com/dashboard](https://partner.microsoft.com/dashboard) and sign in with your Microsoft account.
2. Enroll as an **Individual developer**. Required info: legal name, address, tax information (W-8 BEN for non-US, W-9 for US), payment method. Fee: **$19 one-time registration** for individuals.
3. Wait for account approval (1–3 business days, sometimes instant).
4. Once approved, you are in the Partner Center dashboard.

---

### Step 4 — Create the NexCode App Listing in Partner Center

1. In Partner Center dashboard → **Apps and games** → **New product** → **App**.
2. App name: **NexCode**. Check availability. Reserve the name.
3. You are now in the app's submission dashboard. Note the **Store ID** (a 12-character alphanumeric string, e.g., `9NXXXXXXXX`). You will need this in the MSIX manifest's `<mp:PhoneIdentity>` and the Package Identity section.
4. Go to **Product setup** → fill in categories (Productivity, Developer tools), support URL (`https://<org>.github.io/nexcode/support`), privacy policy URL (`https://<org>.github.io/nexcode/privacy-policy`).
5. Do not submit yet — you need to configure IAP add-ons first (Step 5).

---

### Step 5 — Create In-App Purchase Add-On Products in Partner Center

For each product in the table in Section 3.2.1, do the following:

1. In your app's Partner Center dashboard → **Add-ons** → **Create a new add-on**.
2. **Product type:** Select **"Subscription"** (for recurring billing) or **"Durable"** (for permanent purchase). For NexCode, use **"Subscription"** for all monthly/annual plans.
3. **Product ID:** Enter exactly the product IDs from Section 3.2.1 (e.g., `nexcode_pro_monthly`). This ID is what your code queries with `storeContext.RequestPurchaseAsync("nexcode_pro_monthly")`.
4. Click **Create**.
5. For each add-on, fill in:
   - **Display name:** e.g., "NexCode Pro — Monthly"
   - **Description:** brief description of what's included
   - **Subscription period:** Monthly or Annual
   - **Free trial period:** optional (7 days recommended for Pro)
   - **Base price:** set your pricing tier in each market
6. Repeat for all 6 add-on products.
7. Save all add-ons (do not submit yet — submit only when the main app is ready).

---

### Step 6 — Configure the MSIX Package Identity to Match Partner Center

1. In Partner Center → your app → **Product management** → **Product identity**.
2. Note the values:
   - **Package/Identity/Name** (e.g., `12345YourName.NexCode`)
   - **Package/Identity/Publisher** (e.g., `CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX`)
3. Open `src/NexCode.Gui/Package.appxmanifest` in your solution.
4. Update the `<Identity>` element:
   ```xml
   <Identity
     Name="12345YourName.NexCode"
     Publisher="CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX"
     Version="1.0.0.0" />
   ```
5. These values **must exactly match** Partner Center. If they don't, the Store will reject your package.

---

### Step 7 — Configure GitHub Actions Secrets

Go to your GitHub repository → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**. Add all secrets from Section 27.3:

- `SIGNING_CERTIFICATE_PFX`
- `SIGNING_CERTIFICATE_PASSWORD`
- `NEXCODE_AAD_CLIENT_ID` (from Step 1)
- `NEXCODE_AAD_TENANT_ID` (from Step 1)
- `NEXCODE_TELEMETRY_ENDPOINT` (leave empty for now — add later)
- `NEXCODE_MARKETPLACE_ENDPOINT` (leave empty for now)
- `STORE_PARTNER_CENTER_TENANT_ID` (from Partner Center API, see Step 8)
- `STORE_PARTNER_CENTER_CLIENT_ID` (from Partner Center API)
- `STORE_PARTNER_CENTER_CLIENT_SECRET` (from Partner Center API)

---

### Step 8 — Configure Partner Center API Access (for Automated Store Submission)

This is optional but recommended for automated CI/CD store submissions.

1. In Partner Center → **Account settings** → **API access** → **Create Azure AD application**.
2. Link your Azure AD tenant from Step 1.
3. Under the tenant, create an Azure AD app registration specifically for Partner Center API access (separate from the MSAL SSO app).
4. In Azure AD → the new app → **Certificates & secrets** → create a client secret. Copy it immediately.
5. In Partner Center → assign the Azure AD app the **Manager** role for your account.
6. Copy: **Tenant ID** → `STORE_PARTNER_CENTER_TENANT_ID`, **Application (client) ID** → `STORE_PARTNER_CENTER_CLIENT_ID`, **Client secret** → `STORE_PARTNER_CENTER_CLIENT_SECRET`.
7. Add all three as GitHub Actions secrets.

---

### Step 9 — Set Up GitHub Pages

1. Go to your GitHub repository → **Settings** → **Pages**.
2. Under **Source**, select **GitHub Actions** (not a branch — this lets the `pages.yml` workflow control deployment).
3. The first time `pages.yml` runs after a push to `main`, it will deploy the `docs/` directory to `https://<org>.github.io/<repo>/`.
4. Verify the URLs work (Privacy Policy, ToS, etc.) before submitting to the Store.
5. Go back to Partner Center and fill in the Privacy Policy URL and Support URL with the live GitHub Pages URLs.

---

### Step 10 — Configure the App's Store Association in Visual Studio

1. Open the solution in Visual Studio 2022 (latest preview).
2. Right-click the `NexCode.Gui (Package)` project → **Publish** → **Associate App with the Store**.
3. Sign in with your Microsoft account. Select the **NexCode** app you created in Step 4.
4. Visual Studio will update `Package.appxmanifest` with the correct identity. Commit these changes.

---

### Step 11 — First Store Submission

1. Build and sign the MSIX via the GitHub Actions `release.yml` by pushing a tag: `git tag v1.0.0 && git push origin v1.0.0`.
2. Download the signed `.msix` from the GitHub Release.
3. In Partner Center → your app → **Start a new submission**.
4. Upload the `.msix` package.
5. Fill in: store listing (screenshots, description), pricing (Free with IAP), age rating questionnaire, content declarations.
6. Submit for certification. Microsoft review typically takes 1–3 business days.

---

### Step 12 — Set Up Telemetry Receiver (When Ready)

When you are ready to receive telemetry data:
1. Create a simple REST endpoint (Azure Function, Cloudflare Worker, or any HTTP server) that accepts `POST /telemetry` with a JSON body array of telemetry events.
2. Store events in a database of your choice.
3. Update the `NEXCODE_TELEMETRY_ENDPOINT` GitHub Actions secret with your endpoint URL.
4. The next release build will inject this URL, and the app will start transmitting queued events to it.

---

## 42. Appendices

### Appendix A — Database Encryption Key Derivation (Detail)

1. On first launch, generate 32-byte random salt via `RandomNumberGenerator.Fill(salt)`. Store in Windows Credential Manager as `NexCode_DbSalt_<InstallId>`, DPAPI-protected.
2. Retrieve DPAPI key: `ProtectedData.Protect(userData, entropy, DataProtectionScope.CurrentUser)` where entropy = `Encoding.UTF8.GetBytes("NexCode.DbKeyEntropy.v1")`.
3. Derive SQLCipher key: `key = PBKDF2-SHA256(dpapi_key, salt, 300000, 32)`.
4. Pass to SQLCipher: `PRAGMA key = 'x"<hex_key>"';` on every connection open. Never written to disk.

### Appendix B — cut_paste_file Atomicity

1. Write updated source content to `<source_file>.nexcode_tmp`.
2. Write updated destination content to `<destination_file>.nexcode_tmp`.
3. `MoveFileEx(source_tmp → source, MOVEFILE_REPLACE_EXISTING)` — atomic on same volume.
4. `MoveFileEx(dest_tmp → destination, MOVEFILE_REPLACE_EXISTING)` — atomic.
5. Cross-volume fallback: copy then delete with warning event.
6. Crash recovery: `nexcode-cli` checks for `.nexcode_tmp` files on startup and completes or rolls back.

### Appendix C — Plan Status State Machine

```
                    [create_plan]
                         ↓
                      draft
                         ↓  [auto_present = true]
               pending_confirmation
               ↙                  ↘
          confirmed             rejected
              ↓                     ↓
         [todos created]    [AI creates new plan]
              ↓
          completed
```

### Appendix D — Clarify Question Anti-Spam Enforcement

The anti-spam rule (max 1 `clarify_question` call per turn, only for fundamental ambiguity) is enforced at two levels:

1. **System prompt level:** The rule is injected into every session's system prompt automatically by `AgentLoop.BuildSystemPrompt()`. It cannot be removed by user-defined mode system prompts — it is appended after.
2. **Runtime level:** `ClarifyEngine` tracks `clarify_question` calls per turn. If a second call is made within the same turn, it returns an error to the model: `{ error: "clarify_question_already_called_this_turn", message: "You already asked a clarifying question this turn. Proceed with reasonable assumptions." }`.

### Appendix E — IPC Event Constants (Complete)

| Constant | Direction | Section |
|---|---|---|
| `token` | CLI → Service → GUI | 5.5 |
| `tool_call` / `tool_result` | CLI → Service → GUI | 5.5 |
| `permission_request` / `permission_response` | CLI ↔ GUI | 26 |
| `clarify.question` / `clarify.respond` | CLI ↔ GUI | 11 |
| `plan.updated` | CLI → Service → GUI | 10 |
| `todo.updated` | CLI → Service → GUI | 10 |
| `checkpoint` | CLI → Service → GUI | 13 |
| `memory_updated` | CLI → Service → GUI | 9 |
| `session_start` / `session_end` | CLI → Service → GUI | 5.5 |
| `agent_spawned` / `agent_ended` | CLI → Service → GUI | 19 |
| `linter_diagnostic` | CLI → Service → GUI | 15 |
| `error` / `status` / `progress` | CLI → Service → GUI | 5.5 |
| `session.create` / `session.send_message` / `session.cancel` | GUI → Service | 2.2 |
| `plan.confirm` / `plan.reject` | GUI → Service | 10 |
| `memory.get` / `memory.set` | GUI → Service | 9 |
| `session.checkpoint_reverted` | Service → GUI | 13 |
| `file.changed` / `config.updated` | Service → GUI | 15 |
| `service.auth_required` / `service.auth_success` | Service → GUI | 3 |
| `service.iap_updated` | Service → GUI | 3 |
| `service.health` | Service → GUI | 2.2 |

### Appendix F — Glossary

| Term | Definition |
|---|---|
| Agent | A running `nexcode-cli` instance executing an AI-driven task in a session |
| Checkpoint | Snapshot of file changes (git commit + diff) at the end of one agent turn |
| CLI | Command-Line Interface — `nexcode-cli`, the core AI engine |
| Clarify Question | Structured MCQ question asked by the AI when facing fundamental ambiguity |
| ConPTY | Console Pseudo Terminal — Windows API for hosting console apps in other processes |
| `cut_paste_file` | Custom NexCode tool that atomically moves a line range from one file to another |
| DPAPI | Data Protection API — Windows API for user-scoped encryption of secrets |
| IAP | In-App Purchase — Microsoft Store subscription billing |
| IPC | Inter-Process Communication — named pipe JSON-RPC between service and GUI |
| Implementation Plan | AI-generated structured plan document for a thread; must be confirmed before Todo List creation |
| MCP | Model Context Protocol — open standard (Agentic AI Foundation / Linux Foundation) |
| Memory | Persistent key-value pair in the AI Memory store, accessible across sessions |
| Mode | Named config defining agent system prompt, tools, and constraints |
| mTLS | Mutual TLS — bidirectional certificate authentication |
| Personality | Named behavioral profile injected before Mode prompt to adjust communication style |
| Plugin | Extension package adding tools, hooks, or UI panels. Runs sandboxed. |
| Provider | AI model service (Anthropic, OpenAI, etc.) configured with credentials |
| Sandbox | Execution isolation via Windows Job Object — restricts file, network, process access |
| Session | A single conversation thread between user and agent, within a project |
| SQLCipher | AES-256 page-level encryption extension for SQLite |
| Sub-agent | Child agent spawned by a parent agent for parallel or delegated tasks |
| Todo List | AI-managed task checklist generated from a confirmed Implementation Plan |
| Turn | One round: user message → agent processing → assistant response (all tool calls) |
| WinUI 3 | Windows UI Library 3 — native Windows UI framework via Windows App SDK |

---

*© 2026 NexCode — avhishe.adhikary11@gmail.com*
*Version 1.0 — April 2026 — CONFIDENTIAL*
