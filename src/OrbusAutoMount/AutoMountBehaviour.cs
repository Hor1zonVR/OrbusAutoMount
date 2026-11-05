using System;
using UnityEngine;

namespace OrbusAutoMount;

/// <summary>
/// One native mount summon attempt per observed in-combat -> out-of-combat
/// transition. v0.1.1 includes bounded, always-on lifecycle diagnostics
/// so toggling Enabled in RebornMenu can be verified in a single game log.
/// </summary>
public sealed class AutoMountBehaviour : MonoBehaviour
{
    public AutoMountBehaviour(IntPtr pointer) : base(pointer) { }

    private int _playerId;
    private bool? _lastCombat;
    private float _nextPoll;
    private float _scheduledAt = -1f;
    private bool _waitedForCombat;

    private bool? _lastEnabled;
    private float _nextStatus;
    private int _statusCount;
    private bool _reportedUpdate;

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

        if (!_reportedUpdate)
        {
            _reportedUpdate = true;
            Plugin.Logger.LogInfo("[AutoMount/State] Update loop running. " +
                "RebornMenu Enabled changes will be reported here.");
        }

        bool enabled = Plugin.Enabled?.Value == true;
        if (_lastEnabled != enabled)
        {
            _lastEnabled = enabled;
            ResetSession();
            _nextStatus = 0f;
            Plugin.Logger.LogInfo("[AutoMount/State] Enabled changed to " +
                enabled + (enabled
                    ? "; waiting for the next full combat cycle."
                    : "; automatic summoning stopped."));
        }
        if (!enabled) return;

        try
        {
            object? player = AutoMountGame.LocalPlayer();
            if (player == null)
            {
                ReportStatus("waiting-for-player", null);
                ResetSession();
                return;
            }
            if (!AutoMountGame.IsActiveLocalPlayer(player))
            {
                ReportStatus("player-inactive", player);
                ResetSession();
                return;
            }

            int id = AutoMountGame.Identity(player);
            if (id == 0)
            {
                ReportStatus("player-identity-unavailable", player);
                ResetSession();
                return;
            }

            if (_playerId != id)
            {
                ResetSession();
                _playerId = id;
                Plugin.Logger.LogInfo("[AutoMount/State] Local character " +
                    "ready; now watching actual combat transitions.");
            }

            bool? inCombat = AutoMountGame.InCombat(player);
            if (!inCombat.HasValue || AutoMountGame.IsDead(player))
            {
                ReportStatus(inCombat.HasValue ? "dead" :
                    "combat-field-unavailable", player);
                _scheduledAt = -1f;
                _lastCombat = null;
                _waitedForCombat = false;
                return;
            }

            if (inCombat.Value)
            {
                if (_lastCombat != true)
                {
                    Plugin.Logger.LogInfo("[AutoMount/State] Combat START " +
                        "observed (local player).");
                }
                _lastCombat = true;
                _waitedForCombat = true;
                _scheduledAt = -1f;
                ReportStatus("in-combat", player);
                return;
            }

            if (_lastCombat == true && _waitedForCombat)
            {
                _scheduledAt = Time.realtimeSinceStartup +
                    Mathf.Clamp(Plugin.SummonDelay.Value, 0.25f, 5f);
                _waitedForCombat = false;
                Plugin.Logger.LogInfo("[AutoMount/State] Combat END " +
                    "observed; one native summon attempt scheduled.");
            }
            _lastCombat = false;

            if (_scheduledAt < 0f || Time.realtimeSinceStartup < _scheduledAt)
            {
                ReportStatus(_scheduledAt < 0f ? "idle-out-of-combat" :
                    "waiting-for-delay", player);
                return;
            }

            // Consume first; never retry repeatedly due to failure.
            _scheduledAt = -1f;
            object? avatar = AutoMountGame.Avatar(player);
            if (avatar == null)
            {
                Plugin.Logger.LogInfo("[AutoMount/State] Summon skipped: " +
                    "no active player avatar.");
                return;
            }

            bool? mounted = AutoMountGame.IsMounted(avatar);
            if (mounted != false)
            {
                Plugin.Logger.LogInfo("[AutoMount/State] Summon skipped: " +
                    (mounted == true ? "already mounted." :
                    "mounted-state property unavailable."));
                return;
            }

            if (!AutoMountGame.EquippedMount(avatar))
            {
                Plugin.Logger.LogInfo("[AutoMount/State] Summon skipped: " +
                    "equipped mount slot or base item unavailable.");
                return;
            }

            if (AutoMountGame.InCombat(player) != false ||
                AutoMountGame.IsDead(player))
            {
                Plugin.Logger.LogInfo("[AutoMount/State] Summon skipped: " +
                    "combat/death state changed during delay.");
                return;
            }

            if (AutoMountGame.TryNormalSummon(player, out string reason))
                Plugin.Logger.LogInfo("[AutoMount/State] Normal game " +
                    "summon invoked exactly once: " + reason);
            else
                Plugin.Logger.LogWarning("[AutoMount/State] Native summon " +
                    "method unavailable or failed: " + reason);

            ReportStatus("post-summon-attempt", player);
        }
        catch (Exception ex)
        {
            _scheduledAt = -1f;
            Plugin.Logger.LogWarning("[AutoMount/State] Update check " +
                "failed safely: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void ReportStatus(string phase, object? player)
    {
        // Once every 12 seconds, max 25 short reports per session.
        // No names, entity IDs, inventory contents or coordinates.
        if (_statusCount >= 25 ||
            Time.realtimeSinceStartup < _nextStatus) return;
        _nextStatus = Time.realtimeSinceStartup + 12f;
        _statusCount++;

        object? avatar = player == null ? null : AutoMountGame.Avatar(player);
        bool? combat = player == null ? null : AutoMountGame.InCombat(player);
        bool? mounted = avatar == null ? null : AutoMountGame.IsMounted(avatar);
        string fmt(bool? value) =>
            value.HasValue ? (value.Value ? "true" : "false") : "unknown";

        Plugin.Logger.LogInfo("[AutoMount/State] phase=" + phase +
            ", player=" + (player == null ? "missing" : "found") +
            ", playerActive=" + (player != null &&
                AutoMountGame.IsActiveLocalPlayer(player)) +
            ", combat=" + fmt(combat) +
            ", avatar=" + (avatar != null ? "found" : "missing") +
            ", mounted=" + fmt(mounted) +
            ", equippedMount=" +
                (avatar != null && AutoMountGame.EquippedMount(avatar)) +
            ", nativeSummonMethod=" + AutoMountGame.SummonMethodAvailable() +
            ".");
    }
}
