using AudioMirror.Settings;

namespace AudioMirror.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "AudioMirror.Tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = new JsonSettingsStore(SettingsPath).Load();

        Assert.Empty(settings.SelectedDeviceIds);
        Assert.False(settings.MirrorEnabled);
    }

    [Fact]
    public void Saved_settings_round_trip_and_create_the_directory()
    {
        var store = new JsonSettingsStore(SettingsPath);

        store.Save(new AppSettings(["{0.0.0.00000000}.{abc}", "hdmi"], MirrorEnabled: true));
        var loaded = store.Load();

        Assert.Equal(["{0.0.0.00000000}.{abc}", "hdmi"], loaded.SelectedDeviceIds);
        Assert.True(loaded.MirrorEnabled);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("")]
    public void Corrupt_file_gives_defaults(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, content);

        var settings = new JsonSettingsStore(SettingsPath).Load();

        Assert.Equal(AppSettings.Default.MirrorEnabled, settings.MirrorEnabled);
        Assert.Empty(settings.SelectedDeviceIds);
    }

    [Fact]
    public void File_without_device_list_loads_as_empty_list()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "MirrorEnabled": true }""");

        var settings = new JsonSettingsStore(SettingsPath).Load();

        Assert.NotNull(settings.SelectedDeviceIds);
        Assert.True(settings.MirrorEnabled);
    }
}
