namespace AudioMirror.Audio;

/// <summary>
/// Keeps a mirror output from drifting behind the primary device.
/// </summary>
/// <remarks>
/// Capture and playback run on different hardware clocks, so the buffer between them slowly
/// grows. When it grows past <see cref="MaxLatency"/>, the oldest audio is dropped so that
/// <see cref="TargetLatency"/> remains. Pure arithmetic, so it is unit-tested in isolation.
/// </remarks>
internal sealed class LatencyGuard
{
    public static readonly TimeSpan DefaultTarget = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan DefaultMax = TimeSpan.FromMilliseconds(250);

    private readonly int _maxBytes;
    private readonly int _targetBytes;
    private readonly int _blockAlign;

    public TimeSpan TargetLatency { get; }
    public TimeSpan MaxLatency { get; }

    public LatencyGuard(int averageBytesPerSecond, int blockAlign, TimeSpan target, TimeSpan max)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(averageBytesPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockAlign);
        if (target < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(target), "Target latency can't be negative.");
        if (max <= target)
            throw new ArgumentOutOfRangeException(nameof(max), "Max latency must be greater than target latency.");

        _blockAlign = blockAlign;
        TargetLatency = target;
        MaxLatency = max;
        _targetBytes = Align((int)(averageBytesPerSecond * target.TotalSeconds));
        _maxBytes = Align((int)(averageBytesPerSecond * max.TotalSeconds));
    }

    /// <summary>
    /// How many bytes to discard from a buffer currently holding <paramref name="bufferedBytes"/>.
    /// Always a whole number of audio frames, so channels never get swapped.
    /// </summary>
    public int BytesToDrop(int bufferedBytes)
        => bufferedBytes > _maxBytes ? Align(bufferedBytes - _targetBytes) : 0;

    private int Align(int bytes) => bytes - bytes % _blockAlign;
}
