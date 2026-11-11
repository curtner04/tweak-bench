using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TweakBench.Core;
using TweakBench.Tools;

namespace TweakBench.App
{
    /// <summary>The DNS chooser shown at the top of the Network page.</summary>
    public static class DnsPanel
    {
        public static FrameworkElement Build(UiContext ctx)
        {
            return new DnsView(ctx).Root;
        }

        sealed class DnsView
        {
            readonly UiContext ctx;
            public readonly FrameworkElement Root;
            readonly StackPanel adapterHost = new StackPanel();
            readonly StackPanel resultHost = new StackPanel();
            readonly Dictionary<string, RadioButton> choices = new Dictionary<string, RadioButton>();
            readonly TextBlock summary;
            Button applyButton, undoButton, refreshButton;
            List<DnsAdapter> adapters = new List<DnsAdapter>();
            bool loading, started, busy;

            public DnsView(UiContext context)
            {
                ctx = context;
                var stack = new StackPanel();
                stack.Children.Add(Ui.Heading("DNS servers", "H2"));
                var intro = Ui.Para("DNS turns website names into addresses. The servers you use decide how fast pages start loading and whether known bad sites are blocked. "
                    + "Pick a provider and Tweak Bench applies it to every connected network adapter. It remembers what you had, so Undo puts it back.", "Text");
                intro.Margin = new Thickness(0, 4, 0, 10);
                stack.Children.Add(intro);

                var current = Ui.Text("Connected adapters", "Muted", true, 12);
                stack.Children.Add(current);
                adapterHost.Margin = new Thickness(0, 4, 0, 10);
                stack.Children.Add(adapterHost);
                summary = Ui.Text("", "Muted", false, 12);
                summary.Margin = new Thickness(0, 0, 0, 6);

                stack.Children.Add(Ui.Text("Choose a provider", "Muted", true, 12));
                var list = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                foreach (var provider in DnsTools.Providers)
                {
                    var p = provider;
                    var text = new StackPanel();
                    text.Children.Add(Ui.Text(p.Name, "Text", true, 13));
                    string detail = p.Blurb + (p.IsAutomatic ? "" : "  " + string.Join(", ", p.V4));
                    var blurb = Ui.Para(detail, "Muted", 12);
                    blurb.Margin = new Thickness(0, 1, 0, 0);
                    text.Children.Add(blurb);
                    var radio = new RadioButton { GroupName = "tb-dns", Content = text, Margin = new Thickness(0, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Top };
                    radio.Checked += (s, e) => { if (!loading) UpdateState(); };
                    choices[p.Id] = radio;
                    list.Children.Add(radio);
                }
                stack.Children.Add(list);

                applyButton = Ui.Button("Apply to all connected adapters", Apply, "Primary");
                undoButton = Ui.Button("Undo last change", Undo, "Ghost", "Puts back the DNS servers that were set before the last change from Tweak Bench");
                refreshButton = Ui.Button("Check again", () => Refresh(), "Ghost");
                var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var b in new[] { applyButton, undoButton, refreshButton }) { b.Margin = new Thickness(0, 0, 10, 6); buttons.Children.Add(b); }
                stack.Children.Add(buttons);
                stack.Children.Add(summary);
                stack.Children.Add(resultHost);
                if (!ctx.IsElevated)
                    stack.Children.Add(Ui.Note("Changing DNS needs administrator rights. Use \"Run as administrator\" in the sidebar. You can still see the current servers.", "Info"));

                var card = Ui.Card(stack);
                card.Margin = new Thickness(0, 0, 0, 16);
                Root = card;

                if (ctx.IsDemo)
                {
                    started = true;
                    adapters = new List<DnsAdapter>
                    {
                        new DnsAdapter { Name = "Ethernet 2", Index = 19, Description = "Intel(R) Ethernet Controller I226-V", V4 = new List<string> { "8.8.8.8", "8.8.4.4" }, StaticV4 = true },
                        new DnsAdapter { Name = "Wi-Fi", Index = 23, Description = "MediaTek Wi-Fi 7 MT7927", V4 = new List<string> { "192.168.1.1" } },
                    };
                    ShowAdapters();
                    Select("google");
                    UpdateState();
                }
                else
                {
                    UpdateState();
                    card.Loaded += (s, e) => { if (!started) { started = true; Refresh(); } };
                }
            }

            void Select(string id)
            {
                loading = true;
                RadioButton r;
                if (choices.TryGetValue(id, out r)) r.IsChecked = true;
                loading = false;
            }

            DnsProvider Chosen
            {
                get { foreach (var kv in choices) if (kv.Value.IsChecked == true) return DnsTools.Find(kv.Key); return null; }
            }

            void UpdateState()
            {
                bool havePrevious = DnsTools.LoadPrevious() != null;
                var chosen = Chosen;
                applyButton.IsEnabled = !busy && ctx.IsElevated && chosen != null && adapters.Count > 0;
                applyButton.ToolTip = ctx.IsElevated ? (chosen == null ? "Choose a provider first." : null) : "Needs administrator rights.";
                undoButton.IsEnabled = !busy && ctx.IsElevated && (havePrevious || ctx.IsDemo);
                undoButton.ToolTip = havePrevious || ctx.IsDemo ? (ctx.IsElevated ? "Puts back the DNS servers that were set before the last change from Tweak Bench." : "Needs administrator rights.") : "Nothing to undo yet.";
                refreshButton.IsEnabled = !busy;
            }

