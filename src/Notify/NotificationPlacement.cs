using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TweakBench.Core;

namespace TweakBench.Notify
{
    /// <summary>A rectangle in physical screen pixels (Right and Bottom are exclusive edges, like a Win32 RECT).</summary>
    public struct RectPx : IEquatable<RectPx>
    {
        public int Left, Top, Right, Bottom;

        public RectPx(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }

        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
        public bool IsEmpty { get { return Right <= Left || Bottom <= Top; } }

        public static RectPx FromSize(int left, int top, int width, int height) { return new RectPx(left, top, left + width, top + height); }

        public bool Equals(RectPx other) { return Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom; }
        public override bool Equals(object obj) { return obj is RectPx && Equals((RectPx)obj); }
        public override int GetHashCode() { unchecked { return ((Left * 397 ^ Top) * 397 ^ Right) * 397 ^ Bottom; } }
        public static bool operator ==(RectPx a, RectPx b) { return a.Equals(b); }
        public static bool operator !=(RectPx a, RectPx b) { return !a.Equals(b); }

        /// <summary>"left,top,right,bottom", the form used in the status file.</summary>
        public override string ToString()
        {
            return Left.ToString(CultureInfo.InvariantCulture) + "," + Top.ToString(CultureInfo.InvariantCulture) + ","
                + Right.ToString(CultureInfo.InvariantCulture) + "," + Bottom.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Reads the <see cref="ToString"/> form. Returns false (and an empty rect) for anything else.</summary>
        public static bool TryParse(string text, out RectPx rect)
        {
            rect = new RectPx();
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split(',');
            if (parts.Length != 4) return false;
            var v = new int[4];
            for (int i = 0; i < 4; i++)
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return false;
            rect = new RectPx(v[0], v[1], v[2], v[3]);
            return true;
        }
    }

    /// <summary>One display. All rectangles are physical pixels.</summary>
    public sealed class MonitorInfo
    {
        /// <summary>Device interface path (stable across restarts); may be empty when Windows does not report one.</summary>
        public string Id;
        /// <summary>GDI device name such as \\.\DISPLAY3.</summary>
        public string Device;
        /// <summary>Friendly name such as "DELL U2720Q".</summary>
        public string Name;
        public RectPx Bounds, Work;
        /// <summary>Effective DPI (96 = 100%, 144 = 150%).</summary>
        public int Dpi = 96;
        public bool Primary;
        /// <summary>The n of \\.\DISPLAYn, used for labels ("screen 3"); 0 when unknown.</summary>
        public int Number;
    }

    /// <summary>
    /// Pure placement maths for notification pop-ups (no Windows calls; fully unit-tested). A pop-up is one transparent top-level window
    /// that contains a visible "card"; the card is what the user places. Spots are stored in DIPs (1/96 inch) relative to the work area
    /// of one monitor so they survive DPI and resolution changes.
    /// </summary>
    public static class NotificationPlacement
    {
        /// <summary>Transparent margin between the pop-up window and its visible card, in DIPs (measured on Windows 11 26H2).</summary>
        public static readonly double InsetLeft = 16, InsetTop = 30, InsetRight = 16, InsetBottom = 14;

        /// <summary>Card size in DIPs used when the real one is not known (an idle or collapsed pop-up window).</summary>
        public const int FallbackCardWidthDip = 362, FallbackCardHeightDip = 140;

        const double DefaultGapDip = 12;

        static int Safe(int dpi) { return dpi > 0 ? dpi : 96; }
        static int ToPx(double dip, int dpi) { return (int)Math.Round(dip * Safe(dpi) / 96.0, MidpointRounding.AwayFromZero); }
        static double ToDip(double px, int dpi) { return Math.Round(px * 96.0 / Safe(dpi), 2, MidpointRounding.AwayFromZero); }

        /// <summary>The visible card inside a pop-up window (the window shrunk by the transparent margins at <paramref name="dpi"/>).</summary>
        public static RectPx CardFromWindow(RectPx window, int dpi)
        {
            int left = window.Left + ToPx(InsetLeft, dpi), top = window.Top + ToPx(InsetTop, dpi);
            int right = window.Right - ToPx(InsetRight, dpi), bottom = window.Bottom - ToPx(InsetBottom, dpi);
            return new RectPx(left, top, Math.Max(left, right), Math.Max(top, bottom));
        }

