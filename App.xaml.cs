using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using SnapTranslate.Models;
using SnapTranslate.Native;
using SnapTranslate.Services;
using SnapTranslate.UI;

namespace SnapTranslate;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Global\SnapTranslate_SingleInstance_v1";

    private Mutex? _singleInstanceMutex;
    private GlobalMouseHook? _mouseHook;
    private TrayManager? _trayManager;
    private TranslationCoordinator? _coordinator;
    private TranslationPopupWindow? _popupWindow;
    private StatusPillWindow? _statusPill;
    private SettingsService? _settingsService;
    private AppSettings? _settings;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 1 && string.Equals(e.Args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
        {
            string reportPath = e.Args.Length >= 2 ? e.Args[1] : "self_test_report.txt";
            int exitCode = await RunSelfTestAsync(reportPath);
            Shutdown(exitCode);
            return;
        }

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "โปรแกรม SnapTranslate กำลังทำงานอยู่แล้วที่มุมขวาล่างของหน้าจอ (System Tray)",
                "SnapTranslate",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();

        var translateService = new GoogleTranslateService();
        _popupWindow = new TranslationPopupWindow();
        _statusPill = new StatusPillWindow();

        _coordinator = new TranslationCoordinator(
            _settings.Mode,
            translateService,
            _popupWindow,
            _statusPill);

        _mouseHook = new GlobalMouseHook
        {
            ShouldIgnoreAtPoint = (x, y) => _popupWindow.ContainsPhysicalPoint(x, y)
        };
        _mouseHook.CtrlRightClickTriggered += (x, y) => _coordinator.HandleCtrlRightClick(x, y);
        _mouseHook.Install();

        bool startupEnabled = _settingsService.GetStartWithWindows();
        _trayManager = new TrayManager(_settings.Mode, startupEnabled);

        _trayManager.ModeChanged += mode =>
        {
            _settings.Mode = mode;
            _settingsService.Save(_settings);
            _coordinator.CurrentMode = mode;

            if (mode == AppMode.Popup)
            {
                _statusPill.ShowBottomRight("เปลี่ยนเป็น: โหมดป๊อปอัป (แสดงหน้าต่างแปล)", "#3B82F6", 1800);
            }
            else
            {
                _popupWindow.Hide();
                _statusPill.ShowBottomRight("เปลี่ยนเป็น: โหมดพิมพ์ (ตัด-แปล-วางอัตโนมัติ)", "#10B981", 1800);
            }
        };

        _trayManager.PauseChanged += paused =>
        {
            _mouseHook.IsPaused = paused;
            if (paused)
            {
                _popupWindow.Hide();
                _statusPill.ShowBottomRight("SnapTranslate: หยุดใช้งานชั่วคราว", "#64748B", 1600);
            }
            else
            {
                string color = _settings.Mode == AppMode.Popup ? "#3B82F6" : "#10B981";
                string label = _settings.Mode == AppMode.Popup ? "โหมดป๊อปอัป" : "โหมดพิมพ์";
                _statusPill.ShowBottomRight($"กลับมาทำงานแล้ว ({label})", color, 1600);
            }
        };

        _trayManager.StartupChanged += enabled =>
        {
            _settingsService.SetStartWithWindows(enabled);
        };

        _trayManager.ExitRequested += () =>
        {
            Shutdown(0);
        };

        string initialMsg = _settings.Mode == AppMode.Popup
            ? "SnapTranslate พร้อมใช้งาน (โหมดป๊อปอัป)"
            : "SnapTranslate พร้อมใช้งาน (โหมดพิมพ์)";
        string initialColor = _settings.Mode == AppMode.Popup ? "#3B82F6" : "#10B981";
        _statusPill.ShowBottomRight(initialMsg, initialColor, 2000);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mouseHook?.Dispose();
        _coordinator?.Dispose();
        _trayManager?.Dispose();
        _popupWindow?.Close();
        _statusPill?.Close();

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
                // Ignore if mutex was not owned
            }
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private static async Task<int> RunSelfTestAsync(string reportPath)
    {
        var sb = new StringBuilder();
        bool allPassed = true;

        void Check(string name, bool condition, string detail = "")
        {
            string status = condition ? "PASS" : "FAIL";
            if (!condition)
            {
                allPassed = false;
            }
            sb.AppendLine($"[{status}] {name}{(string.IsNullOrEmpty(detail) ? "" : $" -> {detail}")}");
        }

        try
        {
            // 1. INPUT struct size check
            int expectedInputSize = IntPtr.Size == 8 ? 40 : 28;
            int actualInputSize = Marshal.SizeOf<Win32Interop.INPUT>();
            Check("Win32Interop.INPUT struct size", actualInputSize == expectedInputSize, $"actual={actualInputSize}, expected={expectedInputSize}");

            // 2. LanguageDetector.DetectDirection checks
            Check("DetectDirection pure Thai", LanguageDetector.DetectDirection("สวัสดีครับ สบายดีไหม") == ("th", "en"));
            Check("DetectDirection pure English", LanguageDetector.DetectDirection("Hello world, how are you?") == ("auto", "th"));
            Check("DetectDirection Thai with English tech terms", LanguageDetector.DetectDirection("ช่วยแก้ bug ใน React ให้หน่อย") == ("th", "en"));
            Check("DetectDirection English with 1 Thai word", LanguageDetector.DetectDirection("The word สวัสดี means hello in English") == ("auto", "th"));
            Check("DetectDirection numbers/punctuation", LanguageDetector.DetectDirection("12345 !@#") == ("auto", "th"));

            // 3. LanguageDetector.SplitOuterWhitespace checks
            var ws1 = LanguageDetector.SplitOuterWhitespace("  \nhello world\t ");
            Check("SplitOuterWhitespace preserves leading/trailing whitespace",
                ws1.Leading == "  \n" && ws1.Core == "hello world" && ws1.Trailing == "\t ",
                $"L='{ws1.Leading.Replace("\n", "\\n")}', C='{ws1.Core}', T='{ws1.Trailing.Replace("\t", "\\t")}'");

            var ws2 = LanguageDetector.SplitOuterWhitespace("   \r\n ");
            Check("SplitOuterWhitespace whitespace-only", ws2.Core == string.Empty);

            // 4. TrayIconFactory checks
            using (var popupIcon = TrayIconFactory.CreateIcon(AppMode.Popup, false))
            using (var typingIcon = TrayIconFactory.CreateIcon(AppMode.Typing, false))
            using (var pausedIcon = TrayIconFactory.CreateIcon(AppMode.Popup, true))
            {
                Check("TrayIconFactory creates Popup/Typing/Paused icons",
                    popupIcon.Width == 32 && typingIcon.Width == 32 && pausedIcon.Width == 32);
            }

            // 5. GlobalMouseHook install/uninstall check
            using (var hook = new GlobalMouseHook())
            {
                hook.Install();
                Check("GlobalMouseHook install/uninstall", hook.IsInstalled);
            }

            // 6. SettingsService save/load round-trip
            string tempSettings = Path.Combine(Path.GetTempPath(), $"rct_test_{Guid.NewGuid():N}.json");
            try
            {
                var settingsSvc = new SettingsService(tempSettings);
                settingsSvc.Save(new AppSettings { Mode = AppMode.Typing });
                var loaded = settingsSvc.Load();
                Check("SettingsService save/load round-trip", loaded.Mode == AppMode.Typing);
            }
            finally
            {
                if (File.Exists(tempSettings))
                {
                    File.Delete(tempSettings);
                }
            }

            // 7. GoogleTranslateService parser & live failover/cache checks
            var parsedAuto = GoogleTranslateService.ParseTranslateAT("[[\"สวัสดี\\nโลก\",\"en\"]]", "Hello\nworld", "auto", "th");
            Check("ParseTranslateAT auto-detect shape",
                parsedAuto.TranslatedText == "สวัสดี\nโลก" && parsedAuto.SourceLang == "en");

            var parsedExplicit = GoogleTranslateService.ParseTranslateAT("[\"Hello\\nWorld\"]", "สวัสดี\nโลก", "th", "en");
            Check("ParseTranslateAT explicit-lang shape",
                parsedExplicit.TranslatedText == "Hello\nWorld" && parsedExplicit.SourceLang == "th");

            var translateSvc = new GoogleTranslateService();

            var thToEn = await translateSvc.TranslateAsync("สวัสดีชาวโลก วันนี้อากาศดีมาก", "th", "en");
            Check("GoogleTranslateService TH -> EN",
                !string.IsNullOrWhiteSpace(thToEn.TranslatedText) &&
                thToEn.TranslatedText.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')),
                thToEn.TranslatedText);

            var thToEnCached = await translateSvc.TranslateAsync("สวัสดีชาวโลก วันนี้อากาศดีมาก", "th", "en");
            Check("GoogleTranslateService cache hit",
                ReferenceEquals(thToEn, thToEnCached));

            var enToTh = await translateSvc.TranslateAsync("Hello world, today the weather is very nice.", "auto", "th");
            Check("GoogleTranslateService EN -> TH",
                !string.IsNullOrWhiteSpace(enToTh.TranslatedText) &&
                enToTh.TranslatedText.Any(c => c >= '\u0E00' && c <= '\u0E7F'),
                enToTh.TranslatedText);

            var multiline = await translateSvc.TranslateAsync("Line one is here.\nLine two is below.", "auto", "th");
            Check("GoogleTranslateService multiline preservation",
                multiline.TranslatedText.Contains('\n'),
                multiline.TranslatedText.Replace("\n", "\\n"));
        }
        catch (Exception ex)
        {
            Check("Unhandled self-test exception", false, ex.ToString());
        }

        sb.AppendLine();
        sb.AppendLine(allPassed ? "ALL TESTS PASSED" : "SOME TESTS FAILED");

        string? dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        return allPassed ? 0 : 1;
    }
}
