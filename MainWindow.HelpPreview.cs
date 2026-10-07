using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace SistemPusulasi;

public partial class MainWindow
{
    internal static void CheckAndRenderOverviewHelp(Action<bool,string> check)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"ui-previews");
        Directory.CreateDirectory(directory);
        foreach(var english in new[]{false,true}) {
            var help=new OverviewHelpWindow(english);
            try {
                var content=(FrameworkElement)help.Content;
                content.Measure(new Size(780,620));content.Arrange(new Rect(0,0,780,620));content.UpdateLayout();
                var tabs=VisualDescendants<TabControl>(content).Single();
                check(tabs.Items.Count==4,"Overview help has four focused sections: "+(english?"en":"tr"));
                for(var index=0;index<tabs.Items.Count;index++) {
                    tabs.SelectedIndex=index;
                    RenderPreview(content,780,620,Path.Combine(directory,$"overview-help-{(english?"en":"tr")}-{index}.png"));
                    check(VisualDescendants<TextBlock>(tabs).Count(t=>t.ActualHeight>0 && !string.IsNullOrWhiteSpace(t.Text))>1,"Overview help section has readable content: "+index);
                }
            } finally {help.Close();}
        }
    }
}
