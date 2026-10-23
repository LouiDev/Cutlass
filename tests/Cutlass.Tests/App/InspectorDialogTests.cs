using Cutlass.App.ViewModels.Dialogs;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App;

public sealed class InspectorDialogTests
{
    [Fact]
    public void SpeedDialogPreviewsTheNewDuration()
    {
        var project = new ProjectBuilder().With(V1, V("v1", 0, 100)).Build();
        var dialog = new SpeedDialogViewModel(project, ["v1"]);
        Assert.Equal(100, dialog.Percent);
        Assert.True(dialog.RippleFollowing);
        Assert.Equal([25, 50, 100, 200, 400], dialog.Presets);
        dialog.SetPresetCommand.Execute(200);
        Assert.Equal(200, dialog.Percent);
        Assert.Equal(50, dialog.NewDuration());
        Assert.Equal("Duration 00:00:04:00 becomes 00:00:02:00", dialog.PreviewText);
    }

    [Fact]
    public void SpeedDialogClampsAndLimitsToTheSource()
    {
        // 100 frames of short.mov from source frame 0: at 25% it would need 400 frames but the source only has 100.
        var project = new ProjectBuilder().With(V1, V("v1", 0, 100, media: Short)).Build();
        var dialog = new SpeedDialogViewModel(project, ["v1"]) { Percent = 10 };
        Assert.Equal(25, dialog.Percent);
        dialog.Percent = 900;
        Assert.Equal(400, dialog.Percent);
        dialog.Percent = 50;
        Assert.Equal(TimelineQuery.MaxFramesFrom((MediaClip)TimelineQuery.FindClip(project, "v1")!.Value.Clip, project.Media[1], Rate, 50), dialog.NewDuration());
    }

    [Fact]
    public void SpeedDialogEditMatchesSetSpeed()
    {
        var project = new ProjectBuilder().With(V1, V("v1", 0, 100), V("v2", 100, 50)).Build();
        var dialog = new SpeedDialogViewModel(project, ["v1"]) { Percent = 50, RippleFollowing = false };
        var result = dialog.CreateEdit()(project);
        var expected = TimelineEdits.SetSpeed(project, ["v1"], 50, false);
        Assert.Equal(TimelineQuery.FindClip(expected.Project, "v1")!.Value.Clip, TimelineQuery.FindClip(result.Project, "v1")!.Value.Clip);
        Assert.Equal(100, TimelineQuery.FindClip(result.Project, "v2")!.Value.Clip.Start);
    }

    [Fact]
    public void SpeedDialogSkipsTitlesAndStills()
    {
        var project = new ProjectBuilder().With(V1, V("s1", 0, 50, media: Still)).With(V2, Title("t1", 0, 50)).Build();
        var dialog = new SpeedDialogViewModel(project, ["s1", "t1"]);
        Assert.False(dialog.CanApply);
        var closed = false;
        dialog.CloseRequested += (_, _) => closed = true;
        dialog.ApplyCommand.Execute(null);
        Assert.False(closed);
        dialog.CancelCommand.Execute(null);
        Assert.True(closed);
    }

    [Fact]
    public void SequenceDialogSyncsPresetsAndWarns()
    {
        var dialog = new SequenceSettingsDialogViewModel(new SequenceSettings(1920, 1080, FrameRate.Fps25), hasClips: true);
        Assert.Equal(1920, dialog.SelectedPreset?.Width);
        Assert.False(dialog.HasWarning);
        Assert.Equal(FrameRate.Supported.Length, dialog.Rates.Count);

        dialog.SelectedPreset = SequenceSizePreset.All.First(p => p.Width == 1080 && p.Height == 1920);
        Assert.Equal(1080, dialog.Width);
        Assert.Equal(1920, dialog.Height);
        Assert.True(dialog.HasWarning);
        Assert.Contains("re-fits", dialog.WarningText, StringComparison.Ordinal);

        dialog.Width = 1000;
        Assert.Same(SequenceSizePreset.Custom, dialog.SelectedPreset);

        dialog.SelectedRate = dialog.Rates.First(r => Equals(r.Value, FrameRate.Fps30));
        Assert.Contains("converts every clip", dialog.WarningText, StringComparison.Ordinal);
        Assert.Equal(new SequenceSettings(1000, 1920, FrameRate.Fps30), dialog.Settings);
    }

    [Fact]
    public void SequenceDialogWithoutClipsDoesNotWarn()
    {
        var dialog = new SequenceSettingsDialogViewModel(new SequenceSettings(1920, 1080, FrameRate.Fps25), hasClips: false);
        dialog.SelectedRate = dialog.Rates.First(r => Equals(r.Value, FrameRate.Fps60));
        Assert.False(dialog.HasWarning);
    }

    [Fact]
    public void SequenceDialogEditConvertsByTime()
    {
        var project = new ProjectBuilder().With(V1, V("v1", 25, 50)).Build();
        var dialog = new SequenceSettingsDialogViewModel(project.Sequence.Settings, true)
        {
            SelectedRate = SequenceSettingsDialogViewModel.RateChoices().First(r => Equals(r.Value, FrameRate.Fps50)),
        };
        var result = dialog.CreateEdit()(project);
        Assert.Equal(FrameRate.Fps50, result.Project.Sequence.Settings.Rate);
        Assert.Equal(50, TimelineQuery.FindClip(result.Project, "v1")!.Value.Clip.Start);
    }
}
