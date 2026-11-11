using System.Reflection;

// Version attributes come from the shared ReleaseAttributes.cs (AppIdentity.Version = Release.Version).
[assembly: AssemblyTitle("Tweak Bench")]
[assembly: AssemblyProduct("Tweak Bench")]
[assembly: AssemblyDescription("Windows tweaks explained in plain English, with live status and one-click undo.")]

namespace TweakBench.App
{
    /// <summary>The single authoritative version. Keep it equal to the newest heading in CHANGELOG.md.</summary>
    public static class Release
    {
        public const string Version = "1.1.0";
    }
}
