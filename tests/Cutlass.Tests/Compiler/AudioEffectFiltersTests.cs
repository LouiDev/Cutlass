using Cutlass.Compiler.Emit;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Compiler;

/// <summary>AudioEffectFilters: the filter text of TECH_BRIEF 7.3 step 3a, the pan law and the dB to linear conversion.</summary>
[UseCulture("de-DE")]
public sealed class AudioEffectFiltersTests
{
    [Fact]
    public void NeutralEffectsEmitNothing()
    {
        var e = AudioEffects.None;
        Assert.Null(AudioEffectFilters.HighPass(e));
        Assert.Null(AudioEffectFilters.LowPass(e));
        Assert.Null(AudioEffectFilters.Bass(e));
        Assert.Null(AudioEffectFilters.Treble(e));
        Assert.Null(AudioEffectFilters.Gate(e));
        Assert.Null(AudioEffectFilters.Compressor(e));
        Assert.Null(AudioEffectFilters.Stereo(e));
    }

    [Fact]
    public void FiltersAndShelvesFormatThroughFfNum()
    {
        var e = AudioEffects.None with { HighPassHz = 80, LowPassHz = 12000, BassDb = 3, TrebleDb = -2.5 };
        Assert.Equal("highpass=f=80.000000", AudioEffectFilters.HighPass(e));
        Assert.Equal("lowpass=f=12000.000000", AudioEffectFilters.LowPass(e));
        Assert.Equal("bass=g=3.000000:f=100", AudioEffectFilters.Bass(e));
        Assert.Equal("treble=g=-2.500000:f=3000", AudioEffectFilters.Treble(e));
    }

    [Theory]
    [InlineData(-20, 0.1)]
    [InlineData(-40, 0.01)]
    [InlineData(0, 1)]
    [InlineData(-6.0206, 0.5)]
    public void DecibelsConvertToLinearAmplitude(double db, double linear)
    {
        Assert.Equal(linear, AudioEffectFilters.Linear(db), 4);
    }

    [Fact]
    public void DynamicsUseLinearThresholdsNeverADbSuffix()
    {
        var gate = AudioEffects.None with { Gate = true, GateThresholdDb = -40 };
        Assert.Equal("agate=threshold=0.010000:ratio=4:attack=5:release=100:range=0.001000", AudioEffectFilters.Gate(gate));

        var comp = AudioEffects.None with { Compressor = true, CompressorThresholdDb = -20, CompressorRatio = 2.5 };
        Assert.Equal("acompressor=threshold=0.100000:ratio=2.500000:attack=5:release=50:knee=1:makeup=1", AudioEffectFilters.Compressor(comp));

        // The acompressor threshold stops at its filter minimum of 0.001 (-60 dB).
        var floor = comp with { CompressorThresholdDb = -60 };
        Assert.StartsWith("acompressor=threshold=0.001000:", AudioEffectFilters.Compressor(floor), StringComparison.Ordinal);
        Assert.DoesNotContain("dB", AudioEffectFilters.Gate(gate) + AudioEffectFilters.Compressor(comp), StringComparison.Ordinal);
    }

    [Fact]
    public void PanIsConstantPowerAndCentreEmitsNothing()
    {
        Assert.Null(AudioEffectFilters.Stereo(AudioEffects.None with { Pan = 0 }));
        Assert.Equal("pan=stereo|c0=1.000000*c0|c1=0.000000*c1", AudioEffectFilters.Stereo(AudioEffects.None with { Pan = -1 }));
        Assert.Equal("pan=stereo|c0=0.000000*c0|c1=1.000000*c1", AudioEffectFilters.Stereo(AudioEffects.None with { Pan = 1 }));
        // Half right: theta = 3 PI / 8, cos 0.382683, sin 0.923880.
        Assert.Equal("pan=stereo|c0=0.382683*c0|c1=0.923880*c1", AudioEffectFilters.Stereo(AudioEffects.None with { Pan = 0.5 }));
        var spec = AudioEffectFilters.Stereo(AudioEffects.None with { Pan = 0.3 })!;
        Assert.DoesNotContain(':', spec);
        Assert.DoesNotContain(',', spec);
    }

    [Fact]
    public void MonoWinsOverPanAndStaysStereo()
    {
        var mono = AudioEffects.None with { Mono = true, Pan = -1 };
        Assert.Equal("pan=stereo|c0=0.5*c0+0.5*c1|c1=0.5*c0+0.5*c1", AudioEffectFilters.Stereo(mono));
    }
}
