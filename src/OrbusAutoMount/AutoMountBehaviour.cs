using System;
using UnityEngine;

namespace OrbusAutoMount;

/// <summary>
/// v0.1.3: Maintain the mounted state whenever the local character is alive,
/// loaded and out of combat. This covers fresh logins, zone/instance loads,
/// combat exits, and later dismounts without requiring another fight.
///
/// All actions use Orbus's normal SummonMount method, with a stable-world
/// grace period, active-session guards and exponential retry cooldown.
/// </summary>
public sealed class AutoMountBehaviour : MonoBehaviour
{
    public AutoMountBehaviour(IntPtr pointer) : base(pointer) { }

    private const float PollSeconds = 0.2f;
    private const float WorldReadyGraceSeconds = 3.5f;
    private const float DismountGraceSeconds = 2.5f;
    private const float FirstRetrySeconds = 12f;
    private const float MaxRetrySeconds = 90f;

    private int _playerId;
    private int _avatarId;
    private bool? _lastCombat;
    private bool? _lastMounted;
    private float _readyAt = -1f;
    private float _nextAttemptAt;
    private int _attemptStreak;
    private float _nextPoll;

    private bool? _lastEnabled;
    private float _nextStatus;
    private int _statusCount;
    private bool _reportedUpdate;

    private void ResetSession()
    {
        _playerId = 0;
        _avatarId = 0;
        _lastCombat = null;
        _lastMounted = null;
        _readyAt = -1f;
        _nextAttemptAt = 0f;
        _attemptStreak = 0;
    }

