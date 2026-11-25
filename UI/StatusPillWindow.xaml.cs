using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SnapTranslate.Native;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace SnapTranslate.UI;

public partial class StatusPillWindow : Window
{
    private readonly DispatcherTimer _autoHideTimer;

    public StatusPillWindow()
    {
        InitializeComponent();
        _autoHideTimer = new DispatcherTimer();
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            Hide();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        Win32Interop.ApplyNoActivateToolWindow(hwnd, clickThrough: true);
    }

    public void ShowNearCursor(int physicalX, int physicalY, string message, string hexDotColor, int autoHideMs = 0)
    {
        ApplyContent(message, hexDotColor, autoHideMs);

        var screen = WinForms.Screen.FromPoint(new Drawing.Point(physicalX, physicalY));
        double scale = Win32Interop.GetDpiScaleAtPoint(physicalX, physicalY);
        Drawing.Rectangle work = screen.WorkingArea;

        double workLeft = work.Left / scale;
        double workTop = work.Top / scale;
        double workRight = work.Right / scale;
        double workBottom = work.Bottom / scale;

        double targetX = (physicalX / scale) + 14;
        double targetY = (physicalY / scale) + 16;

        MeasureAndPosition(targetX, targetY, workLeft, workTop, workRight, workBottom);
    }

    public void ShowBottomRight(string message, string hexDotColor, int autoHideMs = 1800)
    {
        ApplyContent(message, hexDotColor, autoHideMs);

        var screen = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
        Drawing.Rectangle work = screen.WorkingArea;
        double scale = Win32Interop.GetDpiScaleAtPoint(work.Right - 20, work.Bottom - 20);

        double workLeft = work.Left / scale;
        double workTop = work.Top / scale;
        double workRight = work.Right / scale;
        double workBottom = work.Bottom / scale;

        UpdateLayout();
        double width = ActualWidth > 0 ? ActualWidth : 220;
        double height = ActualHeight > 0 ? ActualHeight : 44;

        double targetX = workRight - width - 16;
        double targetY = workBottom - height - 16;

        MeasureAndPosition(targetX, targetY, workLeft, workTop, workRight, workBottom);
    }

    public void HidePill()
    {
        _autoHideTimer.Stop();
        if (IsVisible)
        {
            Hide();
        }
    }

    private void ApplyContent(string message, string hexDotColor, int autoHideMs)
    {
        _autoHideTimer.Stop();
        MessageText.Text = message;
        StatusDot.Fill = (SolidColorBrush)new BrushConverter().ConvertFromString(hexDotColor)!;

        if (autoHideMs > 0)
        {
            _autoHideTimer.Interval = TimeSpan.FromMilliseconds(autoHideMs);
            _autoHideTimer.Start();
        }
    }

    private void MeasureAndPosition(
        double targetX,
        double targetY,
        double workLeft,
        double workTop,
        double workRight,
        double workBottom)
    {
        if (!IsVisible)
        {
            Left = -10000;
            Top = -10000;
            Show();
        }

        UpdateLayout();
        double width = ActualWidth > 0 ? ActualWidth : 220;
        double height = ActualHeight > 0 ? ActualHeight : 44;

        if (targetX + width > workRight - 8)
        {
            targetX = workRight - width - 8;
        }

        if (targetY + height > workBottom - 8)
        {
            targetY = workBottom - height - 8;
        }

        if (targetX < workLeft + 8)
        {
            targetX = workLeft + 8;
        }

        if (targetY < workTop + 8)
        {
            targetY = workTop + 8;
        }

        Left = targetX;
        Top = targetY;
    }
}
