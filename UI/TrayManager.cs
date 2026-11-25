using SnapTranslate.Models;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace SnapTranslate.UI;

public sealed class TrayManager : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly WinForms.ToolStripMenuItem _popupModeItem;
    private readonly WinForms.ToolStripMenuItem _typingModeItem;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;

    private Drawing.Icon? _currentIcon;
    private AppMode _mode;
    private bool _isPaused;
    private bool _disposed;

    public event Action<AppMode>? ModeChanged;
    public event Action<bool>? PauseChanged;
    public event Action<bool>? StartupChanged;
    public event Action? ExitRequested;

    public TrayManager(AppMode initialMode, bool initialStartupEnabled)
    {
        _mode = initialMode;
        _menu = new WinForms.ContextMenuStrip();

        _popupModeItem = new WinForms.ToolStripMenuItem("โหมดป๊อปอัป (แสดงหน้าต่างแปล)");
        _popupModeItem.Click += (_, _) => SelectMode(AppMode.Popup);

        _typingModeItem = new WinForms.ToolStripMenuItem("โหมดพิมพ์ (ตัด-แปล-วางอัตโนมัติ)");
        _typingModeItem.Click += (_, _) => SelectMode(AppMode.Typing);

        _pauseItem = new WinForms.ToolStripMenuItem("หยุดใช้งานชั่วคราว");
        _pauseItem.Click += (_, _) =>
        {
            _isPaused = !_isPaused;
            UpdateVisualState();
            PauseChanged?.Invoke(_isPaused);
        };

        _startupItem = new WinForms.ToolStripMenuItem("เปิดโปรแกรมอัตโนมัติเมื่อเปิดเครื่อง")
        {
            Checked = initialStartupEnabled
        };
        _startupItem.Click += (_, _) =>
        {
            bool next = !_startupItem.Checked;
            _startupItem.Checked = next;
            StartupChanged?.Invoke(next);
        };

        var helpItem = new WinForms.ToolStripMenuItem("วิธีใช้งาน");
        helpItem.Click += (_, _) => ShowHelpDialog();

        var exitItem = new WinForms.ToolStripMenuItem("ออกจากโปรแกรม");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            _popupModeItem,
            _typingModeItem,
            new WinForms.ToolStripSeparator(),
            _pauseItem,
            _startupItem,
            new WinForms.ToolStripSeparator(),
            helpItem,
            exitItem
        });

        _notifyIcon = new WinForms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Visible = true
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                AppMode nextMode = _mode == AppMode.Popup ? AppMode.Typing : AppMode.Popup;
                SelectMode(nextMode);
            }
        };

        UpdateVisualState();
    }

    public void SetMode(AppMode mode)
    {
        _mode = mode;
        UpdateVisualState();
    }

    private void SelectMode(AppMode mode)
    {
        if (_mode == mode && !_isPaused)
        {
            return;
        }

        _mode = mode;
        if (_isPaused)
        {
            _isPaused = false;
            PauseChanged?.Invoke(false);
        }

        UpdateVisualState();
        ModeChanged?.Invoke(mode);
    }

    private void UpdateVisualState()
    {
        _popupModeItem.Checked = _mode == AppMode.Popup;
        _typingModeItem.Checked = _mode == AppMode.Typing;
        _pauseItem.Checked = _isPaused;

        Drawing.Icon newIcon = TrayIconFactory.CreateIcon(_mode, _isPaused);
        Drawing.Icon? oldIcon = _currentIcon;
        _currentIcon = newIcon;
        _notifyIcon.Icon = newIcon;
        oldIcon?.Dispose();

        if (_isPaused)
        {
            _notifyIcon.Text = "SnapTranslate — หยุดชั่วคราว";
        }
        else if (_mode == AppMode.Popup)
        {
            _notifyIcon.Text = "SnapTranslate — โหมดป๊อปอัป (คลิกซ้ายเพื่อสลับโหมด)";
        }
        else
        {
            _notifyIcon.Text = "SnapTranslate — โหมดพิมพ์ (คลิกซ้ายเพื่อสลับโหมด)";
        }
    }

    private static void ShowHelpDialog()
    {
        const string message =
            "วิธีใช้งาน SnapTranslate (ไทย ↔ อังกฤษ)\n\n" +
            "1. โหมดป๊อปอัป (ไอคอนสีน้ำเงิน):\n" +
            "   • คลุมดำข้อความ แล้วกด Ctrl + คลิกขวา\n" +
            "   • เปิดหน้าต่างแสดงผลแปลภาษาอัตโนมัติ (คัดลอก / สลับภาษา / แทนที่ได้)\n\n" +
            "2. โหมดพิมพ์ (ไอคอนสีเขียว):\n" +
            "   • คลุมดำข้อความในช่องพิมพ์ แล้วกด Ctrl + คลิกขวา\n" +
            "   • โปรแกรมจะตัดข้อความ (Ctrl+X) แปลเบื้องหลัง และวางคำแปลกลับให้ทันที\n\n" +
            "3. เปลี่ยนโหมด:\n" +
            "   • คลิกซ้ายที่ไอคอนมุมขวาล่างเพื่อสลับโหมดทันที\n" +
            "   • หรือคลิกขวาเพื่อเลือกโหมด / หยุดชั่วคราว / ตั้งเปิดอัตโนมัติ";

        WinForms.MessageBox.Show(
            message,
            "วิธีใช้งาน SnapTranslate",
            WinForms.MessageBoxButtons.OK,
            WinForms.MessageBoxIcon.Information);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _currentIcon?.Dispose();
        _currentIcon = null;
        GC.SuppressFinalize(this);
    }
}
