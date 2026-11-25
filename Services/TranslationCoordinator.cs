using System.Windows;
using SnapTranslate.Models;
using SnapTranslate.Native;
using SnapTranslate.UI;

namespace SnapTranslate.Services;

public sealed class TranslationCoordinator : IDisposable
{
    private readonly GoogleTranslateService _translateService;
    private readonly TranslationPopupWindow _popupWindow;
    private readonly StatusPillWindow _statusPill;

    private AppMode _currentMode;
    private CancellationTokenSource? _popupCts;
    private bool _isTypingBusy;
    private bool _disposed;

    public TranslationCoordinator(
        AppMode initialMode,
        GoogleTranslateService translateService,
        TranslationPopupWindow popupWindow,
        StatusPillWindow statusPill)
    {
        _currentMode = initialMode;
        _translateService = translateService;
        _popupWindow = popupWindow;
        _statusPill = statusPill;

        _popupWindow.SwapRequested += HandleSwapRequestedAsync;
        _popupWindow.ReplaceRequested += HandleReplaceRequestedAsync;
    }

    public AppMode CurrentMode
    {
        get => _currentMode;
        set => _currentMode = value;
    }

    public void HandleCtrlRightClick(int physicalX, int physicalY)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(async () =>
        {
            if (_currentMode == AppMode.Popup)
            {
                await ExecutePopupModeAsync(physicalX, physicalY);
            }
            else
            {
                await ExecuteTypingModeAsync(physicalX, physicalY);
            }
        });
    }

    private async Task ExecutePopupModeAsync(int physicalX, int physicalY)
    {
        _popupCts?.Cancel();
        _popupCts?.Dispose();
        _popupCts = new CancellationTokenSource();
        CancellationToken ct = _popupCts.Token;

        IntPtr targetHwnd = Win32Interop.GetForegroundWindow();
        ClipboardSnapshot? snapshot = await ClipboardHelper.CaptureSnapshotAsync();
        string? capturedText = null;

        try
        {
            capturedText = await ClipboardHelper.CaptureSelectedTextAsync(cut: false, ct);
        }
        catch (OperationCanceledException)
        {
            await ClipboardHelper.RestoreSnapshotAsync(snapshot);
            return;
        }

        await ClipboardHelper.RestoreSnapshotAsync(snapshot);

        if (string.IsNullOrWhiteSpace(capturedText))
        {
            _statusPill.ShowNearCursor(
                physicalX,
                physicalY,
                "กรุณาคลุมดำข้อความก่อนกด Ctrl + คลิกขวา",
                "#F59E0B",
                1500);
            return;
        }

        string cleanText = capturedText.Trim();
        (string sourceLang, string targetLang) = LanguageDetector.DetectDirection(cleanText);

        _statusPill.HidePill();
        _popupWindow.ShowLoading(physicalX, physicalY, targetHwnd, cleanText, targetLang);

        try
        {
            TranslationResult result = await _translateService.TranslateAsync(cleanText, sourceLang, targetLang, ct);
            if (!ct.IsCancellationRequested)
            {
                _popupWindow.ShowResult(result.TranslatedText, targetLang);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                _popupWindow.ShowError($"ไม่สามารถแปลภาษาได้: {ex.Message}");
            }
        }
    }

    private async Task HandleSwapRequestedAsync(string originalText, string nextTargetLang)
    {
        _popupCts?.Cancel();
        _popupCts?.Dispose();
        _popupCts = new CancellationTokenSource();
        CancellationToken ct = _popupCts.Token;

        try
        {
            TranslationResult result = await _translateService.TranslateAsync(originalText, "auto", nextTargetLang, ct);
            if (!ct.IsCancellationRequested)
            {
                _popupWindow.ShowResult(result.TranslatedText, nextTargetLang);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                _popupWindow.ShowError($"ไม่สามารถแปลภาษาได้: {ex.Message}");
            }
        }
    }

    private async Task HandleReplaceRequestedAsync(IntPtr targetHwnd, string translatedText)
    {
        if (targetHwnd == IntPtr.Zero || string.IsNullOrEmpty(translatedText))
        {
            return;
        }

        ClipboardSnapshot? snapshot = await ClipboardHelper.CaptureSnapshotAsync();
        try
        {
            Win32Interop.SetForegroundWindow(targetHwnd);
            await Task.Delay(50);

            await ClipboardHelper.SetTextAsync(translatedText);
            await Task.Delay(25);
            Win32Interop.SendCtrlCombo(Win32Interop.VK_V, forceCtrlPress: true);
            await Task.Delay(140);
        }
        finally
        {
            await ClipboardHelper.RestoreSnapshotAsync(snapshot);
        }
    }

    private async Task ExecuteTypingModeAsync(int physicalX, int physicalY)
    {
        if (_isTypingBusy)
        {
            return;
        }

        _isTypingBusy = true;
        ClipboardSnapshot? snapshot = null;
        string? rawText = null;
        IntPtr targetHwnd = Win32Interop.GetForegroundWindow();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            CancellationToken ct = cts.Token;

            snapshot = await ClipboardHelper.CaptureSnapshotAsync();
            rawText = await ClipboardHelper.CaptureSelectedTextAsync(cut: true, ct);

            if (string.IsNullOrEmpty(rawText))
            {
                await ClipboardHelper.RestoreSnapshotAsync(snapshot);
                _statusPill.ShowNearCursor(
                    physicalX,
                    physicalY,
                    "ไม่พบข้อความที่คลุมดำ หรือช่องนี้แก้ไขไม่ได้",
                    "#F59E0B",
                    1600);
                return;
            }

            (string leading, string core, string trailing) = LanguageDetector.SplitOuterWhitespace(rawText);
            if (string.IsNullOrWhiteSpace(core))
            {
                if (Win32Interop.GetForegroundWindow() == targetHwnd)
                {
                    await ClipboardHelper.SetTextAsync(rawText);
                    await Task.Delay(25, ct);
                    Win32Interop.SendCtrlCombo(Win32Interop.VK_V, forceCtrlPress: true);
                    await Task.Delay(140, ct);
                }

                await ClipboardHelper.RestoreSnapshotAsync(snapshot);
                return;
            }

            (string sourceLang, string targetLang) = LanguageDetector.DetectDirection(core);
            string progressLabel = targetLang == "en"
                ? "กำลังแปล... (TH → EN)"
                : "กำลังแปล... (EN → TH)";
            _statusPill.ShowNearCursor(physicalX, physicalY, progressLabel, "#3B82F6", 0);

            TranslationResult result = await _translateService.TranslateAsync(core, sourceLang, targetLang, ct);
            string finalText = leading + result.TranslatedText + trailing;

            await ClipboardHelper.SetTextAsync(finalText);

            if (Win32Interop.GetForegroundWindow() == targetHwnd)
            {
                await Task.Delay(25, ct);
                Win32Interop.SendCtrlCombo(Win32Interop.VK_V, forceCtrlPress: true);
                await Task.Delay(140, ct);
                await ClipboardHelper.RestoreSnapshotAsync(snapshot);

                string doneLabel = targetLang == "en"
                    ? "✓ แปลและวางแล้ว (TH → EN)"
                    : "✓ แปลและวางแล้ว (EN → TH)";
                _statusPill.ShowNearCursor(physicalX, physicalY, doneLabel, "#10B981", 1100);
            }
            else
            {
                // User switched away from the original window; keep translated text on clipboard.
                _statusPill.ShowNearCursor(
                    physicalX,
                    physicalY,
                    "แปลเสร็จแล้ว (คัดลอกไว้ในคลิปบอร์ด)",
                    "#10B981",
                    1800);
            }
        }
        catch
        {
            // Critical recovery: restore the cut text back into the user's input if translation failed.
            if (!string.IsNullOrEmpty(rawText))
            {
                try
                {
                    await ClipboardHelper.SetTextAsync(rawText);
                    if (Win32Interop.GetForegroundWindow() == targetHwnd)
                    {
                        await Task.Delay(25);
                        Win32Interop.SendCtrlCombo(Win32Interop.VK_V, forceCtrlPress: true);
                        await Task.Delay(140);
                    }
                }
                catch
                {
                    // Best-effort recovery
                }
            }

            await ClipboardHelper.RestoreSnapshotAsync(snapshot);
            _statusPill.ShowNearCursor(
                physicalX,
                physicalY,
                "แปลไม่สำเร็จ (คืนข้อความเดิมแล้ว)",
                "#EF4444",
                2200);
        }
        finally
        {
            _isTypingBusy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _popupCts?.Cancel();
        _popupCts?.Dispose();
        _popupWindow.SwapRequested -= HandleSwapRequestedAsync;
        _popupWindow.ReplaceRequested -= HandleReplaceRequestedAsync;
        GC.SuppressFinalize(this);
    }
}
