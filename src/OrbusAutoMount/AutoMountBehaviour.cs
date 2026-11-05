using System;
using UnityEngine;

namespace OrbusAutoMount;

/// <summary>
/// Detects ONLY a genuine in-combat -> out-of-combat transition of the
/// active local player. After a short delay, asks Orbus to summon the
/// equipped mount once. It does not attempt to restore prior mount state,
/// because AutoMount intentionally summons after EVERY combat exit.
/// </summary>
public sealed class AutoMountBehaviour : MonoBehaviour
{
    public AutoMountBehaviour(IntPtr pointer) : base(pointer) { }

    private int _playerId;
    private bool? _lastCombat;
    private float _nextPoll;
    private float _scheduledAt = -1f;
    private bool _waitedForCombat;

    private void ResetSession()
    {
        _playerId = 0;
        _lastCombat = null;
        _scheduledAt = -1f;
        _waitedForCombat = false;
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup < _nextPoll) return;
        _nextPoll = Time.realtimeSinceStartup + 0.15f;

        if (Plugin.Enabled == null || !Plugin.Enabled.Value)
        {
            if (_playerId != 0) ResetSession();
            return;
        }

        try
        {
            object? player = AutoMountGame.LocalPlayer();
            if (player == null || !AutoMountGame.IsActiveLocalPlayer(player))
            {
                if (_playerId != 0) ResetSession();
                return;
            }

            int id = AutoMountGame.Identity(player);
            if (id == 0)
            {
                ResetSession();
                return;
            }

            if (_playerId != id)
            {
                ResetSession();
                _playerId = id;
                Debug("New local player session; waiting to observe combat.");
            }

            bool? inCombat = AutoMountGame.InCombat(player);
            if (!inCombat.HasValue || AutoMountGame.IsDead(player))
            {
                // Unknown state or death should never trigger a summon.
                _scheduledAt = -1f;
                _lastCombat = null;
                _waitedForCombat = false;
                return;
            }

            if (inCombat.Value)
            {
                if (_lastCombat != true)
                    Debug("Entered combat. Pending summon cancelled.");
                _lastCombat = true;
                _waitedForCombat = true;
                _scheduledAt = -1f;
                return;
            }

            if (_lastCombat == true && _waitedForCombat)
            {
                _scheduledAt = Time.realtimeSinceStartup +
                    Mathf.Clamp(Plugin.SummonDelay.Value, 0.25f, 5f);
                _waitedForCombat = false;
                Debug("Combat ended. Native mount attempt queued.");
            }
            _lastCombat = false;

            if (_scheduledAt < 0f || Time.realtimeSinceStartup < _scheduledAt)
                return;

            // Consume the transition BEFORE invoking game code, so even
            // an exception or an invalid zone cannot cause rapid retries.
            _scheduledAt = -1f;

            object? avatar = AutoMountGame.Avatar(player);
            if (avatar == null)
            {
                Debug("Skipped: no loaded local avatar.");
                return;
            }

            bool? alreadyMounted = AutoMountGame.IsMounted(avatar);
            if (alreadyMounted != false)
            {
                Debug(alreadyMounted == true
                    ? "Skipped: already mounted."
                    : "Skipped: mounted state unavailable.");
                return;
            }

            if (!AutoMountGame.EquippedMount(avatar))
            {
                Debug("Skipped: no equipped mount item in mount slot.");
                return;
            }

            // A final combat re-check avoids summoning after another
            // monster puts the player back into combat.
            if (AutoMountGame.InCombat(player) != false ||
                AutoMountGame.IsDead(player))
            {
                Debug("Skipped: combat/death state changed.");
                return;
            }

            if (AutoMountGame.TryNormalSummon(player, out string reason))
                Plugin.Logger.LogInfo("[AutoMount] Combat ended: " +
                    reason + ". No further attempts until next combat exit.");
            else
                Plugin.Logger.LogWarning("[AutoMount] Could not invoke " +
                    "normal game summon: " + reason);
        }
        catch (Exception ex)
        {
            _scheduledAt = -1f;
            Plugin.Logger.LogWarning("[AutoMount] Safe failure while checking " +
                "local state: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void Debug(string message)
    {
        if (Plugin.Diagnostics?.Value == true)
            Plugin.Logger.LogInfo("[AutoMount/Trace] " + message);
    }
}
