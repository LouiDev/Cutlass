using Cutlass.Compiler.Graph;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Compiler;

/// <summary>VolumeEnvelope: the dB ramp expression of TECH_BRIEF 7.7.</summary>
[UseCulture("de-DE")]
public sealed class VolumeEnvelopeTests
{
    [Fact]
    public void WorkedExampleEnvelope()
    {
        var filter = VolumeEnvelope.Filter([new VolumeKey(0, -20), new VolumeKey(30, 0)], 0, 0, FrameRate.Fps30);

        Assert.Equal(
            "volume=volume='pow(10,(if(lt(t,1.000000),-20.000000+(20.000000)*(t-0.000000)/1.000000,0.000000))/20)':eval=frame",
            filter);
    }

    [Fact]
    public void LateFirstKeyHoldsItsValueAndGainIsFoldedIn()
    {
        var expression = VolumeEnvelope.Expression([new VolumeKey(15, -6), new VolumeKey(45, 0)], 0, 2.5, FrameRate.Fps30);

        Assert.Equal(
            "if(lt(t,0.500000),-3.500000,if(lt(t,1.500000),-3.500000+(6.000000)*(t-0.500000)/1.000000,2.500000))",
            expression);
    }

    [Fact]
    public void HeadExtensionShiftsKeyTimes()
    {
        // A clip with a 15-frame head extension: key frame 0 sits 0.5 s after the extent start.
        var expression = VolumeEnvelope.Expression([new VolumeKey(0, 0)], 15, 0, FrameRate.Fps30);

        Assert.Equal("if(lt(t,0.500000),0.000000,0.000000)", expression);
    }

    [Fact]
    public void NtscTimesFloorToTheMicrosecond()
    {
        var expression = VolumeEnvelope.Expression([new VolumeKey(0, 0), new VolumeKey(1, -1)], 0, 0, FrameRate.Fps2997);

        Assert.Equal("if(lt(t,0.033366),0.000000+(-1.000000)*(t-0.000000)/0.033366,-1.000000)", expression);
    }

    [Fact]
    public void KeysAreSortedAndDuplicatesCollapse()
    {
        var expression = VolumeEnvelope.Expression([new VolumeKey(30, -12), new VolumeKey(0, 0), new VolumeKey(30, -6)], 0, 0, FrameRate.Fps30);

        Assert.Equal("if(lt(t,1.000000),0.000000+(-6.000000)*(t-0.000000)/1.000000,-6.000000)", expression);
    }

    [Fact]
    public void NeedsAtLeastOneKey()
    {
        Assert.Throws<ArgumentException>(() => VolumeEnvelope.Filter([], 0, 0, FrameRate.Fps30));
    }
}
