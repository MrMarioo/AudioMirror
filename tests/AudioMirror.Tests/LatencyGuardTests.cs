using AudioMirror.Audio;

namespace AudioMirror.Tests;

public class LatencyGuardTests
{
    // 48 kHz, 32-bit float stereo: the usual WASAPI shared-mode mix format.
    private const int BlockAlign = 8;
    private const int BytesPerSecond = 48_000 * BlockAlign;
    private const int BytesPerMs = BytesPerSecond / 1000;

    private static LatencyGuard CreateGuard()
        => new(BytesPerSecond, BlockAlign, TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(250));

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(250)]
    public void Keeps_audio_while_buffer_is_within_limit(int bufferedMs)
    {
        Assert.Equal(0, CreateGuard().BytesToDrop(bufferedMs * BytesPerMs));
    }

    [Theory]
    [InlineData(251)]
    [InlineData(400)]
    [InlineData(1000)]
    public void Trims_back_to_target_once_buffer_exceeds_limit(int bufferedMs)
    {
        int buffered = bufferedMs * BytesPerMs;

        int drop = CreateGuard().BytesToDrop(buffered);

        Assert.Equal(80 * BytesPerMs, buffered - drop);
    }

    [Fact]
    public void Drops_whole_frames_only()
    {
        // Odd byte counts happen when capture packets don't line up with the guard's maths.
        int buffered = 300 * BytesPerMs + 5;

        int drop = CreateGuard().BytesToDrop(buffered);

        Assert.True(drop > 0);
        Assert.Equal(0, drop % BlockAlign);
    }

    [Fact]
    public void Rejects_max_not_above_target()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LatencyGuard(BytesPerSecond, BlockAlign, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100)));
    }

    [Theory]
    [InlineData(0, BlockAlign)]
    [InlineData(BytesPerSecond, 0)]
    public void Rejects_invalid_format(int bytesPerSecond, int blockAlign)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LatencyGuard(bytesPerSecond, blockAlign, TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(250)));
    }
}
