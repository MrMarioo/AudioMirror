using AudioMirror.Core;

namespace AudioMirror.Tests;

public class MirrorRoutingTests
{
    private static readonly AudioDevice Speakers = new("speakers", "Speakers");
    private static readonly AudioDevice Hdmi = new("hdmi", "HDMI Monitor");
    private static readonly AudioDevice Headset = new("headset", "Headset");

    private static HashSet<string> Selected(params string[] ids) => new(ids, StringComparer.Ordinal);

    [Fact]
    public void Targets_are_selected_active_devices()
    {
        var routing = MirrorRouting.Resolve(Speakers, [Speakers, Hdmi, Headset], Selected("hdmi"));

        Assert.Equal([Hdmi], routing.Targets);
    }

    [Fact]
    public void Primary_device_is_never_a_target()
    {
        var routing = MirrorRouting.Resolve(Speakers, [Speakers, Hdmi], Selected("speakers", "hdmi"));

        Assert.DoesNotContain(Speakers, routing.Targets);
    }

    [Fact]
    public void Selected_but_unplugged_devices_are_skipped()
    {
        var routing = MirrorRouting.Resolve(Speakers, [Speakers], Selected("hdmi"));

        Assert.Empty(routing.Targets);
    }

    [Fact]
    public void Equality_ignores_device_order_and_names()
    {
        var a = MirrorRouting.Resolve(Speakers, [Hdmi, Headset], Selected("hdmi", "headset"));
        var b = MirrorRouting.Resolve(Speakers with { Name = "Renamed" }, [Headset, Hdmi], Selected("headset", "hdmi"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Different_primary_means_different_routing()
    {
        var a = MirrorRouting.Resolve(Speakers, [Hdmi], Selected("hdmi"));
        var b = MirrorRouting.Resolve(Headset, [Hdmi], Selected("hdmi"));

        Assert.NotEqual(a, b);
    }
}