        /// <summary>The pop-up window that has <paramref name="card"/> as its visible card (the inverse of <see cref="CardFromWindow"/>).</summary>
        public static RectPx WindowFromCard(RectPx card, int dpi)
        {
            return new RectPx(card.Left - ToPx(InsetLeft, dpi), card.Top - ToPx(InsetTop, dpi), card.Right + ToPx(InsetRight, dpi), card.Bottom + ToPx(InsetBottom, dpi));
        }

        /// <summary>
        /// The spot a card stands at: its anchor is the nearest third (left / centre / right) and half (top / bottom) of the monitor's
        /// work area, measured from the card's centre; offsets are the distances to that anchor in DIPs.
        /// </summary>
        public static NotificationSpot SpotFromCard(RectPx card, MonitorInfo monitor)
        {
            if (monitor == null) throw new ArgumentNullException("monitor");
            RectPx work = monitor.Work;
            int dpi = Safe(monitor.Dpi);
            double centerX = (card.Left + card.Right) / 2.0, centerY = (card.Top + card.Bottom) / 2.0;
            double relX = work.Width > 0 ? (centerX - work.Left) / work.Width : 1;
            double relY = work.Height > 0 ? (centerY - work.Top) / work.Height : 1;

            var spot = new NotificationSpot { MonitorId = monitor.Id, MonitorDevice = monitor.Device, MonitorName = monitor.Name };
            if (relX < 1.0 / 3) { spot.Horizontal = "left"; spot.OffsetX = ToDip(card.Left - work.Left, dpi); }
            else if (relX < 2.0 / 3) { spot.Horizontal = "center"; spot.OffsetX = ToDip(centerX - (work.Left + work.Right) / 2.0, dpi); }
            else { spot.Horizontal = "right"; spot.OffsetX = ToDip(work.Right - card.Right, dpi); }
            if (relY < 0.5) { spot.Vertical = "top"; spot.OffsetY = ToDip(card.Top - work.Top, dpi); }
            else { spot.Vertical = "bottom"; spot.OffsetY = ToDip(work.Bottom - card.Bottom, dpi); }
            spot.Normalize();
            return spot;
        }

        /// <summary>
        /// Where a card of the given size stands for <paramref name="spot"/> on <paramref name="monitor"/> (the inverse of
        /// <see cref="SpotFromCard"/>), moved if needed so the whole card is inside the work area (a card larger than the work area is
        /// pinned to its top-left corner).
        /// </summary>
        public static RectPx CardRect(NotificationSpot spot, MonitorInfo monitor, int cardWidthPx, int cardHeightPx)
        {
            if (monitor == null) throw new ArgumentNullException("monitor");
            var s = spot == null ? new NotificationSpot() : spot.Clone();
            s.Normalize();
            RectPx work = monitor.Work;
            int dpi = Safe(monitor.Dpi);
            int w = Math.Max(1, cardWidthPx), h = Math.Max(1, cardHeightPx);
            // Offsets stay fractional pixels until the single rounding at the end, so half-pixel centres round-trip exactly.
            double offsetX = s.OffsetX * dpi / 96.0, offsetY = s.OffsetY * dpi / 96.0;

            double left;
            if (s.Horizontal == "left") left = work.Left + offsetX;
            else if (s.Horizontal == "center") left = (work.Left + work.Right) / 2.0 + offsetX - w / 2.0;
            else left = work.Right - offsetX - w;
            double top;
            if (s.Vertical == "top") top = work.Top + offsetY;
            else top = work.Bottom - offsetY - h;

            int l = (int)Math.Round(left, MidpointRounding.AwayFromZero), t = (int)Math.Round(top, MidpointRounding.AwayFromZero);
            l = w >= work.Width ? work.Left : Math.Max(work.Left, Math.Min(l, work.Right - w));
            t = h >= work.Height ? work.Top : Math.Max(work.Top, Math.Min(t, work.Bottom - h));
            return RectPx.FromSize(l, t, w, h);
        }

