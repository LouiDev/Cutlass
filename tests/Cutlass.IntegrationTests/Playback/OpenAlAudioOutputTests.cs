using System.Diagnostics;
using Cutlass.Media.Audio;

namespace Cutlass.IntegrationTests.Playback;

/// <summary>
/// The OpenAL output on this machine's default device (TECH_BRIEF 5.6): it opens through OpenAL Soft, streams 1 s of
/// silence and tone through the 6-buffer ring, and <see cref="IAudioOutput.PlayedSamples"/> advances monotonically to
/// exactly the frames queued. Skips without a device.
/// </summary>
public sealed class OpenAlAudioOutputTests
{
    [AudioDeviceFact]
    public void PlaysOneSecondOfSilenceAndToneWithAMonotonicClock()
    {
        using var output = OpenAlAudioOutput.TryCreate(null, out var failure);
        Assert.True(output is not null, failure);
        Assert.True(output.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(output.DeviceName));
        output.Open(48000, 2);
        Assert.Equal(OpenAlAudioOutput.DefaultBufferCount, output.FreeBuffers);
        Assert.Equal(0, output.PlayedSamples);

        // 0.5 s of silence, then 0.5 s of a quiet 440 Hz tone: 48000 frames, so the last buffer is partial (896).
        const int Total = 48000;
        var samples = new short[Total * 2];
        for (var i = Total / 2; i < Total; i++)
        {
            var v = (short)(2000 * Math.Sin(2 * Math.PI * 440 * i / 48000.0));
            samples[2 * i] = v;
            samples[(2 * i) + 1] = v;
        }

        var queued = 0;
        var readings = new List<long>();
        var started = false;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            while (queued < Total && output.FreeBuffers > 0)
            {
                var frames = Math.Min(output.BufferFrames, Total - queued);
                Assert.True(output.Enqueue(samples.AsSpan(queued * 2, frames * 2)));
                queued += frames;
            }

            if (!started && output.FreeBuffers == 0)
            {
                output.Start();
                started = true;
            }

            readings.Add(output.PlayedSamples);
            if (queued == Total && output.QueuedBuffers == 0)
            {
                break;
            }

            Thread.Sleep(5);
        }

        Assert.True(started);
        Assert.Equal(Total, queued);
        readings.Add(output.PlayedSamples);
        for (var i = 1; i < readings.Count; i++)
        {
            Assert.True(readings[i] >= readings[i - 1], $"PlayedSamples went from {readings[i - 1]} back to {readings[i]}.");
        }

        Assert.Equal(Total, readings[^1]);
        Assert.True(readings.Distinct().Count() > 10, "PlayedSamples did not advance in steps while playing.");

        // Real time: one second of audio takes about a second (the device may start a little late).
        Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0.85, 3.0);

        output.Flush();
        Assert.Equal(0, output.PlayedSamples);
        Assert.Equal(OpenAlAudioOutput.DefaultBufferCount, output.FreeBuffers);
        Assert.False(output.IsStarted);
    }

    [Fact]
    public void ListingDevicesNeverThrows()
    {
        var devices = OpenAlAudioOutput.ListDevices();
        Assert.All(devices, d => Assert.False(string.IsNullOrEmpty(d)));
    }

    [Fact]
    public void AMissingDeviceFallsBackToTheSilentOutput()
    {
        using var output = OpenAlAudioOutput.CreateOrFallback("Cutlass test device that does not exist", out var failure);

        // Either the default device opened instead, or the silent fallback took over with a reason.
        if (output.IsAvailable)
        {
            Assert.Null(failure);
        }
        else
        {
            Assert.IsType<NullAudioOutput>(output);
            Assert.False(string.IsNullOrWhiteSpace(failure));
        }
    }
}
