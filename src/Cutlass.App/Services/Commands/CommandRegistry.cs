using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Cutlass.App.Services.Commands;

/// <summary>
/// Maps command ids (<see cref="CommandIds"/>) to the commands that implement them, so the keymap, menus and
/// toolbars share one set of commands. Ids without a registration are ignored, which lets the keymap name commands
/// of later milestones. While an agent holds the project (AGENT_INTEGRATION 3.7) <see cref="Gate"/> refuses every
/// command outside <see cref="CommandIds.AllowedWhileAgentHolds"/>, so menus disable themselves and keys are dropped.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, (ICommand Command, object? Parameter)> _commands = new(StringComparer.Ordinal);

    /// <summary>
    /// Decides whether a command may run now regardless of its own state: false refuses it in <see cref="CanExecute"/>
    /// and <see cref="TryExecute"/>. Null allows every command. The main window sets it to the agent lock gate.
    /// </summary>
    public Func<string, bool>? Gate { get; set; }

    /// <summary>The registered ids.</summary>
    public IReadOnlyCollection<string> Ids => _commands.Keys;

    /// <summary>Registers (or replaces) a command.</summary>
    /// <param name="id">The command id.</param>
    /// <param name="command">The command.</param>
    /// <param name="parameter">The parameter passed to <see cref="ICommand.Execute"/>.</param>
    public void Register(string id, ICommand command, object? parameter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(command);
        _commands[id] = (command, parameter);
    }

    /// <summary>Registers (or replaces) an action as a command.</summary>
    /// <param name="id">The command id.</param>
    /// <param name="execute">The action.</param>
    /// <param name="canExecute">Whether it can run now, or null for always.</param>
    public void Register(string id, Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        Register(id, canExecute is null ? new RelayCommand(execute) : new RelayCommand(execute, canExecute));
    }

    /// <summary>True when a command is registered under the id.</summary>
    /// <param name="id">The command id.</param>
    /// <returns>True when registered.</returns>
    public bool IsRegistered(string id) => _commands.ContainsKey(id);

    /// <summary>True when the command is registered and can run now.</summary>
    /// <param name="id">The command id.</param>
    /// <returns>True when it can run.</returns>
    public bool CanExecute(string id) =>
        IsAllowed(id) && _commands.TryGetValue(id, out var entry) && entry.Command.CanExecute(entry.Parameter);

    /// <summary>True when <see cref="Gate"/> lets the command run (whatever the command's own state).</summary>
    /// <param name="id">The command id.</param>
    /// <returns>True when allowed.</returns>
    public bool IsAllowed(string id) => Gate?.Invoke(id) ?? true;

    /// <summary>Runs the command when it is registered and can run.</summary>
    /// <param name="id">The command id.</param>
    /// <returns>True when the id is registered (the key counts as handled even if the command is disabled or gated now).</returns>
    public bool TryExecute(string id)
    {
        if (!_commands.TryGetValue(id, out var entry))
        {
            return false;
        }

        if (IsAllowed(id) && entry.Command.CanExecute(entry.Parameter))
        {
            entry.Command.Execute(entry.Parameter);
        }

        return true;
    }
}
