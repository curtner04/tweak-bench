using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TweakBench.Core;
using TweakBench.Notify;

namespace TweakBench.Tests
{
    /// <summary>Pure tests for the notification pop-up position feature: placement maths, settings and the status file. Nothing here touches the desktop, the registry or processes.</summary>
    public static class NotifyTests
    {
        public static void Run(T t)
        {
            RectTests(t);
            InsetTests(t);
            AnchorTests(t);
            CardRectTests(t);
            RoundTripTests(t);
            ClampTests(t);
            TargetTests(t);
            ResolveTests(t);
            PresetTests(t);
            DescribeTests(t);
            SpotSettingsTests(t);
            StatusFileTests(t);
            HelperNameTests(t);
        }

        // ---- fixtures (the monitors measured on the development PC) -----------------------------------------------

        static MonitorInfo Main3()
        {
            return new MonitorInfo
            {
                Id = @"\\?\DISPLAY#DEL4242#5&1&0&UID1#{e6f07b5f}", Device = @"\\.\DISPLAY3", Name = "DELL U2720Q", Number = 3, Primary = true, Dpi = 144,
                Bounds = new RectPx(0, 0, 3840, 2160), Work = new RectPx(0, 0, 3840, 2088),
            };
        }

        static MonitorInfo Left1()
        {
            return new MonitorInfo
            {
                Id = @"\\?\DISPLAY#GSM5B7F#5&2&0&UID2#{e6f07b5f}", Device = @"\\.\DISPLAY1", Name = "LG ULTRAGEAR", Number = 1, Dpi = 96,
                Bounds = new RectPx(-2560, -19, 0, 1421), Work = new RectPx(-2560, -19, 0, 1421),
            };
        }

        static MonitorInfo Above6()
        {
            return new MonitorInfo
            {
                Id = @"\\?\DISPLAY#GSM5B7F#5&3&0&UID3#{e6f07b5f}", Device = @"\\.\DISPLAY6", Name = "", Number = 6, Dpi = 96,
                Bounds = new RectPx(-1280, -1459, 1280, -19), Work = new RectPx(-1280, -1459, 1280, -67),
            };
        }

        static List<MonitorInfo> All() { return new List<MonitorInfo> { Left1(), Main3(), Above6() }; }

        static bool Near(double a, double b, double tolerance = 0.001) { return Math.Abs(a - b) <= tolerance; }

        // ---- RectPx ------------------------------------------------------------------------------------------------

        static void RectTests(T t)
        {
            var r = new RectPx(-10, 20, 90, 70);
            t.Equal(100, r.Width, "RectPx width");
            t.Equal(50, r.Height, "RectPx height");
            t.Check(!r.IsEmpty, "RectPx with area is not empty");
            t.Check(new RectPx(5, 5, 5, 9).IsEmpty && new RectPx(5, 5, 9, 5).IsEmpty && new RectPx().IsEmpty, "RectPx without area is empty");
            t.Equal(RectPx.FromSize(-10, 20, 100, 50), r, "FromSize matches the corner constructor");
            t.Check(r == RectPx.FromSize(-10, 20, 100, 50) && r != new RectPx(0, 0, 1, 1), "RectPx equality operators");
            t.Equal("-10,20,90,70", r.ToString(), "RectPx.ToString is left,top,right,bottom");
            RectPx back;
            t.Check(RectPx.TryParse("-10,20,90,70", out back) && back == r, "RectPx.TryParse reads ToString");
            t.Check(RectPx.TryParse(" 1 , 2 , 3 , 4 ", out back) && back == new RectPx(1, 2, 3, 4), "RectPx.TryParse ignores spaces");
            t.Check(!RectPx.TryParse("1,2,3", out back) && back.IsEmpty, "RectPx.TryParse rejects three numbers");
            t.Check(!RectPx.TryParse("a,b,c,d", out back), "RectPx.TryParse rejects text");
            t.Check(!RectPx.TryParse(null, out back) && !RectPx.TryParse("", out back), "RectPx.TryParse rejects null and empty");
        }

        // ---- insets --------------------------------------------------------------------------------------------------

        static void InsetTests(T t)
        {
            t.Check(NotificationPlacement.InsetLeft == 16 && NotificationPlacement.InsetTop == 30 && NotificationPlacement.InsetRight == 16 && NotificationPlacement.InsetBottom == 14,
                "insets are the measured 16/30/16/14 DIPs");

            // The pop-up window measured at 144 DPI on the main screen.
            var window = new RectPx(3246, 1833, 3840, 2088);
            RectPx card = NotificationPlacement.CardFromWindow(window, 144);
            t.Equal(new RectPx(3270, 1878, 3816, 2067), card, "card inside the 144-DPI window (24/45/24/21 px margins)");
            t.Equal(window, NotificationPlacement.WindowFromCard(card, 144), "window from card inverts card from window at 144 DPI");

            RectPx card96 = NotificationPlacement.CardFromWindow(new RectPx(100, 200, 500, 400), 96);
            t.Equal(new RectPx(116, 230, 484, 386), card96, "card inside a 96-DPI window (16/30/16/14 px margins)");
            t.Equal(new RectPx(100, 200, 500, 400), NotificationPlacement.WindowFromCard(card96, 96), "window from card at 96 DPI");

            foreach (int dpi in new[] { 96, 120, 144, 168, 192, 240 })
            {
                var w = new RectPx(-1500, -300, -900, -20);
                t.Equal(w, NotificationPlacement.WindowFromCard(NotificationPlacement.CardFromWindow(w, dpi), dpi), "window/card round trip at " + dpi + " DPI");
            }

            RectPx collapsed = NotificationPlacement.CardFromWindow(new RectPx(3246, 2088, 3840, 2088), 144);
            t.Check(collapsed.Width >= 0 && collapsed.Height >= 0, "a collapsed idle window gives a card that is never inverted");
            t.Equal(new RectPx(116, 230, 116, 230), NotificationPlacement.CardFromWindow(new RectPx(100, 200, 110, 210), 96), "a window smaller than its margins gives an empty card at its inner corner");
            t.Equal(NotificationPlacement.CardFromWindow(window, 96), NotificationPlacement.CardFromWindow(window, 0), "DPI 0 is treated as 96");
        }

