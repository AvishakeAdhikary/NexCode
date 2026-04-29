using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;

namespace NexCode.Cli.Clarify;

/// <summary>
/// Spec §11.4 terminal clarify UI: renders a <c>clarify.question</c> envelope as a
/// numbered list per question with arrow-key navigation, space to toggle, enter to
/// advance, and [C] to cancel. Submits via <see cref="IpcMethods.ClarifyRespond"/>.
/// The renderer is decoupled from <see cref="Console"/> so callers/tests can inject
/// any <see cref="ITerminalClarifyIo"/> implementation.
/// </summary>
public sealed class TerminalClarifyRenderer
{
    private readonly ITerminalClarifyIo _io;

    public TerminalClarifyRenderer(ITerminalClarifyIo? io = null)
    {
        _io = io ?? new ConsoleClarifyIo();
    }

    /// <summary>Render a clarify event and produce the answers payload (without sending it).</summary>
    public ClarifyRespondRequest Render(ClarifyQuestionEventPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        _io.WriteLine($"Clarification needed: {payload.Context}");
        _io.WriteLine("(Use [↑/↓] to move, [space] to toggle, [enter] to confirm, [c] to cancel.)");
        _io.WriteLine();

        var answers = new List<ClarifyAnswerItem>();
        foreach (var question in payload.Questions)
        {
            var answer = AskQuestion(question);
            if (answer is null)
            {
                return new ClarifyRespondRequest(
                    QuestionId: payload.QuestionId,
                    Answers: Array.Empty<ClarifyAnswerItem>(),
                    Cancelled: true);
            }
            answers.Add(answer);
        }

        return new ClarifyRespondRequest(
            QuestionId: payload.QuestionId,
            Answers: answers.ToArray(),
            Cancelled: false);
    }

    /// <summary>Render and submit the answers via the helper.</summary>
    public async Task<bool> RenderAndRespondAsync(
        ClarifyQuestionEventPayload payload,
        string pipeName,
        CancellationToken cancellationToken)
    {
        var request = Render(payload);
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.ClarifyRespond, request),
            pipeName,
            cancellationToken);
        if (response.Error is not null)
        {
            IpcClient.PrintError(response.Error);
            return false;
        }
        return !request.Cancelled;
    }

    private ClarifyAnswerItem? AskQuestion(ClarifyQuestionItem question)
    {
        _io.WriteLine($"Q: {question.Prompt}");
        if (question.Options.Length == 0)
        {
            _io.WriteLine("(free-text answer)");
            _io.Write("> ");
            var text = _io.ReadLine();
            if (text is null) return null;
            return new ClarifyAnswerItem(question.Id, Array.Empty<string>(), text);
        }

        var selection = new HashSet<string>();
        var cursor = 0;
        var multi = string.Equals(question.Type, "multi", StringComparison.OrdinalIgnoreCase);

        while (true)
        {
            for (var index = 0; index < question.Options.Length; index++)
            {
                var option = question.Options[index];
                var marker = selection.Contains(option.Id) ? "[x]" : "[ ]";
                var pointer = index == cursor ? ">" : " ";
                _io.WriteLine($" {pointer} {index + 1}. {marker} {option.Label}");
            }
            _io.Write("? ");
            var key = _io.ReadKey();
            _io.WriteLine();
            switch (key.Kind)
            {
                case ClarifyKeyKind.Up:
                    cursor = (cursor - 1 + question.Options.Length) % question.Options.Length;
                    break;
                case ClarifyKeyKind.Down:
                    cursor = (cursor + 1) % question.Options.Length;
                    break;
                case ClarifyKeyKind.Space:
                    var toggleId = question.Options[cursor].Id;
                    if (!multi) selection.Clear();
                    if (!selection.Add(toggleId) && multi) selection.Remove(toggleId);
                    break;
                case ClarifyKeyKind.Enter:
                    if (selection.Count == 0 && question.Required)
                    {
                        _io.WriteLine("(answer required — toggle at least one option with [space])");
                        continue;
                    }
                    return new ClarifyAnswerItem(question.Id, selection.ToArray(), null);
                case ClarifyKeyKind.Cancel:
                    return null;
                case ClarifyKeyKind.Digit:
                    if (key.Value is int digit && digit >= 1 && digit <= question.Options.Length)
                    {
                        var id = question.Options[digit - 1].Id;
                        if (!multi) selection.Clear();
                        if (!selection.Add(id) && multi) selection.Remove(id);
                    }
                    break;
            }
        }
    }
}

public enum ClarifyKeyKind { Up, Down, Space, Enter, Cancel, Digit, Other }

public readonly record struct ClarifyKey(ClarifyKeyKind Kind, object? Value = null);

public interface ITerminalClarifyIo
{
    void Write(string text);
    void WriteLine(string text = "");
    string? ReadLine();
    ClarifyKey ReadKey();
}

internal sealed class ConsoleClarifyIo : ITerminalClarifyIo
{
    public void Write(string text) => Console.Write(text);
    public void WriteLine(string text = "") => Console.WriteLine(text);
    public string? ReadLine() => Console.ReadLine();
    public ClarifyKey ReadKey()
    {
        var info = Console.ReadKey(intercept: true);
        return info.Key switch
        {
            ConsoleKey.UpArrow => new ClarifyKey(ClarifyKeyKind.Up),
            ConsoleKey.DownArrow => new ClarifyKey(ClarifyKeyKind.Down),
            ConsoleKey.Spacebar => new ClarifyKey(ClarifyKeyKind.Space),
            ConsoleKey.Enter => new ClarifyKey(ClarifyKeyKind.Enter),
            ConsoleKey.C => new ClarifyKey(ClarifyKeyKind.Cancel),
            >= ConsoleKey.D1 and <= ConsoleKey.D9 => new ClarifyKey(ClarifyKeyKind.Digit, info.Key - ConsoleKey.D0),
            _ => new ClarifyKey(ClarifyKeyKind.Other)
        };
    }
}
