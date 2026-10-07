using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SistemPusulasi;

internal static class ScrollBarThemeTests
{
    internal static void Run(Action<bool,string> check)
    {
        var viewer=new ScrollViewer { Width=320,Height=180,VerticalScrollBarVisibility=ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Visible,Content=new Border {Width=1100,Height=1300} };
        void Layout(){viewer.Measure(new Size(320,180));viewer.Arrange(new Rect(0,0,320,180));viewer.UpdateLayout();}
        Layout();
        var bars=Descendants(viewer).OfType<ScrollBar>().ToArray();
        check(bars.Length==2,"Overflow content exposes both themed scrollbar orientations");
        foreach(var orientation in new[]{Orientation.Vertical,Orientation.Horizontal}) {
            var bar=bars.Single(b=>b.Orientation==orientation);bar.ApplyTemplate();
            var track=bar.Template.FindName("PART_Track",bar) as Track;
            check(track?.Thumb!=null && track.Thumb.ActualHeight>0 && track.Thumb.ActualWidth>0,"Themed scroll handle has a usable hit area: "+orientation);
            var before=orientation==Orientation.Vertical?viewer.VerticalOffset:viewer.HorizontalOffset;
            (orientation==Orientation.Vertical?ScrollBar.PageDownCommand:ScrollBar.PageRightCommand).Execute(null,bar);
            Layout();
            var after=orientation==Orientation.Vertical?viewer.VerticalOffset:viewer.HorizontalOffset;
            check(after>before,"Track paging reaches overflow content: "+orientation);
        }
        viewer.ScrollToBottom();viewer.ScrollToRightEnd();Layout();
        check(viewer.VerticalOffset==viewer.ScrollableHeight && viewer.HorizontalOffset==viewer.ScrollableWidth,
            "Long content remains reachable at the bottom and right edges");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {
            var child=VisualTreeHelper.GetChild(parent,i);yield return child;
            foreach(var nested in Descendants(child))yield return nested;
        }
    }
}
