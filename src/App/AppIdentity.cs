namespace AppShared
{
    // Identity used by the shared sources in C:\dev\App Maintenance\shared\csharp.
    public static class AppIdentity
    {
        public const string Code = "TB";                                   // Configuration Transfer app ID
        public const string ShortName = "Tweak Bench";                     // used in user-facing messages
        public const string Version = TweakBench.App.Release.Version;      // the app's single version source
    }
}
