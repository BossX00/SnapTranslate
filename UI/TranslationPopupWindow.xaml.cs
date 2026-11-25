using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SnapTranslate.Native;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace SnapTranslate.UI;

public partial class TranslationPopupWindow : Window
{
    private readonly DispatcherTimer _copyResetTimer;
    private DateTime _shownAtUtc;
    private string _originalText = string.Empty;
    private string _translatedText = string.Empty;
    private string _targetLang = "en";
    private IntPtr _targetHwnd;
    private double _currentScale = 1.0;

    public event Func<string, string, Task>? SwapRequested;
    public event Func<IntPtr, string, Task>? ReplaceRequested;

    public TranslationPopupWindow()
    {
        InitializeComponent();
        _copyResetTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1300)
        };
        _copyResetTimer.Tick += (_, _) =>
        {
            _copyResetTimer.Stop();
            CopyButton.Content = "คัดลอก";
        };
    }

    public void ShowLoading(int physicalX, int physicalY, IntPtr targetHwnd, string originalText, string targetLang)
    {
        _targetHwnd = targetHwnd;
        _originalText = originalText;
        _translatedText = string.Empty;
        _targetLang = targetLang;

        UpdateDirectionBadge(targetLang);

        string singleLinePreview = originalText.Replace("\r", " ").Replace("\n", " ").Trim();
        if (singleLinePreview.Length > 85)
        {
            singleLinePreview = singleLinePreview[..85] + "...";
        }
        SourcePreviewText.Text = $"ต้นฉบับ: {singleLinePreview}";

        LoadingPanel.Visibility = Visibility.Visible;
        ResultTextBox.Visibility = Visibility.Collapsed;
        ErrorTextBlock.Visibility = Visibility.Collapsed;
        CopyButton.IsEnabled = false;
        ReplaceButton.IsEnabled = false;
        SwapButton.IsEnabled = false;
        CopyButton.Content = "คัดลอก";

        PositionAtCursor(physicalX, physicalY);
        _shownAtUtc = DateTime.UtcNow;

        if (!IsVisible)
        {
            Show();
        }
        Activate();
    }

    public void ShowResult(string translatedText, string targetLang)
    {
        _translatedText = translatedText;
        _targetLang = targetLang;
        UpdateDirectionBadge(targetLang);

        LoadingPanel.Visibility = Visibility.Collapsed;
        ErrorTextBlock.Visibility = Visibility.Collapsed;
        ResultTextBox.Text = translatedText;
        ResultTextBox.Visibility = Visibility.Visible;

        CopyButton.IsEnabled = true;
        ReplaceButton.IsEnabled = _targetHwnd != IntPtr.Zero;
        SwapButton.IsEnabled = true;

        UpdateLayout();
        ClampInsideCurrentMonitor();
    }

    public void ShowError(string errorMessage)
    {
        _translatedText = string.Empty;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ResultTextBox.Visibility = Visibility.Collapsed;
        ErrorTextBlock.Text = errorMessage;
        ErrorTextBlock.Visibility = Visibility.Visible;

        CopyButton.IsEnabled = false;
        ReplaceButton.IsEnabled = false;
        SwapButton.IsEnabled = true;

        UpdateLayout();
        ClampInsideCurrentMonitor();
    }

    public bool ContainsPhysicalPoint(int physicalX, int physicalY)
    {
        if (!IsVisible)
        {
            return false;
        }

        double dipX = physicalX / _currentScale;
        double dipY = physicalY / _currentScale;
        return dipX >= Left && dipX <= Left + ActualWidth && dipY >= Top && dipY <= Top + ActualHeight;
    }

    private void UpdateDirectionBadge(string targetLang)
    {
        if (targetLang == "en")
        {
            DirectionText.Text = "TH → EN";
            DirectionBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
        }
        else
        {
            DirectionText.Text = "EN → TH";
            DirectionBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
        }
    }

    private void PositionAtCursor(int physicalX, int physicalY)
    {
        _currentScale = Win32Interop.GetDpiScaleAtPoint(physicalX, physicalY);
        var screen = WinForms.Screen.FromPoint(new Drawing.Point(physicalX, physicalY));
        Drawing.Rectangle work = screen.WorkingArea;

        double workLeft = work.Left / _currentScale;
        double workTop = work.Top / _currentScale;
        double workRight = work.Right / _currentScale;
        double workBottom = work.Bottom / _currentScale;

        double targetX = (physicalX / _currentScale) + 12;
        double targetY = (physicalY / _currentScale) + 14;

        double estWidth = Width;
        double estHeight = 165;

        if (targetX + estWidth > workRight - 8)
        {
            targetX = (physicalX / _currentScale) - estWidth - 12;
        }
        if (targetY + estHeight > workBottom - 8)
        {
            targetY = workBottom - estHeight - 8;
        }

        Left = Math.Max(workLeft + 8, Math.Min(targetX, workRight - estWidth - 8));
        Top = Math.Max(workTop + 8, Math.Min(targetY, workBottom - estHeight - 8));
    }

    private void ClampInsideCurrentMonitor()
    {
        int physX = (int)((Left + ActualWidth / 2) * _currentScale);
        int physY = (int)((Top + ActualHeight / 2) * _currentScale);
        var screen = WinForms.Screen.FromPoint(new Drawing.Point(physX, physY));
        Drawing.Rectangle work = screen.WorkingArea;

        double workLeft = work.Left / _currentScale;
        double workTop = work.Top / _currentScale;
        double workRight = work.Right / _currentScale;
        double workBottom = work.Bottom / _currentScale;

        if (Left + ActualWidth > workRight - 8)
        {
            Left = Math.Max(workLeft + 8, workRight - ActualWidth - 8);
        }
        if (Top + ActualHeight > workBottom - 8)
        {
            Top = Math.Max(workTop + 8, workBottom - ActualHeight - 8);
        }
    }

    private async void SwapButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_originalText) || SwapRequested is null)
        {
            return;
        }

        string nextTarget = _targetLang == "en" ? "th" : "en";
        _targetLang = nextTarget;
        UpdateDirectionBadge(nextTarget);
        LoadingPanel.Visibility = Visibility.Visible;
        ResultTextBox.Visibility = Visibility.Collapsed;
        ErrorTextBlock.Visibility = Visibility.Collapsed;
        SwapButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        ReplaceButton.IsEnabled = false;

        await SwapRequested.Invoke(_originalText, nextTarget);
    }

    private async void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_translatedText) || _targetHwnd == IntPtr.Zero || ReplaceRequested is null)
        {
            return;
        }

        string textToPaste = _translatedText;
        IntPtr hwnd = _targetHwnd;
        Hide();
        await ReplaceRequested.Invoke(hwnd, textToPaste);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_translatedText))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetDataObject(_translatedText, copy: true);
            CopyButton.Content = "✓ คัดลอกแล้ว";
            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
        catch
        {
            // Ignore transient clipboard lock
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if ((DateTime.UtcNow - _shownAtUtc).TotalMilliseconds < 250)
        {
            return;
        }

        if (IsVisible)
        {
            Hide();
        }
    }
}
