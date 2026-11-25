using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using SnapTranslate.Native;

namespace SnapTranslate.Services;

public sealed class ClipboardSnapshot
{
    public string? Text { get; init; }
    public string? Html { get; init; }
    public string? Rtf { get; init; }
    public StringCollection? FileDropList { get; init; }
    public BitmapSource? Image { get; init; }
    public bool IsEmpty => Text is null && Html is null && Rtf is null && FileDropList is null && Image is null;
}

public static class ClipboardHelper
{
    private const int MaxClipboardRetries = 8;
    private const int ClipboardRetryDelayMs = 15;

    public static async Task<ClipboardSnapshot?> CaptureSnapshotAsync()
    {
        return await RunOnDispatcherWithRetryAsync<ClipboardSnapshot?>(() =>
        {
            try
            {
                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    return new ClipboardSnapshot
                    {
                        FileDropList = System.Windows.Clipboard.GetFileDropList()
                    };
                }

                if (System.Windows.Clipboard.ContainsImage())
                {
                    return new ClipboardSnapshot
                    {
                        Image = System.Windows.Clipboard.GetImage()
                    };
                }

                if (System.Windows.Clipboard.ContainsText())
                {
                    string text = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
                    string? html = System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.Html)
                        ? System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Html)
                        : null;
                    string? rtf = System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.Rtf)
                        ? System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Rtf)
                        : null;

                    return new ClipboardSnapshot
                    {
                        Text = text,
                        Html = html,
                        Rtf = rtf
                    };
                }

                return new ClipboardSnapshot();
            }
            catch (COMException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }).ConfigureAwait(false);
    }

    public static async Task RestoreSnapshotAsync(ClipboardSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        await RunOnDispatcherWithRetryAsync(() =>
        {
            if (snapshot.IsEmpty)
            {
                System.Windows.Clipboard.Clear();
                return true;
            }

            if (snapshot.FileDropList is not null)
            {
                System.Windows.Clipboard.SetFileDropList(snapshot.FileDropList);
                return true;
            }

            if (snapshot.Image is not null)
            {
                System.Windows.Clipboard.SetImage(snapshot.Image);
                return true;
            }

            if (snapshot.Text is not null)
            {
                var dataObj = new System.Windows.DataObject();
                dataObj.SetText(snapshot.Text, System.Windows.TextDataFormat.UnicodeText);
                if (snapshot.Html is not null)
                {
                    dataObj.SetText(snapshot.Html, System.Windows.TextDataFormat.Html);
                }

                if (snapshot.Rtf is not null)
                {
                    dataObj.SetText(snapshot.Rtf, System.Windows.TextDataFormat.Rtf);
                }

                System.Windows.Clipboard.SetDataObject(dataObj, copy: true);
                return true;
            }

            return true;
        }).ConfigureAwait(false);
    }

    public static async Task<bool> SetTextAsync(string text)
    {
        return await RunOnDispatcherWithRetryAsync(() =>
        {
            System.Windows.Clipboard.SetDataObject(text, copy: true);
            return true;
        }).ConfigureAwait(false);
    }

    public static async Task<string?> CaptureSelectedTextAsync(bool cut, CancellationToken ct)
    {
        ushort key = cut ? Win32Interop.VK_X : Win32Interop.VK_C;
        uint seqBefore = Win32Interop.GetClipboardSequenceNumber();

        await Task.Delay(25, ct).ConfigureAwait(false);
        Win32Interop.SendCtrlCombo(key, forceCtrlPress: false);

        bool changed = await WaitForClipboardChangeAsync(seqBefore, 160, ct).ConfigureAwait(false);
        if (!changed)
        {
            // User may have released Ctrl right before the simulated key press; force full Ctrl+C / Ctrl+X combo.
            Win32Interop.SendCtrlCombo(key, forceCtrlPress: true);
            changed = await WaitForClipboardChangeAsync(seqBefore, 220, ct).ConfigureAwait(false);
        }

        if (!changed)
        {
            return null;
        }

        await Task.Delay(15, ct).ConfigureAwait(false);

        return await RunOnDispatcherWithRetryAsync<string?>(() =>
        {
            if (System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText))
            {
                return System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
            }

            return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
        }).ConfigureAwait(false);
    }

    private static async Task<bool> WaitForClipboardChangeAsync(uint seqBefore, int timeoutMs, CancellationToken ct)
    {
        int elapsed = 0;
        const int step = 15;
        while (elapsed < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            if (Win32Interop.GetClipboardSequenceNumber() != seqBefore)
            {
                return true;
            }

            await Task.Delay(step, ct).ConfigureAwait(false);
            elapsed += step;
        }

        return Win32Interop.GetClipboardSequenceNumber() != seqBefore;
    }

    private static async Task<T> RunOnDispatcherWithRetryAsync<T>(Func<T> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return default!;
        }

        for (int attempt = 0; attempt < MaxClipboardRetries; attempt++)
        {
            try
            {
                return await dispatcher.InvokeAsync(action).Task.ConfigureAwait(false);
            }
            catch (COMException) when (attempt < MaxClipboardRetries - 1)
            {
                await Task.Delay(ClipboardRetryDelayMs).ConfigureAwait(false);
            }
            catch (COMException)
            {
                return default!;
            }
        }

        return default!;
    }
}
