using System.Windows;
using System.Windows.Input;
namespace SistemPusulasi;
public partial class MainWindow
{
    private void InitializeWindowBehavior()
    {
        WindowStyle=WindowStyle.SingleBorderWindow;
        ResizeMode=ResizeMode.CanResizeWithGrip;
        ShowInTaskbar=true;
        var bounds=FitWindow(SystemParameters.WorkArea,1240,820);
        MinWidth=Math.Min(900,bounds.Width);MinHeight=Math.Min(560,bounds.Height);
        Width=bounds.Width;Height=bounds.Height;
        WindowStartupLocation=WindowStartupLocation.Manual;
        Left=bounds.Left;Top=bounds.Top;
        // In addition to the native title bar, the page heading acts as a familiar drag surface.
        PageTitle.MouseLeftButtonDown+=WorkspaceTitle_MouseLeftButtonDown;
        PageTitle.Cursor=Cursors.SizeAll;
        PageTitle.ToolTip=L10n.T("Taşımak için sürükleyin; büyütmek veya geri almak için çift tıklayın.");
    }
    internal static Rect FitWindow(Rect workArea,double preferredWidth,double preferredHeight)
    {
        double width=Math.Min(preferredWidth,Math.Max(1,workArea.Width-24));
        double height=Math.Min(preferredHeight,Math.Max(1,workArea.Height-24));
        return new Rect(workArea.Left+(workArea.Width-width)/2,workArea.Top+(workArea.Height-height)/2,width,height);
    }
    private void WorkspaceTitle_MouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ClickCount==2) {WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;e.Handled=true;return;}
        if(e.ButtonState==MouseButtonState.Pressed && WindowState==WindowState.Normal) {try{DragMove();}catch(InvalidOperationException){}e.Handled=true;}
    }
}
