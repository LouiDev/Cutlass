using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The error dialog (DESIGN 9): a summary, the last 30 ffmpeg log lines and "Copy details", which includes the
/// command.
/// </summary>
public sealed partial class ErrorDialogViewModel : ObservableObject
{
    /// <summary>How many log lines the dialog shows.</summary>
    public const int LogLineCount = 30;

    /// <summary>Creates the view model.</summary>
    /// <param name="title">The window title, for example "Export failed".</param>
    /// <param name="summary">One or two sentences saying what went wrong.</param>
    /// <param name="log">The ffmpeg log (only the last <see cref="LogLineCount"/> lines are kept).</param>
    /// <param name="command">The ffmpeg command for display, or null.</param>
    public ErrorDialogViewModel(string title, string summary, IReadOnlyList<string>? log, string? command)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Error" : title;
        Summary = string.IsNullOrWhiteSpace(summary) ? "Something went wrong." : summary.Trim();
        var lines = log ?? [];
        LogText = string.Join('\n', lines.Skip(Math.Max(0, lines.Count - LogLineCount)));
        Command = string.IsNullOrWhiteSpace(command) ? null : command;
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The window title.</summary>
    public string Title { get; }

    /// <summary>What went wrong.</summary>
    public string Summary { get; }

    /// <summary>The last log lines.</summary>
    public string LogText { get; }

    /// <summary>The command for display, or null.</summary>
    public string? Command { get; }

    /// <summary>True when there are log lines.</summary>
    public bool HasLog => LogText.Length > 0;

    /// <summary>True when the command is known.</summary>
    public bool HasCommand => Command is not null;

    /// <summary>The text "Copy details" puts on the clipboard: summary, log and command.</summary>
    public string DetailsText
    {
        get
        {
            var text = new StringBuilder(Summary);
            if (HasLog)
            {
                text.Append("\n\nffmpeg log (last ").Append(LogLineCount).Append(" lines):\n").Append(LogText);
            }

            if (Command is not null)
            {
                text.Append("\n\nCommand:\n").Append(Command);
            }

            return text.ToString();
        }
    }

    /// <summary>Builds the dialog for an exception, taking the log and command from an <see cref="FfmpegException"/>.</summary>
    /// <param name="title">The window title.</param>
    /// <param name="error">The error.</param>
    /// <param name="log">The log, when the caller has a longer one than the exception.</param>
    /// <param name="command">The command, when the caller knows it.</param>
    /// <returns>The view model.</returns>
    public static ErrorDialogViewModel FromException(string title, Exception error, IReadOnlyList<string>? log = null, string? command = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error is FfmpegException ff)
        {
            return new ErrorDialogViewModel(
                title,
                ff.Message,
                log is { Count: > 0 } ? log : ff.StderrTail,
                command ?? ff.DisplayCommand);
        }

        return new ErrorDialogViewModel(title, error.Message, log, command);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
