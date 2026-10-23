using System.Windows.Input;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>One volume keyframe of the audio group (DESIGN 4.4): its time, its level in dB and a remove button.</summary>
public sealed class InspectorKeyframeRow : InspectorRow
{
    /// <summary>Creates the row.</summary>
    /// <param name="index">The keyframe index.</param>
    /// <param name="time">The time field (clip-relative timecode).</param>
    /// <param name="level">The level field in dB.</param>
    /// <param name="removeCommand">Removes this keyframe.</param>
    public InspectorKeyframeRow(int index, InspectorTimecodeField time, InspectorNumberField level, ICommand removeCommand)
        : base(string.Empty)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(removeCommand);
        Index = index;
        Time = time;
        Level = level;
        RemoveCommand = removeCommand;
    }

    /// <summary>The keyframe index.</summary>
    public int Index { get; }

    /// <summary>The time field.</summary>
    public InspectorTimecodeField Time { get; }

    /// <summary>The level field.</summary>
    public InspectorNumberField Level { get; }

    /// <summary>Removes the keyframe.</summary>
    public ICommand RemoveCommand { get; }
}