        // ---- spot from card -------------------------------------------------------------------------------------------

        static void AnchorTests(T t)
        {
            MonitorInfo m = Main3(); // work 3840 x 2088 at 144 DPI
            Func<int, int, NotificationSpot> at = (cx, cy) => NotificationPlacement.SpotFromCard(RectPx.FromSize(cx - 100, cy - 50, 200, 100), m);

            t.Equal("left", at(100, 100).Horizontal, "centre in the first third is left");
            t.Equal("left", at(1279, 100).Horizontal, "just under a third is still left");
            t.Equal("center", at(1280, 100).Horizontal, "exactly one third is centre");
            t.Equal("center", at(1920, 100).Horizontal, "middle is centre");
            t.Equal("center", at(2559, 100).Horizontal, "just under two thirds is centre");
            t.Equal("right", at(2560, 100).Horizontal, "exactly two thirds is right");
            t.Equal("right", at(3700, 100).Horizontal, "last third is right");
            t.Equal("top", at(500, 100).Vertical, "upper half is top");
            t.Equal("top", at(500, 1043).Vertical, "just above the middle is top");
            t.Equal("bottom", at(500, 1044).Vertical, "the middle line is bottom");
            t.Equal("bottom", at(500, 2000).Vertical, "lower half is bottom");

            // Offsets: px to DIP at 144 DPI (1.5 px per DIP).
            var br = NotificationPlacement.SpotFromCard(new RectPx(3279, 1860, 3822, 2070), m);
            t.Equal("right", br.Horizontal, "bottom-right card anchors right");
            t.Equal("bottom", br.Vertical, "bottom-right card anchors bottom");
            t.Check(Near(br.OffsetX, 12) && Near(br.OffsetY, 12), "18 px at 144 DPI is 12 DIPs (got " + br.OffsetX + "," + br.OffsetY + ")");
            var tl = NotificationPlacement.SpotFromCard(new RectPx(30, 45, 573, 255), m);
            t.Check(tl.Horizontal == "left" && tl.Vertical == "top" && Near(tl.OffsetX, 20) && Near(tl.OffsetY, 30), "top-left card: 30 px / 45 px are 20 / 30 DIPs");
            var tc = NotificationPlacement.SpotFromCard(RectPx.FromSize(1649, 18, 543, 210), m);
            t.Check(tc.Horizontal == "center" && tc.Vertical == "top" && Near(tc.OffsetX, 0, 0.5) && Near(tc.OffsetY, 12), "top-centre card: centred offset is 0 (got " + tc.OffsetX + ")");
            var right = NotificationPlacement.SpotFromCard(RectPx.FromSize(1649 + 150, 18, 543, 210), m);
            t.Check(right.Horizontal == "center" && Near(right.OffsetX, 100, 0.5), "a card 150 px right of centre is +100 DIPs (got " + right.OffsetX + ")");
            var left = NotificationPlacement.SpotFromCard(RectPx.FromSize(1649 - 150, 18, 543, 210), m);
            t.Check(left.Horizontal == "center" && Near(left.OffsetX, -100, 0.5), "a card 150 px left of centre is -100 DIPs (got " + left.OffsetX + ")");

            t.Equal(m.Id, br.MonitorId, "spot records the monitor id");
            t.Equal(m.Device, br.MonitorDevice, "spot records the device name");
            t.Equal("DELL U2720Q", br.MonitorName, "spot records the friendly name");

            // 96-DPI monitor with negative coordinates (DISPLAY1 at -2560,-19): offsets are relative to its own work area.
            MonitorInfo l = Left1();
            var spot = NotificationPlacement.SpotFromCard(new RectPx(-2500, -9, -2138, 131), l);
            t.Check(spot.Horizontal == "left" && spot.Vertical == "top" && Near(spot.OffsetX, 60) && Near(spot.OffsetY, 10), "negative-coordinate monitor: offsets from its own work-area corner");
            var sr = NotificationPlacement.SpotFromCard(new RectPx(-400, 1200, -38, 1340), l);
            t.Check(sr.Horizontal == "right" && sr.Vertical == "bottom" && Near(sr.OffsetX, 38) && Near(sr.OffsetY, 81), "negative-coordinate monitor: bottom-right offsets (got " + sr.OffsetX + "," + sr.OffsetY + ")");
            MonitorInfo a = Above6();
            var up = NotificationPlacement.SpotFromCard(new RectPx(-1200, -1400, -840, -1260), a);
            t.Check(up.Horizontal == "left" && up.Vertical == "top" && Near(up.OffsetX, 80) && Near(up.OffsetY, 59), "monitor above the main screen: offsets from its own corner (got " + up.OffsetX + "," + up.OffsetY + ")");

            t.Throws<ArgumentNullException>(() => NotificationPlacement.SpotFromCard(new RectPx(0, 0, 10, 10), null), "SpotFromCard needs a monitor");
        }

        // ---- card from spot -------------------------------------------------------------------------------------------

