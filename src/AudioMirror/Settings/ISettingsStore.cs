namespace AudioMirror.Settings;

internal interface ISettingsStore
{
    /// <summary>Never throws: falls back to <see cref="AppSettings.Default"/> if nothing usable is stored.</summary>
    AppSettings Load();

    /// <summary>Best effort: a failure is logged, never thrown, because settings must not break audio.</summary>
    void Save(AppSettings settings);
}
