using System.Windows;
using System.Windows.Controls;
namespace SistemPusulasi;

public partial class MainWindow
{
    private bool initializingLanguage;
    private sealed record LanguageChoice(string Code,string Name);
    internal void InitializeLanguagePicker()
    {
        initializingLanguage=true;
        LanguagePicker.DisplayMemberPath="Name";LanguagePicker.SelectedValuePath="Code";
        LanguagePicker.ItemsSource=new[]{new LanguageChoice("tr","Türkçe"),new LanguageChoice("en","English")};
        LanguagePicker.SelectedValue=L10n.Language;
        initializingLanguage=false;
        Activated+=(_,_)=>AutomaticGitHubCheck();
    }
    private void LanguagePicker_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(initializingLanguage || LanguagePicker.SelectedItem is not LanguageChoice choice || choice.Code==L10n.Language)return;
        if(githubBusy || updateScanRunning || antivirusReading || installLaunching || updateInstalling) {
            initializingLanguage=true;LanguagePicker.SelectedValue=L10n.Language;initializingLanguage=false;
            MessageBox.Show(L10n.T("Dil değiştirmeden önce çalışan sorgunun tamamlanmasını bekleyin."),"System Compass");return;
        }
        if(!demo) {
            SaveSettings();
            var current=LocalStore.LoadSettings();current.Language=choice.Code;LocalStore.SaveSettings(current);
        }
        L10n.SetLanguage(choice.Code);
        var replacement=new MainWindow(demo);
        replacement.InitializeLanguagePicker();
        replacement.Left=Left;replacement.Top=Top;replacement.Width=Width;replacement.Height=Height;
        replacement.WindowState=WindowState;
        Application.Current.MainWindow=replacement;
        replacement.Show();replacement.Page("settings");Close();
    }
}