            void ShowAdapters()
            {
                adapterHost.Children.Clear();
                if (adapters.Count == 0)
                {
                    adapterHost.Children.Add(Ui.Para("No connected network adapter was found.", "Muted"));
                    return;
                }
                foreach (var a in adapters)
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var name = Ui.Text(a.Name, "Text", true, 13);
                    name.ToolTip = a.Description;
                    row.Children.Add(name);
                    var servers = Ui.Para(DnsTools.Describe(a), "Muted", 13);
                    Grid.SetColumn(servers, 1);
                    row.Children.Add(servers);
                    adapterHost.Children.Add(row);
                }
                var first = adapters.Select(DnsTools.Match).FirstOrDefault();
                bool same = first != null && adapters.All(a => DnsTools.Match(a) == first);
                summary.Text = same ? "Right now: " + first.Name + "." : "";
                if (same && !loading) Select(first.Id);
            }

            void Refresh()
            {
                if (busy) return;
                if (ctx.IsDemo) { ShowAdapters(); return; }
                busy = true;
                summary.Text = "Reading the network adapters...";
                UpdateState();
                List<DnsAdapter> found = null;
                ctx.RunInBackground("Reading DNS settings", () => { found = DnsTools.ReadAdapters(); }, () =>
                {
                    busy = false;
                    adapters = found ?? new List<DnsAdapter>();
                    ShowAdapters();
                    UpdateState();
                }, ex =>
                {
                    busy = false;
                    summary.Text = "Could not read the DNS settings: " + ex.Message;
                    UpdateState();
                });
            }

            void Apply()
            {
                var provider = Chosen;
                if (provider == null || adapters.Count == 0) return;
                var sb = new StringBuilder();
                sb.AppendLine(provider.IsAutomatic ? "Go back to automatic DNS (from your router) on these adapters?" : "Use " + provider.Name + " as the DNS on these adapters?");
                sb.AppendLine();
                foreach (var a in adapters) sb.AppendLine("   • " + a.Name + ": now " + DnsTools.Describe(a));
                if (!provider.IsAutomatic)
                {
                    sb.AppendLine();
                    sb.AppendLine("New servers: " + string.Join(", ", provider.V4) + " (IPv4) and " + string.Join(", ", provider.V6) + " (IPv6).");
                }
                sb.AppendLine();
                sb.AppendLine("Tweak Bench remembers the servers you had before its first change, so Undo can put them back.");
                if (!Ui.Confirm(ctx.Window, sb.ToString().TrimEnd(), "Change DNS servers")) return;

                if (ctx.IsDemo) { ToolsUi.ShowNote(resultHost, "Demo mode: nothing was changed.", "Info"); return; }
                busy = true;
                UpdateState();
                var targets = adapters.ToList();
                ctx.RunInBackground("Changing DNS servers", () => DnsTools.Apply(provider, targets), () =>
                {
                    busy = false;
                    ToolsUi.ShowNote(resultHost, provider.IsAutomatic ? "DNS is automatic again." : "DNS now uses " + provider.Name + ". Choose Undo last change to go back.", "Good");
                    ctx.SetStatus("DNS servers changed.");
                    Refresh();
                }, ex =>
                {
                    busy = false;
                    ToolsUi.ShowNote(resultHost, ex.Message, "Bad");
                    ctx.SetStatus("DNS change failed: " + ex.Message, true);
                    UpdateState();
                    if (ex.Message.StartsWith("Your network connections changed", StringComparison.Ordinal)) Refresh();
                });
            }

            void Undo()
            {
                if (ctx.IsDemo) { ToolsUi.ShowNote(resultHost, "Demo mode: nothing was changed.", "Info"); return; }
                var previous = DnsTools.LoadPrevious();
                if (previous == null) return;
                var sb = new StringBuilder();
                sb.AppendLine("Put back the DNS servers from " + previous.Saved + "?");
                sb.AppendLine();
                foreach (var a in previous.Adapters)
                {
                    var asAdapter = new DnsAdapter { Name = a.Name, Index = a.Index, V4 = a.V4, V6 = a.V6, StaticV4 = a.StaticV4, StaticV6 = a.StaticV6 };
                    sb.AppendLine("   • " + a.Name + ": " + DnsTools.Describe(asAdapter));
                }
                if (!Ui.Confirm(ctx.Window, sb.ToString().TrimEnd(), "Undo DNS change")) return;
                busy = true;
                UpdateState();
                ctx.RunInBackground("Restoring DNS servers", () => DnsTools.Undo(), () =>
                {
                    busy = false;
                    ToolsUi.ShowNote(resultHost, "The previous DNS servers are back.", "Good");
                    ctx.SetStatus("DNS servers restored.");
                    Refresh();
                }, ex =>
                {
                    busy = false;
                    ToolsUi.ShowNote(resultHost, ex.Message, "Bad");
                    ctx.SetStatus("DNS undo failed: " + ex.Message, true);
                    UpdateState();
                });
            }
        }
    }
}