        static void CardRectTests(T t)
        {
            MonitorInfo m = Main3();
            var spot = new NotificationSpot { Horizontal = "right", Vertical = "bottom", OffsetX = 12, OffsetY = 12 };
            t.Equal(RectPx.FromSize(3279, 1860, 543, 210), NotificationPlacement.CardRect(spot, m, 543, 210), "bottom-right: 12 DIPs = 18 px at 144 DPI from the work-area edges (taskbar excluded)");
            spot.Horizontal = "left"; spot.Vertical = "top";
            t.Equal(RectPx.FromSize(18, 18, 543, 210), NotificationPlacement.CardRect(spot, m, 543, 210), "top-left");
            spot.Horizontal = "center"; spot.OffsetX = 0;
            t.Equal(RectPx.FromSize(1649, 18, 543, 210), NotificationPlacement.CardRect(spot, m, 543, 210), "top-centre: centre 1920 minus half of 543, rounded");
            spot.OffsetX = 100;
            t.Equal(RectPx.FromSize(1799, 18, 543, 210), NotificationPlacement.CardRect(spot, m, 543, 210), "centre offset +100 DIPs = +150 px");
            spot.OffsetX = -100;
            t.Equal(RectPx.FromSize(1499, 18, 543, 210), NotificationPlacement.CardRect(spot, m, 543, 210), "centre offset -100 DIPs = -150 px");

            MonitorInfo l = Left1();
            var s96 = new NotificationSpot { Horizontal = "right", Vertical = "bottom", OffsetX = 12, OffsetY = 12 };
            t.Equal(RectPx.FromSize(-12 - 362, 1421 - 12 - 140, 362, 140), NotificationPlacement.CardRect(s96, l, 362, 140), "negative-coordinate monitor, bottom-right at 96 DPI");
            s96.Horizontal = "left"; s96.Vertical = "top";
            t.Equal(RectPx.FromSize(-2560 + 12, -19 + 12, 362, 140), NotificationPlacement.CardRect(s96, l, 362, 140), "negative-coordinate monitor, top-left at 96 DPI");

            t.Equal(NotificationPlacement.CardRect(new NotificationSpot(), m, 543, 210), NotificationPlacement.CardRect(null, m, 543, 210), "a null spot behaves like the default bottom-right spot");
            RectPx tiny = NotificationPlacement.CardRect(spot, m, 0, -5);
            t.Check(tiny.Width == 1 && tiny.Height == 1, "card size is at least 1 px");
            t.Throws<ArgumentNullException>(() => NotificationPlacement.CardRect(spot, null, 10, 10), "CardRect needs a monitor");

            var odd = new MonitorInfo { Work = new RectPx(0, 0, 1000, 800), Dpi = 0 };
            t.Equal(RectPx.FromSize(1000 - 12 - 300, 800 - 12 - 100, 300, 100), NotificationPlacement.CardRect(new NotificationSpot(), odd, 300, 100), "DPI 0 on a monitor is treated as 96");
        }

        static void RoundTripTests(T t)
        {
            var rng = new Random(20261111);
            int bad = 0, total = 0;
            foreach (MonitorInfo m in All())
            {
                // Presets and a spread of positions, with odd and even sizes.
                var cards = new List<RectPx>();
                foreach (string name in new[] { "top-left", "top-center", "top-right", "bottom-left", "bottom-center", "bottom-right" })
                    cards.Add(NotificationPlacement.CardRect(NotificationPlacement.Preset(name, m), m, 543, 210));
                for (int i = 0; i < 400; i++)
                {
                    int w = rng.Next(100, 700), h = rng.Next(60, 400);
                    int x = rng.Next(m.Work.Left, m.Work.Right - w + 1), y = rng.Next(m.Work.Top, m.Work.Bottom - h + 1);
                    cards.Add(RectPx.FromSize(x, y, w, h));
                }
                foreach (RectPx card in cards)
                {
                    NotificationSpot spot = NotificationPlacement.SpotFromCard(card, m);
                    RectPx again = NotificationPlacement.CardRect(spot, m, card.Width, card.Height);
                    total++;
                    if (again != card) { bad++; if (bad <= 3) t.Check(false, "round trip on " + m.Device + ": card " + card + " came back as " + again + " via " + spot.Horizontal + "/" + spot.Vertical + " " + spot.OffsetX + "," + spot.OffsetY); }
                }
            }
            t.Check(bad == 0, "SpotFromCard then CardRect returns the same card for " + total + " cards on 3 monitors (" + bad + " differed)");

            // The same, going through JSON like settings.json does.
            MonitorInfo main = Main3();
            var card2 = RectPx.FromSize(1234, 567, 543, 210);
            var s = new AppSettings { MoveNotifications = true, NotificationSpot = NotificationPlacement.SpotFromCard(card2, main) };
            AppSettings back = Json.Deserialize<AppSettings>(Json.SerializeIndented(s));
            back.Normalize();
            t.Equal(card2, NotificationPlacement.CardRect(back.NotificationSpot, main, 543, 210), "the spot survives a JSON round trip");
        }

        static void ClampTests(T t)
        {
            MonitorInfo m = Main3();
            var spot = new NotificationSpot { Horizontal = "right", Vertical = "bottom", OffsetX = 5000, OffsetY = 5000 };
            RectPx r = NotificationPlacement.CardRect(spot, m, 543, 210);
            t.Equal(new RectPx(0, 0, 543, 210), r, "a huge right/bottom offset is clamped to the top-left of the work area");
            spot = new NotificationSpot { Horizontal = "left", Vertical = "top", OffsetX = -5000, OffsetY = -5000 };
            t.Equal(new RectPx(0, 0, 543, 210), NotificationPlacement.CardRect(spot, m, 543, 210), "negative left/top offsets are clamped to the work area");
            spot = new NotificationSpot { Horizontal = "left", Vertical = "top", OffsetX = 5000, OffsetY = 5000 };
            t.Equal(RectPx.FromSize(3840 - 543, 2088 - 210, 543, 210), NotificationPlacement.CardRect(spot, m, 543, 210), "huge left/top offsets are clamped to the bottom-right of the work area");
            spot = new NotificationSpot { Horizontal = "center", Vertical = "top", OffsetX = 99999, OffsetY = 0 };
            t.Equal(3840 - 543, NotificationPlacement.CardRect(spot, m, 543, 210).Left, "a huge centre offset stops at the right edge");

            // Negative-coordinate monitors: the card never leaves the work area.
            foreach (MonitorInfo mon in new[] { Left1(), Above6() })
            {
                foreach (string h in new[] { "left", "center", "right" })
                    foreach (string v in new[] { "top", "bottom" })
                        foreach (double off in new[] { -3000.0, -50, 0, 12, 700, 5000 })
                        {
                            var sp = new NotificationSpot { Horizontal = h, Vertical = v, OffsetX = off, OffsetY = off };
                            RectPx card = NotificationPlacement.CardRect(sp, mon, 362, 140);
                            bool inside = card.Left >= mon.Work.Left && card.Top >= mon.Work.Top && card.Right <= mon.Work.Right && card.Bottom <= mon.Work.Bottom;
                            if (!inside) t.Check(false, "card " + card + " outside the work area of " + mon.Device + " for " + h + "/" + v + " " + off);
                        }
                t.Check(true, "cards stay inside the work area of " + mon.Device + " for every anchor and offset");
            }

            // A card larger than the work area is pinned to the top-left corner.
            var small = new MonitorInfo { Work = new RectPx(-500, -300, 100, 100), Dpi = 96 };
            t.Equal(RectPx.FromSize(-500, -300, 800, 600), NotificationPlacement.CardRect(new NotificationSpot(), small, 800, 600), "an oversized card is pinned to the top-left of the work area");
        }

