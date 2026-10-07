using System.Windows;

namespace SistemPusulasi;

public partial class MainWindow
{
    private OverviewHelpWindow? overviewHelpWindow;

    private void OverviewHelp_Click(object sender, RoutedEventArgs e)
    {
        bool english = L10n.Language == "en";
        if (overviewHelpWindow is not null && overviewHelpWindow.IsEnglish != english)
            overviewHelpWindow.Close();
        if (overviewHelpWindow is null)
        {
            var help = CreateOverviewHelpWindow(english);
            help.Owner = this;
            help.Closed += (_, _) => { if (ReferenceEquals(overviewHelpWindow, help)) overviewHelpWindow = null; };
            overviewHelpWindow = help;
            help.Show();
        }
        if (overviewHelpWindow.WindowState == WindowState.Minimized)
            overviewHelpWindow.WindowState = WindowState.Normal;
        overviewHelpWindow.Activate();
    }

    // The factory builds UI only; preview checks can inspect it without starting any system action.
    internal static OverviewHelpWindow CreateOverviewHelpWindow(bool english) => new(english);
}
