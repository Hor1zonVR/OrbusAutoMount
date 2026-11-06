using System;
using UnityEngine;

namespace OrbusAutoMount;

/// <summary>
/// v0.1.4: Maintain mounted state after login, loads, combat and dismounts.
/// Native summon retries use a session-wide cooldown that survives short
/// mounted-state blips, combat transitions and avatar/scene reloads.
/// Only a continuously mounted state resets the failure backoff.
/// </summary>
public sealed class AutoMountBehaviour : MonoBehaviour
{
    public AutoMountBehaviour(IntPtr pointer) : base(pointer) { }

    private const float PollSeconds = 0.2f;
    private const float DismountGraceSeconds = 2.5f;
    private const float FirstRetrySeconds = 12f;
    private const float MaxRetrySeconds = 90f;
    private const float StableMountedSeconds = 5f;
    private const float MinimumSummonSpacingSeconds = 8f;

    private int _playerId;
    private int _avatarId;
    private bool? _lastCombat;
    private bool? _lastMounted;
    private float _readyAt = -1f;
    // Opportunity delay for combat exits, manual dismounts and world loads.
    private float _resumeAfterAt;
    // Session-wide native request throttle; never reset by combat/loading.
    private float _nextAttemptAt;
    private float _lastNativeAttemptAt = -1f;
    private int _attemptStreak;
    private float _mountedSince = -1f;
    private bool _mountedStableConfirmed;
    private float _nextPoll;

    private bool? _lastEnabled;
    private float _nextStatus;
    private int _statusCount;
    private bool _reportedUpdate;

    private void ResetSession(bool clearCooldown)
    {
        _playerId = 0;
        _avatarId = 0;
        _lastCombat = null;
        _lastMounted = null;
        _mountedSince = -1f;
        _mountedStableConfirmed = false;
        _readyAt = -1f;
        _resumeAfterAt = 0f;
        // A zone/character reload must NOT bypass a recent failed summon.
        // Only explicitly changing Enabled begins a new throttle session.
        if (clearCooldown)
        {
            _nextAttemptAt = 0f;
            _lastNativeAttemptAt = -1f;
            _attemptStreak = 0;
        }
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
            ResetSession(true);
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
                if (_playerId != 0) ResetSession(false);
                return;
            }

            int playerId = AutoMountGame.Identity(player);
            if (playerId == 0)
            {
                ResetSession(false);
                ReportStatus("player-identity-unavailable", player);
                return;
            }

            if (_playerId != playerId)
            {
                ResetSession(false);
                _playerId = playerId;
                Plugin.Logger.LogInfo("[AutoMount/State] New local character " +
                    "session; checking mount state after world settles.");
            }

            object? avatar = AutoMountGame.Avatar(player);
            if (avatar == null || !AutoMountGame.IsActiveAvatar(avatar))
            {
                _avatarId = 0;
                _lastMounted = null;
                _mountedSince = -1f;
                _mountedStableConfirmed = false;
                _readyAt = -1f;
                _resumeAfterAt = 0f;
                ReportStatus(avatar == null ? "waiting-for-avatar" :
                    "avatar-inactive", player);
                return;
            }

            int avatarId = AutoMountGame.Identity(avatar);
            if (avatarId == 0)
            {
                _avatarId = 0;
                _readyAt = -1f;
                _mountedSince = -1f;
                _mountedStableConfirmed = false;
                ReportStatus("avatar-identity-unavailable", player);
                return;
            }

            if (_avatarId != avatarId)
            {
                _avatarId = avatarId;
                _lastCombat = null;
                _lastMounted = null;
                _mountedSince = -1f;
                _mountedStableConfirmed = false;
                float loadDelay = Mathf.Clamp(Plugin.LoadDelay.Value, 1f, 15f);
                _readyAt = now + loadDelay;
                _resumeAfterAt = _readyAt;
                Plugin.Logger.LogInfo("[AutoMount/State] Avatar ready; " +
                    "waiting " + loadDelay + " seconds after " +
                    "load before first idle mount attempt.");
            }