        // ---- window target -------------------------------------------------------------------------------------------

        static void TargetTests(T t)
        {
            var spot = new NotificationSpot { Horizontal = "right", Vertical = "bottom", OffsetX = 12, OffsetY = 12 };
            var window = new RectPx(3246, 1833, 3840, 2088); // a 144 DPI pop-up as the shell places it on the main screen

            // Card size measured from the shell's own placement (live measurement on 26H2: about 544x189 px at 144 DPI).
            double cw, ch;
            NotificationPlacement.CardSizeDip(window, 144, out cw, out ch);
            t.Equal(364.0, cw, "card width in DIPs from a 594-px window at 144 DPI");
            t.Equal(126.0, ch, "card height in DIPs from a 255-px window at 144 DPI");
            NotificationPlacement.CardSizeDip(new RectPx(3246, 2088, 3840, 2088), 144, out cw, out ch);
            t.Equal((double)NotificationPlacement.FallbackCardWidthDip, cw, "a collapsed window gives the fallback width");
            t.Equal((double)NotificationPlacement.FallbackCardHeightDip, ch, "a collapsed window gives the fallback height");

            // Same DPI: the window keeps its size; the card (bottom-left anchored, 24/21 px in at 144 DPI) is 18 px from the corner.
            RectPx same = NotificationPlacement.TargetWindowRect(spot, Main3(), 364, 126, 594, 255, 144, true);
            t.Equal(new RectPx(3252, 1836, 3846, 2091), same, "same-DPI target keeps the window size");
            t.Equal(3840 - 18, same.Left + 24 + 546, "same-DPI card right edge is 18 px from the work-area right edge");
            t.Equal(2088 - 18, same.Bottom - 21, "same-DPI card bottom is 18 px above the work-area bottom");
            t.Equal(same, NotificationPlacement.TargetWindowRect(spot, Main3(), 364, 126, 594, 255, 144, false), "trimming changes nothing at the same DPI");

            // To a 96-DPI monitor: the shell draws a 364x126 card at the window's bottom-left (16 / 14 px in) and keeps the 255-px
            // height (a shorter window cuts the card off), so only the width is trimmed. Matches the live test on DISPLAY1.
            MonitorInfo l = Left1();
            RectPx trimmed = NotificationPlacement.TargetWindowRect(spot, l, 364, 126, 594, 255, 144, true);
            t.Equal(new RectPx(-392, 1168, 4, 1423), trimmed, "to a 96-DPI monitor: keep the height, trim the width to card + margins");
            t.Equal(-12, trimmed.Left + 16 + 364, "the card's right edge is 12 px from the monitor's right edge");
            t.Equal(1421 - 12, trimmed.Bottom - 14, "the card's bottom is 12 px above the monitor's bottom");
            RectPx untrimmed = NotificationPlacement.TargetWindowRect(spot, l, 364, 126, 594, 255, 144, false);
            t.Equal(new RectPx(-392, 1168, 202, 1423), untrimmed, "without trimming the window keeps the shell's width; the card lands in the same place");

            // From 96 to 144 DPI the card grows to 546x189 px and the window keeps its pixel height.
            RectPx up = NotificationPlacement.TargetWindowRect(spot, Main3(), 364, 126, 396, 170, 96, true);
            t.Equal(546 + 48, up.Width, "a 364-DIP card at 144 DPI needs a 594-px window");
            t.Equal(170, up.Height, "the window keeps the shell's pixel height");

            // Top-centre on the 96-DPI monitor above the main screen (negative coordinates): the card's top is 12 px below the top edge
            // and centred on x = 0; the transparent part of the window above it may reach past the monitor.
            var top = new NotificationSpot { Horizontal = "center", Vertical = "top", OffsetX = 0, OffsetY = 12 };
            RectPx high = NotificationPlacement.TargetWindowRect(top, Above6(), 364, 126, 594, 255, 144, true);
            t.Equal(-1459 + 12, high.Bottom - 14 - 126, "top-centre on DISPLAY6: the card starts 12 px below the top edge");
            t.Equal(0, high.Left + 16 + 182, "top-centre on DISPLAY6: the card is centred on x = 0");

            // Never empty, and odd inputs fall back to sensible sizes.
            RectPx fromNothing = NotificationPlacement.TargetWindowRect(spot, l, 0, 0, 0, 0, 0, true);
            t.Check(!fromNothing.IsEmpty, "no measurement and DPI 0 still give a usable target");
            t.Equal(NotificationPlacement.FallbackCardWidthDip + 32, fromNothing.Width, "fallback width is the fallback card plus its side margins");
            RectPx fromNaN = NotificationPlacement.TargetWindowRect(spot, l, double.NaN, double.NaN, 594, 255, 144, true);
            t.Check(!fromNaN.IsEmpty, "NaN card sizes fall back to the default card");
            t.Throws<ArgumentNullException>(() => NotificationPlacement.TargetWindowRect(spot, null, 364, 126, 594, 255, 144, true), "TargetWindowRect needs a destination");
        }

