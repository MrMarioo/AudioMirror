using System.Globalization;
using System.Reflection;
using AudioMirror.Resources;

namespace AudioMirror.Tests;

public class StringsTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "pl-PL" };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Every_string_exists_in_every_language(string culture)
    {
        using var _ = new UiCultureScope(culture);

        foreach (var property in typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static))
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(null)), property.Name);

        // Formatted strings: the placeholder must survive translation.
        var formatters = typeof(Strings).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => !m.IsSpecialName);
        foreach (var method in formatters)
            Assert.Contains("X", (string?)method.Invoke(null, ["X"]), StringComparison.Ordinal);
    }

    [Fact]
    public void Polish_windows_gets_polish_ui()
    {
        using (new UiCultureScope("pl-PL")) Assert.Equal("Zamknij", Strings.MenuExit);
        using (new UiCultureScope("en-US")) Assert.Equal("Exit", Strings.MenuExit);
    }

    private sealed class UiCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
        public UiCultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);
        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
