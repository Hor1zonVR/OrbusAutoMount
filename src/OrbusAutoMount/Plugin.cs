using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace OrbusAutoMount;

[BepInPlugin("com.horizon.orbus.automount", "OrbusAutoMount", "0.1.4")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<float> SummonDelay = null!;
    internal static ConfigEntry<float> LoadDelay = null!;
    internal static ConfigEntry<bool> Diagnostics = null!;

    public override void Load()
    {
        Logger = Log;

        Enabled = Config.Bind("General", "Enabled", false,
            "Maintain your mounted state whenever you are out of combat: " +
            "after logging in, changing areas, leaving combat, or becoming " +
            "unmounted while idle. Uses Orbus's normal summon action and " +
            "backs off if the game rejects it.");

        SummonDelay = Config.Bind("General", "SummonDelaySeconds", 0.9f,
            new ConfigDescription(
                "Time to wait after combat ends, allowing Orbus to clear " +
                "its combat lock before attempting a normal mount summon.",
                new AcceptableValueRange<float>(0.25f, 5f),
                "RebornMenu.Label=After-Combat Delay (seconds)"));

        LoadDelay = Config.Bind("General", "LoadDelaySeconds", 3.5f,
            new ConfigDescription(
                "Wait after the character and avatar load before summoning. " +
                "Helps avoid mounting during zone transitions.",
                new AcceptableValueRange<float>(1f, 15f),
                "RebornMenu.Label=After-Load Delay (seconds)"));

        // Kept for backwards compatibility. Diagnostic summaries are
        // enabled by default in v0.1.1 and already bounded; nothing
        // sensitive (names, IDs, inventory details) is logged.
        Diagnostics = Config.Bind("Diagnostics", "LogTransitions", false,
            "Legacy diagnostic toggle. Version 0.1.1 always logs bounded " +
            "state summaries and combat transitions for initial testing.");

        // BepInEx registers the IL2CPP MonoBehaviour and invokes its Update.
        AddComponent<AutoMountBehaviour>();
        Logger.LogInfo("[AutoMount] v0.1.4 loaded (Enabled=" + Enabled.Value +
            "). Maintain mount after login, zone changes, combat exits " +
            "and idle dismounts. Retry backoff 12-90s survives combat, " +
            "loading and brief mounted-state changes; a continuous 5s " +
            "mount clears backoff. No forced state or custom packets. " +
            "Client method mapping: " + AutoMountGame.DescribeMapping() + ".");
    }
}