        // ---- resolve / presets / words --------------------------------------------------------------------------------

        static void ResolveTests(T t)
        {
            List<MonitorInfo> list = All();
            var byId = new NotificationSpot { MonitorId = list[0].Id.ToLowerInvariant(), MonitorDevice = @"\\.\DISPLAY3" };
            t.Equal(@"\\.\DISPLAY1", NotificationPlacement.Resolve(byId, list).Device, "id wins over device and ignores case");
            var byDevice = new NotificationSpot { MonitorId = "gone", MonitorDevice = @"\\.\display6" };
            t.Equal(@"\\.\DISPLAY6", NotificationPlacement.Resolve(byDevice, list).Device, "device name is the fallback (ignoring case)");
            var onlyDevice = new NotificationSpot { MonitorDevice = @"\\.\DISPLAY1" };
            t.Equal(@"\\.\DISPLAY1", NotificationPlacement.Resolve(onlyDevice, list).Device, "device name works without an id");
            var unknown = new NotificationSpot { MonitorId = "gone", MonitorDevice = @"\\.\DISPLAY9" };
            bool exact;
            t.Equal(@"\\.\DISPLAY3", NotificationPlacement.Resolve(unknown, list, out exact).Device, "an unknown monitor falls back to the main screen");
            t.Check(!exact, "fallback is reported as not exact");
            NotificationPlacement.Resolve(byId, list, out exact);
            t.Check(exact, "a found monitor is reported as exact");
            t.Equal(@"\\.\DISPLAY3", NotificationPlacement.Resolve(null, list).Device, "a null spot resolves to the main screen");
            t.Equal(@"\\.\DISPLAY3", NotificationPlacement.Resolve(new NotificationSpot(), list).Device, "an empty spot resolves to the main screen");

            var noPrimary = new List<MonitorInfo> { Left1(), Above6() };
            t.Equal(@"\\.\DISPLAY1", NotificationPlacement.Resolve(unknown, noPrimary).Device, "without a main screen the first monitor is used");
            t.Check(NotificationPlacement.Resolve(unknown, new List<MonitorInfo>()) == null, "an empty monitor list resolves to null");
            t.Check(NotificationPlacement.Resolve(unknown, null) == null, "a null monitor list resolves to null");
        }

        static void PresetTests(T t)
        {
            MonitorInfo m = Main3();
            string[] names = { "top-left", "top-center", "top-right", "bottom-left", "bottom-center", "bottom-right" };
            string[] horizontal = { "left", "center", "right", "left", "center", "right" };
            for (int i = 0; i < names.Length; i++)
            {
                NotificationSpot p = NotificationPlacement.Preset(names[i], m);
                t.Check(p != null, names[i] + " is a preset");
                if (p == null) continue;
                t.Equal(horizontal[i], p.Horizontal, names[i] + " horizontal anchor");
                t.Equal(i < 3 ? "top" : "bottom", p.Vertical, names[i] + " vertical anchor");
                t.Equal(horizontal[i] == "center" ? 0.0 : 12.0, p.OffsetX, names[i] + " horizontal offset");
                t.Equal(12.0, p.OffsetY, names[i] + " vertical offset");
                t.Equal(m.Id, p.MonitorId, names[i] + " carries the monitor id");
                t.Equal(m.Device, p.MonitorDevice, names[i] + " carries the device name");
                t.Equal(m.Name, p.MonitorName, names[i] + " carries the monitor name");
                RectPx card = NotificationPlacement.CardRect(p, m, 543, 210);
                t.Check(card.Left >= 0 && card.Right <= 3840 && card.Top >= 0 && card.Bottom <= 2088, names[i] + " is inside the work area");
            }
            t.Equal("top", NotificationPlacement.Preset("  TOP-Center ", m).Vertical, "preset names ignore case and spaces");
            t.Check(NotificationPlacement.Preset("middle", m) == null, "unknown preset name gives null");
            t.Check(NotificationPlacement.Preset("top-middle", m) == null, "unknown horizontal part gives null");
            t.Check(NotificationPlacement.Preset("left-top", m) == null, "reversed preset name gives null");
            t.Check(NotificationPlacement.Preset("top-left", null) == null, "preset without a monitor gives null");
            t.Check(NotificationPlacement.Preset(null, m) == null, "null preset name gives null");
            // The default spot is the bottom-right preset, which is where Windows already puts pop-ups.
            var def = new NotificationSpot();
            NotificationSpot br = NotificationPlacement.Preset("bottom-right", m);
            t.Check(def.Horizontal == br.Horizontal && def.Vertical == br.Vertical && def.OffsetX == br.OffsetX && def.OffsetY == br.OffsetY, "a new spot equals the bottom-right preset");
        }

