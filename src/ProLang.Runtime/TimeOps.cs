using System.Diagnostics;

namespace ProLang.Runtime;

/// <summary>
/// The clock behind the <c>time_millis</c> builtin.
/// </summary>
public static class TimeOps
{
    /// <summary>
    /// Started once, at first use, so the numbers a program sees begin near zero.
    /// </summary>
    /// <remarks>
    /// A <see cref="Stopwatch"/> rather than <see cref="Environment.TickCount"/> or
    /// <see cref="DateTime"/>. Tick count moves in steps of about fifteen milliseconds on Windows,
    /// which is most of a frame at sixty per second — a frame-rate counter built on it reads as a
    /// few fixed values rather than a measurement. And a wall clock is not monotonic: it can be
    /// set backwards, which would make an elapsed time negative.
    /// </remarks>
    private static readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    /// Milliseconds since the program started.
    /// </summary>
    /// <remarks>
    /// Truncated to 32 bits, so it wraps after about twenty-five days. That is deliberate rather
    /// than overlooked: ProLang's <c>int</c> is 32 bits, and every caller subtracts two readings to
    /// get an interval. Two's-complement subtraction gives the right answer across a wrap for any
    /// interval shorter than the wrap period, which every interval anybody measures with this is.
    /// </remarks>
    public static int Millis() => unchecked((int)_clock.ElapsedMilliseconds);
}