        /// <summary>
        /// The card size in DIPs of a pop-up window that the shell itself placed and sized at <paramref name="dpi"/> (the DPI of the
        /// monitor it is on). A collapsed or tiny window gives the fallback size (<see cref="FallbackCardWidthDip"/> x <see cref="FallbackCardHeightDip"/>).
        /// </summary>
        public static void CardSizeDip(RectPx shellWindow, int dpi, out double widthDip, out double heightDip)
        {
            double scale = Safe(dpi) / 96.0;
            widthDip = Math.Round(shellWindow.Width / scale - InsetLeft - InsetRight, 2, MidpointRounding.AwayFromZero);
            heightDip = Math.Round(shellWindow.Height / scale - InsetTop - InsetBottom, 2, MidpointRounding.AwayFromZero);
            if (widthDip < 1 || heightDip < 1) { widthDip = FallbackCardWidthDip; heightDip = FallbackCardHeightDip; }
        }

        /// <summary>
        /// The window rectangle that puts the pop-up's card at <paramref name="spot"/> on <paramref name="dest"/>.
        /// <para>Layout measured on Windows 11 26H2: the shell draws the card (or a stack of cards) at the window's bottom-left corner,
        /// <see cref="InsetLeft"/> and <see cref="InsetBottom"/> DIPs in, at the DPI of the monitor the window is on, and it keeps the
        /// content's pixel height when the window is resized, so a shorter window cuts the card off. The window therefore keeps the
        /// height the shell gave it (<paramref name="windowHeightPx"/>). On a monitor with another DPI than <paramref name="sourceDpi"/>
        /// the card is drawn at that monitor's scale and the window is wider than it needs to be; with <paramref name="trimWidth"/> the
        /// width is cut to the card plus its side margins, because the transparent rest of the window still catches mouse clicks.</para>
        /// Never returns an empty rectangle.
        /// </summary>
        public static RectPx TargetWindowRect(NotificationSpot spot, MonitorInfo dest, double cardWidthDip, double cardHeightDip,
            int windowWidthPx, int windowHeightPx, int sourceDpi, bool trimWidth)
        {
            if (dest == null) throw new ArgumentNullException("dest");
            int from = Safe(sourceDpi), to = Safe(dest.Dpi);
            if (double.IsNaN(cardWidthDip) || double.IsNaN(cardHeightDip) || cardWidthDip < 1 || cardHeightDip < 1)
            {
                cardWidthDip = FallbackCardWidthDip;
                cardHeightDip = FallbackCardHeightDip;
            }
            int cw = Math.Max(1, ToPx(cardWidthDip, to)), ch = Math.Max(1, ToPx(cardHeightDip, to));
            RectPx card = CardRect(spot, dest, cw, ch);

            int sides = ToPx(InsetLeft, to) + ToPx(InsetRight, to);
            int width = windowWidthPx > 0 && !(trimWidth && to != from) ? windowWidthPx : cw + sides;
            int height = windowHeightPx > 0 ? windowHeightPx : ch + ToPx(InsetTop, to) + ToPx(InsetBottom, to);
            int left = card.Left - ToPx(InsetLeft, to);
            int bottom = card.Bottom + ToPx(InsetBottom, to);
            return RectPx.FromSize(left, bottom - height, Math.Max(1, width), Math.Max(1, height));
        }

        /// <summary>The monitor a spot refers to: by id (ignoring case), then by device name, else the main one (else the first). Null for an empty list.</summary>
        public static MonitorInfo Resolve(NotificationSpot spot, IList<MonitorInfo> monitors)
        {
            bool exact;
            return Resolve(spot, monitors, out exact);
        }