        static void DescribeTests(T t)
        {
            List<MonitorInfo> list = All();
            MonitorInfo main = list[1], one = list[0], six = list[2];
            t.Equal("Top centre of screen 3 (main screen, DELL U2720Q)", NotificationPlacement.Describe(NotificationPlacement.Preset("top-center", main), list), "describe top centre of the main screen");
            t.Equal("Bottom centre of screen 3 (main screen, DELL U2720Q)", NotificationPlacement.Describe(NotificationPlacement.Preset("bottom-center", main), list), "describe bottom centre");
            t.Equal("12 px from the top-right corner of screen 1 (LG ULTRAGEAR)", NotificationPlacement.Describe(NotificationPlacement.Preset("top-right", one), list), "describe a corner of a secondary screen");
            t.Equal("12 px from the bottom-left corner of screen 3 (main screen, DELL U2720Q)", NotificationPlacement.Describe(NotificationPlacement.Preset("bottom-left", main), list), "describe bottom-left of the main screen");
            t.Equal("12 px from the bottom-right corner of screen 6", NotificationPlacement.Describe(NotificationPlacement.Preset("bottom-right", six), list), "a monitor without a name shows only its number");

            var custom = new NotificationSpot { MonitorId = one.Id, Horizontal = "left", Vertical = "bottom", OffsetX = 20.4, OffsetY = 40 };
            t.Equal("20 px from the left and 40 px from the bottom of screen 1 (LG ULTRAGEAR)", NotificationPlacement.Describe(custom, list), "describe different offsets");
            var shifted = new NotificationSpot { MonitorId = main.Id, Horizontal = "center", Vertical = "top", OffsetX = 150, OffsetY = 200 };
            t.Equal("200 px from the top and 150 px right of the centre of screen 3 (main screen, DELL U2720Q)", NotificationPlacement.Describe(shifted, list), "describe a shifted centre spot");
            shifted.OffsetX = -150;
            t.Check(NotificationPlacement.Describe(shifted, list).Contains("150 px left of the centre"), "describe a centre spot shifted left");
            var tall = new NotificationSpot { MonitorId = main.Id, Horizontal = "center", Vertical = "top", OffsetX = 0, OffsetY = 300 };
            t.Equal("300 px from the top, centred on screen 3 (main screen, DELL U2720Q)", NotificationPlacement.Describe(tall, list), "describe a centred spot far from the edge");

            var gone = new NotificationSpot { MonitorId = "gone", MonitorDevice = @"\\.\DISPLAY9", Horizontal = "right", Vertical = "top", OffsetX = 12, OffsetY = 12, MonitorName = "Old monitor" };
            string text = NotificationPlacement.Describe(gone, list);
            t.Check(text.Contains("(that screen is not connected: using the main screen)"), "describe a missing monitor says it is not connected (got: " + text + ")");
            t.Check(text.StartsWith("12 px from the top-right corner of screen 3"), "a missing monitor is described on the main screen (got: " + text + ")");
            t.Check(!text.Contains("DELL"), "the fallback text does not claim the old monitor's name");

            t.Equal("No spot has been chosen yet", NotificationPlacement.Describe(null, list), "describe no spot");
            t.Equal("No screens were found", NotificationPlacement.Describe(custom, new List<MonitorInfo>()), "describe with no screens");
            var garbage = new NotificationSpot { MonitorId = one.Id, Horizontal = "diagonal", Vertical = "sideways", OffsetX = double.NaN, OffsetY = 12 };
            t.Check(NotificationPlacement.Describe(garbage, list).Contains("bottom-right corner"), "describe repairs unknown anchors like Normalize");
            t.Equal("diagonal", garbage.Horizontal, "describe does not change the spot it was given");
        }

        // ---- settings -----------------------------------------------------------------------------------------------

