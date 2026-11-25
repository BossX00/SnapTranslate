using System.Drawing.Drawing2D;
using SnapTranslate.Models;
using SnapTranslate.Native;
using Drawing = System.Drawing;

namespace SnapTranslate.UI;

public static class TrayIconFactory
{
    public static Drawing.Icon CreateIcon(AppMode mode, bool isPaused)
    {
        using var bitmap = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Drawing.Color.Transparent);

            Drawing.Color bgColor = isPaused
                ? Drawing.Color.FromArgb(255, 100, 116, 139) // Slate #64748B
                : mode == AppMode.Popup
                    ? Drawing.Color.FromArgb(255, 37, 99, 235) // Blue #2563EB
                    : Drawing.Color.FromArgb(255, 5, 150, 105); // Emerald #059669

            using var bgBrush = new Drawing.SolidBrush(bgColor);
            using var bgPath = CreateRoundedRectPath(new Drawing.RectangleF(2f, 2f, 28f, 28f), 7f);
            g.FillPath(bgBrush, bgPath);

            using var whiteBrush = new Drawing.SolidBrush(Drawing.Color.White);
            using var whitePen = new Drawing.Pen(Drawing.Color.White, 2.4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            if (isPaused)
            {
                // Crisp pause bars "||"
                using var bar1 = CreateRoundedRectPath(new Drawing.RectangleF(10f, 9f, 4f, 14f), 1.5f);
                using var bar2 = CreateRoundedRectPath(new Drawing.RectangleF(18f, 9f, 4f, 14f), 1.5f);
                g.FillPath(whiteBrush, bar1);
                g.FillPath(whiteBrush, bar2);
            }
            else if (mode == AppMode.Popup)
            {
                // Crisp vector "T" + accent dot
                using var topBar = CreateRoundedRectPath(new Drawing.RectangleF(8f, 8.5f, 16f, 3.6f), 1.5f);
                using var stem = CreateRoundedRectPath(new Drawing.RectangleF(14.1f, 9.5f, 3.8f, 14.5f), 1.5f);
                g.FillPath(whiteBrush, topBar);
                g.FillPath(whiteBrush, stem);

                using var accentBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 147, 197, 253));
                g.FillEllipse(accentBrush, 21.5f, 20.5f, 4.2f, 4.2f);
            }
            else
            {
                // Typing Mode: crisp vector diagonal pencil + swap underline
                var pencilPoints = new[]
                {
                    new Drawing.PointF(20.5f, 7.5f),
                    new Drawing.PointF(24.5f, 11.5f),
                    new Drawing.PointF(13.5f, 22.5f),
                    new Drawing.PointF(8.5f, 23.5f),
                    new Drawing.PointF(9.5f, 18.5f)
                };
                g.FillPolygon(whiteBrush, pencilPoints);

                // Subtle cut mark near nib + baseline
                g.DrawLine(whitePen, 16f, 24f, 24f, 24f);
            }
        }

        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            using var tempIcon = Drawing.Icon.FromHandle(hIcon);
            return (Drawing.Icon)tempIcon.Clone();
        }
        finally
        {
            Win32Interop.DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath CreateRoundedRectPath(Drawing.RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