        /// <summary>Like <see cref="Resolve(NotificationSpot, IList{MonitorInfo})"/>; <paramref name="exact"/> is false when the main screen was used instead.</summary>
        public static MonitorInfo Resolve(NotificationSpot spot, IList<MonitorInfo> monitors, out bool exact)
        {
            exact = false;
            if (monitors == null || monitors.Count == 0) return null;
            if (spot != null)
            {
                if (!string.IsNullOrWhiteSpace(spot.MonitorId))
                {
                    var byId = monitors.FirstOrDefault(m => m != null && string.Equals(m.Id, spot.MonitorId, StringComparison.OrdinalIgnoreCase));
                    if (byId != null) { exact = true; return byId; }
                }
                if (!string.IsNullOrWhiteSpace(spot.MonitorDevice))
                {
                    var byDevice = monitors.FirstOrDefault(m => m != null && string.Equals(m.Device, spot.MonitorDevice, StringComparison.OrdinalIgnoreCase));
                    if (byDevice != null) { exact = true; return byDevice; }
                }
            }
            return monitors.FirstOrDefault(m => m != null && m.Primary) ?? monitors.FirstOrDefault(m => m != null);
        }

        /// <summary>One of "top-left", "top-center", "top-right", "bottom-left", "bottom-center", "bottom-right" (12 DIPs from the work-area edges). Null for any other name.</summary>
        public static NotificationSpot Preset(string name, MonitorInfo monitor)
        {
            if (monitor == null || name == null) return null;
            var spot = new NotificationSpot { MonitorId = monitor.Id, MonitorDevice = monitor.Device, MonitorName = monitor.Name, OffsetX = DefaultGapDip, OffsetY = DefaultGapDip };
            string[] parts = name.Trim().ToLowerInvariant().Split('-');
            if (parts.Length != 2) return null;
            if (parts[0] == "top" || parts[0] == "bottom") spot.Vertical = parts[0]; else return null;
            if (parts[1] == "left" || parts[1] == "right") spot.Horizontal = parts[1];
            else if (parts[1] == "center") { spot.Horizontal = "center"; spot.OffsetX = 0; }
            else return null;
            return spot;
        }

        /// <summary>The spot in plain words, for example "Top centre of screen 3 (main screen, DELL U2720Q)".</summary>
        public static string Describe(NotificationSpot spot, IList<MonitorInfo> monitors)
        {
            if (spot == null) return "No spot has been chosen yet";
            var s = spot.Clone();
            s.Normalize();
            bool exact;
            MonitorInfo monitor = Resolve(s, monitors, out exact);
            if (monitor == null) return "No screens were found";

            string screen = "screen " + (monitor.Number > 0 ? monitor.Number.ToString(CultureInfo.InvariantCulture) : "1");
            string details = "";
            if (!exact) details = " (that screen is not connected: using the main screen)";
            else
            {
                var bits = new List<string>();
                if (monitor.Primary) bits.Add("main screen");
                if (!string.IsNullOrWhiteSpace(monitor.Name)) bits.Add(monitor.Name.Trim());
                if (bits.Count > 0) details = " (" + string.Join(", ", bits) + ")";
            }

            string vertical = s.Vertical;
            int x = (int)Math.Round(Math.Max(0, Math.Abs(s.OffsetX)), MidpointRounding.AwayFromZero);
            int y = (int)Math.Round(Math.Max(0, s.OffsetY), MidpointRounding.AwayFromZero);
            string text;
            if (s.Horizontal == "center")
            {
                if (Math.Abs(s.OffsetX) < 0.5 && y <= 24) text = Capitalize(vertical) + " centre of " + screen;
                else if (Math.Abs(s.OffsetX) < 0.5) text = y + " px from the " + vertical + ", centred on " + screen;
                else text = y + " px from the " + vertical + " and " + x + " px " + (s.OffsetX > 0 ? "right" : "left") + " of the centre of " + screen;
            }
            else
            {
                int xo = (int)Math.Round(Math.Max(0, s.OffsetX), MidpointRounding.AwayFromZero);
                if (xo == y) text = xo + " px from the " + vertical + "-" + s.Horizontal + " corner of " + screen;
                else text = xo + " px from the " + s.Horizontal + " and " + y + " px from the " + vertical + " of " + screen;
            }
            return text + details;
        }

        static string Capitalize(string text) { return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1); }
    }
}