        static void SpotSettingsTests(T t)
        {
            var defaults = new AppSettings();
            t.Check(!defaults.MoveNotifications, "notification moving is off by default");
            t.Check(defaults.NotificationSpot == null, "there is no spot until the user picks one");
            t.Check(defaults.NotificationTitles != null && defaults.NotificationTitles.Count == 0, "no learned titles by default");

            var spot = new NotificationSpot();
            t.Check(spot.Horizontal == "right" && spot.Vertical == "bottom" && spot.OffsetX == 12 && spot.OffsetY == 12, "spot defaults are bottom-right, 12 DIPs");

            // Normalize.
            var bad = new NotificationSpot { Horizontal = " LEFT ", Vertical = "Top", OffsetX = double.NaN, OffsetY = double.PositiveInfinity };
            bad.Normalize();
            t.Equal("left", bad.Horizontal, "anchors are lower-cased and trimmed");
            t.Equal("top", bad.Vertical, "vertical anchor is lower-cased");
            t.Equal(12.0, bad.OffsetX, "a NaN offset becomes 12");
            t.Equal(100000.0, bad.OffsetY, "an infinite offset is limited to 100000");
            bad.OffsetX = -1e300; bad.OffsetY = 1e300; bad.Normalize();
            t.Check(bad.OffsetX == -100000 && bad.OffsetY == 100000, "huge offsets are limited to +-100000");
            bad.OffsetX = double.NegativeInfinity; bad.Normalize();
            t.Equal(-100000.0, bad.OffsetX, "negative infinity is limited to -100000");
            var unknown = new NotificationSpot { Horizontal = "middle", Vertical = "centre" };
            unknown.Normalize();
            t.Check(unknown.Horizontal == "right" && unknown.Vertical == "bottom", "unknown anchors become right / bottom");
            var nulls = new NotificationSpot { Horizontal = null, Vertical = null };
            nulls.Normalize();
            t.Check(nulls.Horizontal == "right" && nulls.Vertical == "bottom", "null anchors become right / bottom");
            var fine = new NotificationSpot { Horizontal = "center", Vertical = "top", OffsetX = -37.5, OffsetY = 0 };
            fine.Normalize();
            t.Check(fine.Horizontal == "center" && fine.Vertical == "top" && fine.OffsetX == -37.5 && fine.OffsetY == 0, "valid values are kept");
            NotificationSpot copy = fine.Clone();
            copy.Horizontal = "left";
            t.Equal("center", fine.Horizontal, "Clone makes an independent copy");

            // AppSettings.Normalize.
            var s = new AppSettings
            {
                MoveNotifications = true,
                NotificationSpot = new NotificationSpot { Horizontal = "nonsense", OffsetX = double.NaN },
                NotificationTitles = new List<string> { "  Nueva notificación ", "", null, "   ", "nueva notificación", "Nouvelle notification", "Nueva notificación" },
            };
            s.Normalize();
            t.Equal("right", s.NotificationSpot.Horizontal, "AppSettings.Normalize repairs the spot");
            t.Equal(12.0, s.NotificationSpot.OffsetX, "AppSettings.Normalize repairs spot offsets");
            t.Check(s.NotificationTitles.SequenceEqual(new[] { "Nueva notificación", "Nouvelle notification" }), "titles are trimmed, blanks and duplicates (ignoring case) dropped, order kept (got: " + string.Join("|", s.NotificationTitles) + ")");
            s.NotificationTitles = null; s.Normalize();
            t.Check(s.NotificationTitles != null && s.NotificationTitles.Count == 0, "a null title list becomes empty");
            s.NotificationTitles = Enumerable.Range(0, 50).Select(i => "Title " + i).ToList(); s.Normalize();
            t.Equal(20, s.NotificationTitles.Count, "at most 20 titles are kept");
            t.Equal("Title 0", s.NotificationTitles[0], "the first titles are the ones kept");
            s.NotificationTitles = new List<string> { new string('x', 500) }; s.Normalize();
            t.Equal(200, s.NotificationTitles[0].Length, "a title is cut to 200 characters");
            s.NotificationSpot = null; s.Normalize();
            t.Check(s.NotificationSpot == null, "a null spot stays null");

            // JSON round trip through Save / Load.
            string folder = t.TempFolder();
            string file = Path.Combine(folder, "settings.json");
            var saved = new AppSettings
            {
                MoveNotifications = true,
                NotificationSpot = new NotificationSpot { MonitorId = @"\\?\DISPLAY#DEL4242#5&1&0&UID1#{e6f07b5f}", MonitorDevice = @"\\.\DISPLAY3", MonitorName = "DELL \"U2720Q\"", Horizontal = "center", Vertical = "top", OffsetX = -12.25, OffsetY = 33.5 },
                NotificationTitles = new List<string> { "Nueva notificación", "新しい通知" },
            };
            saved.Save(file);
            AppSettings loaded = AppSettings.Load(file);
            t.Check(loaded.MoveNotifications, "MoveNotifications survives Save/Load");
            NotificationSpot ls = loaded.NotificationSpot;
            t.Check(ls != null, "the spot survives Save/Load");
            if (ls != null)
            {
                t.Equal(saved.NotificationSpot.MonitorId, ls.MonitorId, "MonitorId survives Save/Load (backslashes and braces)");
                t.Equal(@"\\.\DISPLAY3", ls.MonitorDevice, "MonitorDevice survives Save/Load");
                t.Equal("DELL \"U2720Q\"", ls.MonitorName, "MonitorName with quotes survives Save/Load");
                t.Equal("center", ls.Horizontal, "Horizontal survives Save/Load");
                t.Equal("top", ls.Vertical, "Vertical survives Save/Load");
                t.Equal(-12.25, ls.OffsetX, "OffsetX survives Save/Load");
                t.Equal(33.5, ls.OffsetY, "OffsetY survives Save/Load");
            }
            t.Check(loaded.NotificationTitles.SequenceEqual(saved.NotificationTitles), "titles (non-ASCII) survive Save/Load");

            // Saving an unusable spot writes a usable file.
            var broken = new AppSettings { MoveNotifications = true, NotificationSpot = new NotificationSpot { OffsetX = double.NaN, OffsetY = double.PositiveInfinity, Horizontal = "x" } };
            string brokenFile = Path.Combine(folder, "broken.json");
            broken.Save(brokenFile);
            AppSettings repaired = AppSettings.Load(brokenFile);
            t.Check(repaired.NotificationSpot != null && repaired.NotificationSpot.OffsetX == 12 && repaired.NotificationSpot.OffsetY == 100000 && repaired.NotificationSpot.Horizontal == "right", "NaN and infinity never reach the file");

            // A settings file from before this feature loads with the defaults.
            string old = Path.Combine(folder, "old.json");
            File.WriteAllText(old, "{\"Schema\":1,\"Width\":1300,\"Height\":900,\"Theme\":\"dark\",\"PendingOn\":[\"a.b\"]}");
            AppSettings legacy = AppSettings.Load(old);
            t.Check(!legacy.MoveNotifications && legacy.NotificationSpot == null && legacy.NotificationTitles.Count == 0, "an older settings file loads with the option off");
            t.Equal("dark", legacy.Theme, "an older settings file keeps its other values");

            // Hand-written JSON with a partial spot.
            string partial = Path.Combine(folder, "partial.json");
            File.WriteAllText(partial, "{\"Schema\":1,\"MoveNotifications\":true,\"NotificationSpot\":{\"Horizontal\":\"left\"},\"NotificationTitles\":null}");
            AppSettings p = AppSettings.Load(partial);
            t.Check(p.NotificationSpot != null && p.NotificationSpot.Horizontal == "left" && p.NotificationSpot.Vertical == "bottom" && p.NotificationSpot.OffsetX == 12, "a partial spot gets defaults for the missing parts");
            t.Check(p.NotificationTitles != null && p.NotificationTitles.Count == 0, "a null titles list in the file becomes empty");
        }

        // ---- status file --------------------------------------------------------------------------------------------

