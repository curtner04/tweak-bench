using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>What every page needs from the window. Created once by MainWindow and passed to each page.</summary>
    public sealed class UiContext
    {
        public Window Window;
        public ISystem System;
        public Engine Engine;
        public HistoryStore History;
        public AppSettings Settings;
        public ProfileStore Profiles;
        /// <summary>Screenshot/demo mode: <see cref="System"/> is a MemorySystem. Pages must not start real tools (winget, DISM, SFC, PowerShell).</summary>
        public bool IsDemo;

        /// <summary>Shows a line in the window's status bar (error = red).</summary>
        public Action<string, bool> StatusLine = (text, error) => { };
        /// <summary>Selects a page by id ("home", a category id, "apps", "install", "features", "repair", "history").</summary>
        public Action<string> Navigate = id => { };
        /// <summary>Re-reads every tweak's state and refreshes the visible page and the pending-changes bar.</summary>
        public Action RefreshTweaks = () => { };
        /// <summary>Saves state and reopens Tweak Bench with administrator rights (asks Windows for permission).</summary>
        public Action RunAsAdmin = () => { };
        /// <summary>Saves <see cref="Settings"/> to settings.json (errors go to the status line).</summary>
        public Action SaveSettings = () => { };
        /// <summary>Raised on the UI thread whenever busy work starts or ends.</summary>
        public event Action BusyChanged = () => { };

        readonly List<string> busy = new List<string>();

        public Dispatcher Dispatcher { get { return Window.Dispatcher; } }
        public bool IsElevated { get { return System.IsElevated; } }

        /// <summary>True while any long operation runs (applying tweaks, SFC, winget...). Restart waits until it is false.</summary>
        public bool IsBusy { get { lock (busy) return busy.Count > 0; } }
        public string BusyText { get { lock (busy) return string.Join(", ", busy.Distinct()); } }

        public void SetStatus(string text, bool error = false) { OnUi(() => StatusLine(text, error)); }

        /// <summary>Runs on the UI thread (immediately when already on it).</summary>
        public void OnUi(Action action)
        {
            if (Dispatcher.CheckAccess()) action();
            else Dispatcher.BeginInvoke(action);
        }

        /// <summary>Marks work as running until the returned token is disposed. <paramref name="what"/> is shown when Restart has to wait.</summary>
        public IDisposable BeginBusy(string what)
        {
            lock (busy) busy.Add(what);
            OnUi(() => BusyChanged());
            return new BusyToken(this, what);
        }

        sealed class BusyToken : IDisposable
        {
            readonly UiContext ctx; readonly string what; bool done;
            public BusyToken(UiContext c, string w) { ctx = c; what = w; }
            public void Dispose()
            {
                if (done) return;
                done = true;
                lock (ctx.busy) ctx.busy.Remove(what);
                ctx.OnUi(() => ctx.BusyChanged());
            }
        }

        /// <summary>
        /// Runs <paramref name="work"/> on a background thread while marked busy, then calls <paramref name="done"/> (or
        /// <paramref name="failed"/>, default: status line) on the UI thread.
        /// </summary>
        public Task RunInBackground(string what, Action work, Action done = null, Action<Exception> failed = null)
        {
            IDisposable token = BeginBusy(what);
            return Task.Run(() =>
            {
                Exception error = null;
                try { work(); } catch (Exception ex) { error = ex; }
                OnUi(() =>
                {
                    token.Dispose();
                    if (error == null) { if (done != null) done(); }
                    else if (failed != null) failed(error);
                    else StatusLine(what + " failed: " + error.Message, true);
                });
            });
        }
    }

    /// <summary>A page in the sidebar. The window calls <see cref="Build"/> once, then <see cref="Shown"/> every time it is selected.</summary>
    public abstract class PageBase
    {
        protected readonly UiContext Ctx;
        protected PageBase(UiContext ctx) { Ctx = ctx; }

        public abstract string Id { get; }
        public abstract string Title { get; }
        /// <summary>One sentence under the title.</summary>
        public abstract string Subtitle { get; }
        /// <summary>Segoe Fluent Icons code point for the sidebar.</summary>
        public abstract string Glyph { get; }

        FrameworkElement root;
        /// <summary>The page content (built on first use). The window puts it inside its own scroll viewer.</summary>
        public FrameworkElement Root { get { if (root == null) root = Build(); return root; } }

        protected abstract FrameworkElement Build();

        /// <summary>The page was selected (refresh live data here).</summary>
        public virtual void Shown() { }

        /// <summary>Tweak states may have changed (after a batch, an undo, or Refresh).</summary>
        public virtual void TweaksChanged() { }

        /// <summary>Before Restart: copy staged selections into <see cref="UiContext.Settings"/> (the window saves them).</summary>
        public virtual void SavePending() { }
    }
}
