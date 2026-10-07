using System.Windows;
namespace SistemPusulasi;

internal static class UiDialogs
{
    static UiDialogs()=>L10n.RegisterEnglish(new Dictionary<string,string>{
        ["Sistem Pusulası — İşlem tamamlanamadı"]="System Compass — Operation could not be completed",
        ["Başka bir antivirüs işlemi sürüyor."]="Another antivirus operation is running.",
        ["Kurulum dosyası bulunamadı. ZIP paketini tamamen çıkarıp yeniden dene."]="Installer file was not found. Extract the entire ZIP package and try again.",
        ["Sistem Pusulası çalışıyor. Devam eden bakım varsa bitmesini bekleyin; ardından uygulamayı kapatıp kurulumu tekrar açın."]="System Compass is running. Wait for any maintenance to finish, close the app, then run the installer again.",
        ["Çalışan Sistem Pusulası işlemi doğrulanamadı. Bakımın bitmesini bekleyip uygulamayı kapatın."]="The running System Compass process could not be verified. Wait for maintenance to finish and close the app.",
        ["Günlük tarama görevi {0} için kaydedildi."]="The daily scan has been scheduled for {0}."
    });
    internal static MessageBoxResult Show(string text,string caption,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage icon=MessageBoxImage.None)
    {
        var match=System.Text.RegularExpressions.Regex.Match(text,@"^Günlük tarama görevi (\d{2}:\d{2}) için kaydedildi\.$");
        return MessageBox.Show(match.Success?L10n.F("Günlük tarama görevi {0} için kaydedildi.",match.Groups[1].Value):DynamicTranslations.KnownText(text),L10n.T(caption),buttons,icon);
    }
}
