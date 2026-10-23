using Cutlass.Media.Preview.Audio;

namespace Cutlass.App.Services.Playback;

/// <summary>
/// Peak meter ballistics for one channel of the master meter (DESIGN 4.5): the bar jumps up to a new peak at once and
/// falls at <see cref="FallDbPerSecond"/>; the peak-hold line stays for <see cref="HoldSeconds"/> and then falls too.
/// Pure, so it is unit tested; the viewer feeds it on every animation frame while playing.
/// </summary>
public sealed class MeterBallistics
{
    /// <summary>The bottom of the meter scale.</summary>
    public const double FloorDb = -60;

    /// <summary>How fast the bar and an expired hold fall.</summary>
    public const double FallDbPerSecond = 24;

    /// <summary>How long the peak-hold line stays.</summary>
    public const double HoldSeconds = 1.5;

    private double _held;

    /// <summary>The bar level in dBFS (at least <see cref="FloorDb"/>).</summary>
    public double LevelDb { get; private set; } = FloorDb;

    /// <summary>The peak-hold level in dBFS (at least <see cref="LevelDb"/>).</summary>
    public double PeakDb { get; private set; } = FloorDb;

    /// <summary>Feeds the peak measured since the last call.</summary>
    /// <param name="peak">The linear peak (1 is full scale).</param>
    /// <param name="elapsedSeconds">The time since the last call.</param>
    public void Update(float peak, double elapsedSeconds)
    {
        var dt = double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0, 1) : 0;
        var db = Math.Clamp(MixerPeaks.ToDbfs(peak), FloorDb, 0);
        LevelDb = Math.Max(db, Math.Max(FloorDb, LevelDb - (FallDbPerSecond * dt)));
        if (db >= PeakDb)
        {
            PeakDb = db;
            _held = 0;
        }
        else
        {
            _held += dt;
            if (_held > HoldSeconds)
            {
                PeakDb = Math.Max(FloorDb, PeakDb - (FallDbPerSecond * dt));
            }
        }

        PeakDb = Math.Max(PeakDb, LevelDb);
    }

    /// <summary>Drops both levels to the floor (playback stopped).</summary>
    public void Reset()
    {
        LevelDb = FloorDb;
        PeakDb = FloorDb;
        _held = 0;
    }
}
