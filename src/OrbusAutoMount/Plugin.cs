using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace OrbusAutoMount;

[BepInPlugin("com.horizon.orbus.automount", "OrbusAutoMount", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<float> SummonDelay = null!;
    internal static ConfigEntry<bool> Diagnostics = null!;

    public override void Load()
    {
        Logger = Log;

        Enabled = Config.Bind("General", "Enabled", false,
            "Automatically summon your equipped mount ONCE after each " +
            "real transition from in-combat to out-of-combat. Off by default " +
            "until the native mount restrictions have been tested.");

        SummonDelay = Config.Bind("General", "SummonDelaySeconds", 0.9f,
            new ConfigDescription(
                "Time to wait after combat ends, allowing Orbus to clear " +
                "its combat lock before attempting a normal mount summon.",
                new AcceptableValueRange<float>(0.25f, 5f),
                "RebornMenu.Label=Auto-Mount Delay (seconds)"));

        // This section is hidden from RebornMenu's normal player UI.
        Diagnostics = Config.Bind("Diagnostics", "LogTransitions", false,
            "Log detected combat transitions, mount-state checks and " +
            "exactly one summon attempt per combat exit.");

        // BepInEx registers the IL2CPP MonoBehaviour and invokes its Update.
        AddComponent<AutoMountBehaviour>();
        Logger.LogInfo("[AutoMount] v0.1.0 loaded (Enabled=" + Enabled.Value +
            "). No forced mount state, no networking, no repeating summons. " +
            "Native client method mapping: " + AutoMountGame.DescribeMapping() + ".");
    }
}