            bool? combat = AutoMountGame.InCombat(player);
            if (!combat.HasValue || AutoMountGame.IsDead(player))
            {
                _lastCombat = null;
                _lastMounted = null;
                _mountedSince = -1f;
                _mountedStableConfirmed = false;
                _readyAt = now + Mathf.Clamp(Plugin.LoadDelay.Value, 1f, 15f);
                _resumeAfterAt = Math.Max(_resumeAfterAt, _readyAt);
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
                _mountedSince = -1f;
                _mountedStableConfirmed = false;
                _resumeAfterAt = Math.Max(_readyAt,
                    now + Mathf.Clamp(Plugin.SummonDelay.Value, 0.25f, 5f));
                ReportStatus("in-combat", player);
                return;
            }

            if (_lastCombat == true)
            {
                _resumeAfterAt = Math.Max(_readyAt,
                    now + Mathf.Clamp(Plugin.SummonDelay.Value, 0.25f, 5f));
                Plugin.Logger.LogInfo("[AutoMount/State] Combat END " +
                    "observed; idle check scheduled without resetting " +
                    "native summon cooldown.");
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
                if (_mountedSince < 0f) _mountedSince = now;
                _lastMounted = true;

                // A single mounted=true poll is not proof that summoning
                // succeeded. Only sustained mounting (5 seconds) can clear
                // exponential backoff. Even then the last native call's
                // minimum spacing remains in force.
                if (!_mountedStableConfirmed &&
                    now - _mountedSince >= StableMountedSeconds)
                {
                    _mountedStableConfirmed = true;
                    _attemptStreak = 0;
                    _nextAttemptAt = Math.Max(now,
                        _lastNativeAttemptAt + MinimumSummonSpacingSeconds);
                    Plugin.Logger.LogInfo("[AutoMount/State] Mounted " +
                        "continuously for " + StableMountedSeconds +
                        "s; retry backoff reset (minimum call spacing kept).");
                }
                ReportStatus("mounted", player);
                return;
            }

            _mountedSince = -1f;
            _mountedStableConfirmed = false;

            if (_lastMounted == true)
            {
                // If the player intentionally dismounts, allow a brief
                // window to interact before our always-mounted mode acts.
                // After an enemy dismount, _lastMounted was cleared above.
                _resumeAfterAt = Math.Max(_resumeAfterAt,
                    now + DismountGraceSeconds);
                Plugin.Logger.LogInfo("[AutoMount/State] Dismount detected " +
                    "while idle; remount queued after short grace period.");
            }
            _lastMounted = false;

            if (_readyAt < 0f || now < _readyAt)
            {
                ReportStatus("waiting-for-world-ready", player);
                return;
            }

            if (now < _resumeAfterAt || now < _nextAttemptAt)
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

            // Consume a bounded retry window BEFORE invoking the game.
            // Combat, temporary mounting and area transitions never reset
            // this exponential backoff. A stable mount does (above).
            float retry = Mathf.Min(MaxRetrySeconds,
                FirstRetrySeconds * Mathf.Pow(2f,
                    Mathf.Min(_attemptStreak, 3)));
            _attemptStreak++;
            _lastNativeAttemptAt = now;
            _nextAttemptAt = now + Math.Max(retry,
                MinimumSummonSpacingSeconds);

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
            _nextAttemptAt = Math.Max(_nextAttemptAt,
                now + MaxRetrySeconds);
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
            ", stableMount=" + _mountedStableConfirmed +
            ", retriesSinceStableMount=" + _attemptStreak +
            ", cooldownSeconds=" +
                Math.Max(0f, _nextAttemptAt - Time.realtimeSinceStartup)
                    .ToString("0.0") +
            ", nativeSummonMethod=" + AutoMountGame.SummonMethodAvailable() +
            ".");
    }
}
