# NexCode Architecture Decisions

These decisions clarify contradictions or under-specified areas in `plan.md`. The spec remains authoritative; this file only resolves implementation-critical gaps.

## AD-0001 — Microsoft Store Products Are Treated As Subscriptions

- Spec references: Section 3.2.1, Step 5 in Section 41.
- Decision: implement all paid Store add-ons as `Subscription` products.
- Reason: Section 41 explicitly instructs subscription product creation, while the Section 3.2.1 table uses inaccurate durable wording.

## AD-0002 — `NexCode.Service` Is A User-Mode Helper Host

- Spec references: Sections 2.1.1, 29.1, 29.2.
- Decision: implement `NexCode.Service` as a long-running user-mode helper/tray process, started by the app and optionally at login.
- Reason: this better matches packaged desktop app behavior and avoids Windows Service distribution complexity.

## AD-0003 — Paid Licensing Is Store-First Only

- Spec references: Sections 3.2, 37, 41.
- Decision: Microsoft Store IAP is the sole paid entitlement source. Non-Store distribution is for dev/test or limited free scenarios until a separate licensing system exists.
- Reason: the spec explicitly rejects an external licensing API.

## AD-0004 — Session Memory Is Runtime State

- Spec references: Section 9.1, Section 2.3.
- Decision: global and project memories are durable; session-scoped memory is runtime/session state and not long-term persisted in the encrypted database.
- Reason: the spec marks session memory as not persisted, which conflicts with a blanket interpretation of all memory data being durable.

## AD-0005 — Plugin Manifest Canonical Name

- Spec references: Section 22.1.
- Decision: the canonical plugin manifest file name is `nexcode-plugin.json`.
- Reason: the spec introduces directory-based plugins but uses inconsistent naming elsewhere in planning notes; a stable manifest name is needed for tooling.

## AD-0006 — Checkpoint Restore Must Be Guarded

- Spec references: Section 13.3, Section 35, Section 36.
- Decision: restore/retry flows must use guarded restore semantics that account for unmanaged working-tree changes and require explicit confirmation for destructive actions.
- Reason: the spec wants checkpoint restore, but an unconditional blanket reset is too risky for shared worktrees.

## AD-0007 — WinUI Bootstrap Is Considered Successful Only After Template Verification

- Spec references: Sections 4.1, 38; WinUI skill bootstrap flow.
- Decision: environment readiness is not accepted until `dotnet new list winui` succeeds and a scaffold can be created from the Microsoft WinUI template path.
- Reason: the initial machine bootstrap reported success before the template was actually usable, so template verification is part of the gate.

## AD-0008 — Superuser Access Uses A Sealed Local Grant

- Spec references: Section 3.1.2, Section 2.3, Section 35; user security requirement on 2026-04-19.
- Decision: replace the public hardcoded superuser identifier and env-var activation path with a sealed local grant stored outside the repository, protected at rest with DPAPI, and validated cryptographically by the helper service.
- Reason: the repository is intended to be public, so the privileged account mapping must not be exposed as a checked-in literal or a supported environment-variable switch.

## AD-0009 — Agent Loop Hosted In `NexCode.Service`; CLI Reserved For Sub-Agents And Headless Use

- Spec references: Sections 2.1.1, 2.1.3, 5.1, 5.3, 19.
- Decision: the main-session agent loop (LLM provider streaming, tool execution, checkpoint emission, plan/todo/clarify management) runs in-process inside `NexCode.Service`. `NexCode.Cli` is **not** spawned per main session. `NexCode.Cli` is reserved as the worker host for two cases:
  - Sub-agents spawned via `spawn_subagent` (Section 19), where each sub-agent runs in its own `NexCode.Cli` child process for permission and sandbox isolation.
  - Headless / CI / scripted usage where the user invokes `nexcode` directly from a terminal without the GUI.
- Reason: §2.1.3 reads as if the helper *must* spawn one CLI worker per active session, but spawning a child process for every chat turn is unnecessary process churn and complicates IPC, streaming, and crash recovery. Running the main session inside the helper keeps the hot path simple, lets the existing `ServiceEventHub` carry token/tool/checkpoint events without an extra hop, and still gives sub-agents real OS-level isolation when they actually need it. The CLI surface stays useful as a thin JSON-RPC client today and grows into a proper worker host when sub-agents land in Slice 0016.
- Implication: Section 5 of `plan.md` should be read as "the agent loop runs in the helper for the main session, and in a CLI worker process for sub-agents", not "always in a CLI worker". Future spec revisions should fold this into §2.1 rather than treating it as an exception.
