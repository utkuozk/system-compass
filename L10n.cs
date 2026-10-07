using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace SistemPusulasi;

/// <summary>Turkish source strings are the stable translation keys.</summary>
public static class L10n
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, string> English = new(UiTranslations.English, StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> Missing = new(StringComparer.Ordinal);
    public static string Language { get; private set; } = "tr";
    public static event EventHandler? LanguageChanged;
    public static IReadOnlyCollection<string> MissingEnglishKeys => Missing.Keys.ToArray();
    public static IReadOnlyDictionary<string, string> EnglishTranslations
    {
        get { lock (Sync) return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(English, StringComparer.Ordinal)); }
    }

    public static void RegisterEnglish(IDictionary<string, string> translations)
    {
        ArgumentNullException.ThrowIfNull(translations);
        lock (Sync)
            foreach (var entry in translations)
            {
                English[entry.Key] = entry.Value;
                Missing.TryRemove(entry.Key, out _);
            }
    }

    public static void SetLanguage(string language)
    {
        var normalized = language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "tr";
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => SetLanguage(normalized));
            return;
        }
        Language = normalized;
        var culture = CultureInfo.GetCultureInfo(normalized == "en" ? "en-US" : "tr-TR");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string T(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        if (Language == "en")
        {
            lock (Sync)
                if (English.TryGetValue(source, out var translated)) return translated;
            Missing.TryAdd(source, 0);
        }
        return source.Replace("Sistem Pusulası", "System Compass", StringComparison.Ordinal)
            .Replace("SİSTEM PUSULASI", "SYSTEM COMPASS", StringComparison.Ordinal);
    }

    public static string F(string sourceFormat, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, T(sourceFormat), arguments);
}

/// <summary>Static XAML labels are resolved in the selected language when the window is created.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => L10n.T(UiTranslations.SourceFor(Key));
}
