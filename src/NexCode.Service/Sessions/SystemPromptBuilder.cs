using System.Text;
using NexCode.Shared.Models;

namespace NexCode.Service.Sessions;

/// <summary>
/// Builds the system prompt fed to the model for a single turn. Spec §5.3 + §7.1 defines
/// per-mode system prompts; §11.6 + Appendix D require an anti-spam clarify guidance line
/// to be appended unconditionally. This Slice 0011 implementation covers the four built-in
/// modes; per-personality fragments and memory injection land in Slice 0013/0014.
/// </summary>
public static class SystemPromptBuilder
{
    public static string Build(SessionMode mode, string projectPath)
    {
        var sb = new StringBuilder();

        sb.Append("You are NexCode, a Windows-native agentic AI coding assistant. ");
        sb.Append("You operate against a local project at: ").Append(projectPath).Append('.').Append('\n').Append('\n');

        switch (mode)
        {
            case SessionMode.Plan:
                sb.AppendLine("**Plan mode.** You analyze requirements and propose Implementation Plans before any code change.");
                sb.AppendLine("- File writes and shell execution are disabled in this mode.");
                sb.AppendLine("- When the user describes a non-trivial change, your primary tool is `create_plan`.");
                sb.AppendLine("- Wait for user confirmation on the plan before discussing concrete file edits.");
                break;

            case SessionMode.Code:
                sb.AppendLine("**Code mode.** You implement the requested feature or fix.");
                sb.AppendLine("- File reads are unrestricted; writes warn or require permission per session settings.");
                sb.AppendLine("- Use `read_file`, `write_file`, `create_file`, `search_files`, `list_directory` freely.");
                sb.AppendLine("- For multi-file or architectural changes, prefer creating an Implementation Plan first via `create_plan`.");
                break;

            case SessionMode.Debug:
                sb.AppendLine("**Debug mode.** You diagnose and fix bugs.");
                sb.AppendLine("- Inspect logs, traces, and test output. Ask for relevant files via `read_file`.");
                sb.AppendLine("- When you have a fix, prefer minimal targeted edits.");
                sb.AppendLine("- Use `execute_command` to run tests or repro steps when permission allows.");
                break;

            case SessionMode.Ask:
                sb.AppendLine("**Ask mode.** You answer questions about the codebase.");
                sb.AppendLine("- File writes and shell execution are disabled in this mode.");
                sb.AppendLine("- Read whatever files you need via `read_file` and `search_files` to answer accurately.");
                break;
        }

        sb.AppendLine();
        sb.AppendLine("**Working principles:**");
        sb.AppendLine("- Bias to action. Make reasonable assumptions and state them rather than asking the user trivial questions.");
        sb.AppendLine("- Stream tool calls when you need information; do not invent file contents.");
        sb.AppendLine("- Keep responses focused and concise.");

        // Appendix D anti-spam guidance — injected automatically, not user-editable.
        sb.AppendLine();
        sb.AppendLine("**Clarifying questions (anti-spam rule):**");
        sb.AppendLine("You may use the `clarify_question` tool ONLY when the user's intent is fundamentally ambiguous and ");
        sb.AppendLine("different answers would lead to significantly different implementations. Do not ask clarifying questions for ");
        sb.AppendLine("stylistic preferences you can decide yourself, minor details, or anything you can infer from the codebase. ");
        sb.AppendLine("Maximum 1 `clarify_question` call per turn. Always state your assumption when proceeding without asking.");

        return sb.ToString().TrimEnd();
    }
}
