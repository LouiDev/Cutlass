using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.Core.Model;
using Cutlass.Media.Projects;

namespace Cutlass.App.ViewModels;

/// <summary>One row of the Relink dialog (DESIGN 11): the media, where it was, and the file chosen to replace it.</summary>
public sealed partial class RelinkItemViewModel : ObservableObject
{
    /// <summary>Creates the row.</summary>
    /// <param name="asset">The project asset.</param>
    /// <param name="state">Its link state (offline or changed; online when relinked by choice).</param>
    public RelinkItemViewModel(MediaAsset asset, MediaLinkState state)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Asset = asset;
        State = state;
        Name = MediaRelinker.FileNameOf(asset.Path);
    }

    /// <summary>The project asset.</summary>
    public MediaAsset Asset { get; }

    /// <summary>The media id.</summary>
    public string Id => Asset.Id;

    /// <summary>The file name the project expects.</summary>
    public string Name { get; }

    /// <summary>The path the project points at.</summary>
    public string OldPath => Asset.Path;

    /// <summary>The link state when the dialog opened.</summary>
    public MediaLinkState State { get; }

    /// <summary>True when the file was missing.</summary>
    public bool IsOffline => State == MediaLinkState.Offline;

    /// <summary>The file chosen to link to, or null while unresolved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolved))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? NewPath { get; set; }

    /// <summary>
    /// True while the recorded path is on a remote volume the open scan did not touch (DESIGN 11): "Confirm location"
    /// looks it up.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsUnconfirmed { get; set; }

    /// <summary>True once a confirmed location was looked up and the file was not there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsMissingAfterConfirm { get; set; }

    /// <summary>Why linking to <see cref="NewPath"/> failed, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    /// <summary>True when a new file was chosen.</summary>
    public bool IsResolved => NewPath is not null;

    /// <summary>True when the last attempt failed.</summary>
    public bool HasError => Error is not null;

    /// <summary>The status column: the error, the new path, or what is wrong with the old one.</summary>
    public string StatusText => Error is { } error
        ? error
        : NewPath is { } path
            ? "Found: " + path
            : IsUnconfirmed
                ? "Not checked: on a network location. Confirm the location to look it up."
            : IsMissingAfterConfirm
                ? "Offline: the file is not at this location"
            : State switch
            {
                MediaLinkState.Offline => "Offline: the file is missing",
                MediaLinkState.Changed => "Changed since import",
                _ => "Online",
            };
}
