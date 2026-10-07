using System.IO;
using System.Text;

namespace SistemPusulasi;

internal static class InstallerLanguage
{
    // The setup marker lives beside the installed executable so it works even
    // when setup is elevated under a different administrator account. Each
    // user records which marker they have applied in their own LocalAppData.
    internal static bool ApplyPending(string markerPath, string appliedMarkerPath, AppSettings settings, Action<AppSettings> saveSettings)
    {
        string[] fields;
        try { fields = File.ReadAllText(markerPath, Encoding.UTF8).Trim().Split('|', 2); }
        catch { return false; }

        if (fields.Length != 2 || fields[1].Length == 0 ||
            fields[0] is not ("english" or "turkish")) return false;

        string applied;
        try { applied = File.ReadAllText(appliedMarkerPath, Encoding.UTF8).Trim(); }
        catch { applied = ""; }
        string marker = fields[0] + "|" + fields[1];
        if (string.Equals(applied, marker, StringComparison.Ordinal)) return false;

        settings.Language = fields[0] == "english" ? "en" : "tr";
        try { saveSettings(settings); }
        catch { return true; } // Use the selected language for this launch; retry persistence next time.
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(appliedMarkerPath)!);
            File.WriteAllText(appliedMarkerPath, marker, new UTF8Encoding(false));
        } catch { } // A failed acknowledgement may cause a harmless retry on the next launch.
        return true;
    }
}