        static void StatusFileTests(T t)
        {
            string folder = t.TempFolder();
            string file = Path.Combine(folder, "logs", "notifications-status.json");
            t.Check(NotificationStatusFile.Read(file) == null, "a missing status file reads as null");
            t.Check(NotificationStatusFile.Read(null) == null && NotificationStatusFile.Read("") == null, "a null or empty path reads as null");

            var status = new NotificationStatus
            {
                Pid = 4242, Version = "1.1.0", ExePath = @"C:\Users\Test\AppData\Local\Programs\Tweak Bench\Tweak Bench Notifications.exe",
                StartedUtc = "2026-11-11T08:00:00Z", State = NotificationStatus.StateMoving, Moved = 7, LastMovedUtc = "2026-11-11T08:05:00Z",
                LastToastTitle = "New notification", LastToastRect = new RectPx(3246, 1833, 3840, 2088).ToString(), LastTargetRect = new RectPx(1500, 18, 2094, 273).ToString(),
                LastError = null, HostFound = true, LastCardWidthDip = 364, LastCardHeightDip = 126.5,
            };
            t.Check(NotificationStatusFile.Write(file, status), "the status file is written (and its folder created)");
            t.Check(File.Exists(file), "the status file exists");
            t.Check(!string.IsNullOrEmpty(status.UpdatedUtc), "Write stamps UpdatedUtc");
            NotificationStatus read = NotificationStatusFile.Read(file);
            t.Check(read != null, "the status file reads back");
            if (read != null)
            {
                t.Equal(4242, read.Pid, "Pid round trip");
                t.Equal("1.1.0", read.Version, "Version round trip");
                t.Equal(status.ExePath, read.ExePath, "ExePath round trip (backslashes)");
                t.Equal("2026-11-11T08:00:00Z", read.StartedUtc, "StartedUtc round trip");
                t.Equal("moving", read.State, "State round trip");
                t.Equal(7, read.Moved, "Moved round trip");
                t.Equal("2026-11-11T08:05:00Z", read.LastMovedUtc, "LastMovedUtc round trip");
                t.Equal("New notification", read.LastToastTitle, "LastToastTitle round trip");
                t.Equal("3246,1833,3840,2088", read.LastToastRect, "LastToastRect round trip");
                t.Equal("1500,18,2094,273", read.LastTargetRect, "LastTargetRect round trip");
                t.Check(read.LastError == null, "a null error stays null");
                t.Check(read.HostFound, "HostFound round trip");
                t.Equal(364.0, read.LastCardWidthDip, "LastCardWidthDip round trip");
                t.Equal(126.5, read.LastCardHeightDip, "LastCardHeightDip round trip");
                t.Equal(status.UpdatedUtc, read.UpdatedUtc, "UpdatedUtc round trip");
                RectPx parsed;
                t.Check(RectPx.TryParse(read.LastTargetRect, out parsed) && parsed.Width == 594, "the rectangle strings parse back to rectangles");
            }

            // Overwrite and the temporary file is gone.
            status.State = NotificationStatus.StateStopped; status.Moved = 8; status.LastError = "Something \"odd\" happened";
            t.Check(NotificationStatusFile.Write(file, status), "the status file can be overwritten");
            read = NotificationStatusFile.Read(file);
            t.Check(read != null && read.State == "stopped" && read.Moved == 8 && read.LastError == "Something \"odd\" happened", "an overwritten status file reads the new values");
            t.Check(!Directory.GetFiles(Path.GetDirectoryName(file), "*.tmp").Any(), "no temporary files are left behind");

            // Unreadable content.
            File.WriteAllText(file, "{ not json");
            t.Check(NotificationStatusFile.Read(file) == null, "a corrupt status file reads as null");
            File.WriteAllText(file, "");
            t.Check(NotificationStatusFile.Read(file) == null, "an empty status file reads as null");
            t.Check(NotificationStatusFile.Write(file, status) && NotificationStatusFile.Read(file) != null, "a corrupt file is replaced by the next write");

            t.Check(!NotificationStatusFile.Write(file, null), "writing a null status returns false instead of throwing");
            t.Check(!NotificationStatusFile.Write(null, status), "writing to a null path returns false instead of throwing");

            // A path that cannot be written does not throw.
            string blocker = Path.Combine(folder, "blocker");
            File.WriteAllText(blocker, "x");
            t.Check(!NotificationStatusFile.Write(Path.Combine(blocker, "sub", "status.json"), status), "an unwritable location returns false instead of throwing");

            // The default location is under Paths.Logs (a sibling of the data folder).
            string oldData = Environment.GetEnvironmentVariable("TWEAKBENCH_DATA");
            try
            {
                string data = Path.Combine(t.TempFolder(), "data");
                Environment.SetEnvironmentVariable("TWEAKBENCH_DATA", data);
                string expected = Path.Combine(Path.GetDirectoryName(data), "logs", "notifications-status.json");
                t.Equal(expected, NotificationStatusFile.FilePath, "the status file lives in the machine-local logs folder");
                t.Check(NotificationStatusFile.Read() == null, "no status file yet in the default location");
                t.Check(NotificationStatusFile.Write(status) && File.Exists(expected), "the default-location write goes to the logs folder");
                NotificationStatus viaDefault = NotificationStatusFile.Read();
                t.Check(viaDefault != null && viaDefault.Pid == 4242, "the default-location read finds it");
                t.Check(!File.Exists(Path.Combine(data, "notifications-status.json")), "nothing is written into the data folder that gets exported");
            }
            finally { Environment.SetEnvironmentVariable("TWEAKBENCH_DATA", oldData); }

            t.Equal("2026-11-11T13:00:00Z", NotificationStatus.Stamp(new DateTime(2026, 11, 11, 13, 0, 0, DateTimeKind.Utc)), "Stamp formats UTC as ISO 8601");
        }

        // ---- helper names (pure) ------------------------------------------------------------------------------------

        static void HelperNameTests(T t)
        {
            t.Equal("Tweak Bench Notifications.exe", NotificationHelper.ExeName, "helper exe name");
            t.Equal("Tweak Bench Notifications", NotificationHelper.RunValueName, "Run value name");
            t.Check(NotificationHelper.MutexName.StartsWith(@"Local\") && NotificationHelper.ReloadEventName.StartsWith(@"Local\") && NotificationHelper.QuitEventName.StartsWith(@"Local\"), "helper objects live in the session namespace");
            t.Check(NotificationHelper.ReloadEventName != NotificationHelper.QuitEventName && NotificationHelper.ReloadEventName != NotificationHelper.MutexName, "helper object names differ");
            t.Equal(@"C:\Apps\Tweak Bench\Tweak Bench Notifications.exe", NotificationHelper.ExePathNextTo(@"C:\Apps\Tweak Bench\Tweak Bench.exe"), "helper sits next to the app exe");
            t.Equal("Tweak Bench Notifications.exe", NotificationHelper.ExePathNextTo("Tweak Bench.exe"), "helper path for a bare file name");
            t.Equal(1, Monitors.NumberOf(@"\\.\DISPLAY1"), "monitor number from the device name");
            t.Equal(12, Monitors.NumberOf(@"\\.\display12"), "monitor number ignores case");
            t.Equal(0, Monitors.NumberOf(""), "monitor number of nothing is 0");
            t.Equal(0, Monitors.NumberOf(null), "monitor number of null is 0");
            t.Equal("New notification", ToastWindow.DefaultTitle, "the English pop-up title is built in");
        }
    }
}