    private void Update()
    {
        float now = Time.realtimeSinceStartup;
        if (now < _nextPoll) return;
        _nextPoll = now + PollSeconds;

        if (!_reportedUpdate)
        {
            _reportedUpdate = true;
            Plugin.Logger.LogInfo("[AutoMount/State] Update loop running.");
        }

        bool enabled = Plugin.Enabled?.Value == true;
        if (_lastEnabled != enabled)
        {
            _lastEnabled = enabled;
            ResetSession();
            _nextStatus = 0f;
            Plugin.Logger.LogInfo("[AutoMount/State] Enabled changed to " +
                enabled + (enabled
                    ? "; will maintain mount when safely out of combat."
                    : "; automatic mounting stopped."));
        }
        if (!enabled) return;

        try
        {
            object? player = AutoMountGame.LocalPlayer();
            if (player == null || !AutoMountGame.IsActiveLocalPlayer(player))
            {
                ReportStatus(player == null ? "waiting-for-player" :
                    "player-inactive", player);
                if (_playerId != 0) ResetSession();
                return;
            }

            int playerId = AutoMountGame.Identity(player);
            if (playerId == 0)
            {
                ResetSession();
                ReportStatus("player-identity-unavailable", player);
                return;
            }

            if (_playerId != playerId)
            {
                ResetSession();
                _playerId = playerId;
                Plugin.Logger.LogInfo("[AutoMount/State] New local character " +
                    "session; checking mount state after world settles.");
            }

            object? avatar = AutoMountGame.Avatar(player);
            if (avatar == null || !AutoMountGame.IsActiveAvatar(avatar))
            {
                _avatarId = 0;
                _lastMounted = null;
                _readyAt = -1f;
                _nextAttemptAt = 0f;
                _attemptStreak = 0;
                ReportStatus(avatar == null ? "waiting-for-avatar" :
                    "avatar-inactive", player);
                return;
            }

            int avatarId = AutoMountGame.Identity(avatar);
            if (avatarId == 0)
            {
                _avatarId = 0;
                _readyAt = -1f;
                ReportStatus("avatar-identity-unavailable", player);
                return;
            }

            if (_avatarId != avatarId)
            {
                _avatarId = avatarId;
                _lastCombat = null;
                _lastMounted = null;
                _attemptStreak = 0;
                _readyAt = now + WorldReadyGraceSeconds;
                _nextAttemptAt = _readyAt;
                Plugin.Logger.LogInfo("[AutoMount/State] Avatar ready; " +
                    "waiting " + WorldReadyGraceSeconds + " seconds after " +
                    "load before first idle mount attempt.");
            }

            bool? combat = AutoMountGame.InCombat(player);
            if (!combat.HasValue || AutoMountGame.IsDead(player))
            {
                _lastCombat = null;
                _lastMounted = null;
                _readyAt = now + WorldReadyGraceSeconds;
                _nextAttemptAt = Math.Max(_nextAttemptAt, _readyAt);
                ReportStatus(combat.HasValue ? "dead" :
                    "combat-field-unavailable", player);
                return;
            }

            if (combat.Value)
            {
                if (_lastCombat != true)
                    Plugin.Logger.LogInfo("[AutoMount/State] Combat START " +
                        "observed (local player).");
                _lastCombat = true;
                // Combat may dismount the player; it isn't a manual dismount.
                _lastMounted = null;
                _attemptStreak = 0;
                _nextAttemptAt = now + Plugin.SummonDelay.Value;
                ReportStatus("in-combat", player);
                return;
            }

            if (_lastCombat == true)
            {
                _attemptStreak = 0;
                _nextAttemptAt = Math.Max(_readyAt,
                    now + Mathf.Clamp(Plugin.SummonDelay.Value, 0.25f, 5f));
                Plugin.Logger.LogInfo("[AutoMount/State] Combat END " +
                    "observed; idle mount check scheduled.");
            }
            _lastCombat = false;

            bool? mounted = AutoMountGame.IsMounted(avatar);
            if (!mounted.HasValue)
            {
                ReportStatus("mounted-state-unavailable", player);
                return;
            }

            if (mounted.Value)
            {
                _lastMounted = true;
                _attemptStreak = 0;
                ReportStatus("mounted", player);
                return;
            }

            if (_lastMounted == true)
            {
                // If the player intentionally dismounts, allow a brief
                // window to interact before our always-mounted mode acts.
                // After an enemy dismount, _lastMounted was cleared above.
                _nextAttemptAt = Math.Max(_nextAttemptAt,
                    now + DismountGraceSeconds);
                _attemptStreak = 0;
                Plugin.Logger.LogInfo("[AutoMount/State] Dismount detected " +
                    "while idle; remount queued after short grace period.");
            }
            _lastMounted = false;

            if (_readyAt < 0f || now < _readyAt)
            {
                ReportStatus("waiting-for-world-ready", player);
                return;
            }

            if (now < _nextAttemptAt)
            {
                ReportStatus("waiting-for-mount-cooldown", player);
                return;
            }

            // Recheck immediately before invoking native game code.
            // Never manually change the mount flag, inventory or network.
            if (AutoMountGame.InCombat(player) != false ||
                AutoMountGame.IsDead(player) ||
                !AutoMountGame.IsActiveLocalPlayer(player) ||
                !AutoMountGame.IsActiveAvatar(avatar) ||
                AutoMountGame.IsMounted(avatar) != false)
            {
                ReportStatus("state-changed-before-attempt", player);
                return;
            }

            // Consume a bounded retry window before invocation. Rejected
            // summons (restricted zones, unavailable mounts, etc.) will
            // back off to 12s / 24s / 48s / 90s, never spam every frame.
            float retry = Mathf.Min(MaxRetrySeconds,
                FirstRetrySeconds * Mathf.Pow(2f,
                    Mathf.Min(_attemptStreak, 3)));
            _attemptStreak++;
            _nextAttemptAt = now + retry;

            if (AutoMountGame.TryNormalSummon(player, out string reason))
                Plugin.Logger.LogInfo("[AutoMount/State] Idle summon request " +
                    "#" + _attemptStreak + ": " + reason +
                    "; if still unmounted, retry no sooner than " +
                    retry + "s.");
            else
                Plugin.Logger.LogWarning("[AutoMount/State] Idle summon " +
                    "request failed: " + reason +
                    "; retry no sooner than " + retry + "s.");

            ReportStatus("post-idle-summon-request", player);
        }
        catch (Exception ex)
        {
            // Fail closed, without rapid retries on repeated exceptions.
            _nextAttemptAt = now + MaxRetrySeconds;
            Plugin.Logger.LogWarning("[AutoMount/State] Mount maintenance " +
                "check failed safely: " + ex.GetType().Name + ": " +
                ex.Message);
        }
    }

    private void ReportStatus(string phase, object? player)
    {
        // Bounded metadata-only diagnostics, max 25 summaries per game.
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
            ", legacyMountSlotPopulated=" +
                (avatar != null && AutoMountGame.EquippedMount(avatar)) +
            ", nativeSummonMethod=" + AutoMountGame.SummonMethodAvailable() +
            ".");
    }
}
