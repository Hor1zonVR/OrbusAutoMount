# OrbusAutoMount

A lightweight, client-side BepInEx / IL2CPP quality-of-life mod for **OrbusVR Reborn**.

## v0.1.3 — stay mounted when out of combat

AutoMount no longer requires a combat exit. While enabled, it keeps the normal mount summoned whenever the local character is loaded, alive, **out of combat**, and the game reports `isMounted=false`.

- **First login / fresh character load:** after the local avatar is available, wait the configurable **After-Load Delay** (default 3.5 seconds), then attempt a normal native summon.
- **Changing area/instance:** if the avatar changes or goes temporarily unavailable, wait for it to become active again; if the game dismounts an existing avatar without replacing it, detect `mounted=true → false` and remount.
- **After combat:** keep the existing configurable 0.9-second post-combat delay.
- **Other dismounts:** while idle, remount after a short 2.5-second grace period. **Manually dismounting will not stay permanent while AutoMount is enabled.** Disable it in RebornMenu when you intentionally want to remain on foot.
- **No summon spam:** if a native summon does not result in a mounted state, retry only after a backoff of 12, 24, 48 and then 90 seconds. Native game restrictions still decide whether mounting is permitted.
- **No forced equip:** this only deploys the mount already selected in Orbus through the normal summon method. If the game truly unequips or removes the mount *item* rather than simply dismounting you, v0.1.3 cannot automatically choose and re-equip it.

Still experimental. The Quest and PC launchers/installers are unaffected; AutoMount remains **out of RebornManager's public catalogue** until explicitly reinstated.

### v0.1.3 test plan

1. Build/publish from `release.ps1`, then update the existing private AutoMount DLL (or test via local instance).
2. With AutoMount enabled, join an open-world area **without fighting**. Check that the equipped mount appears a few seconds after the local avatar loads.
3. Dismount manually while out of combat; it should summon again after roughly 2.5 seconds.
4. Fight and exit combat: still remounts following the configured after-combat delay.
5. Change instance or zone and confirm remounting once the local avatar settles.
6. In a restricted area, it must not force or repeatedly spam a summon. The native game method retains authority; logs show bounded retry timing.
7. If the mount item has actually become unequipped, capture that separately: the current mod cannot restore it.



## v0.1.2 — fixed the false equipped-mount check

The v0.1.1 live logs proved that RebornMenu was enabling AutoMount, the local combat state changed correctly, and the native summon method was available. The attempt never ran because our own check of `PlayerAvatar.mountSlot` / `cbn.bebp` returned false. In the same session the game reported `mounted=true` after a normal manual summon, so the empty slot field is not a reliable eligibility signal.

**v0.1.2 removes that blocking check.** After a real combat exit and configured delay, while the player is active, alive, out of combat, and not already mounted, AutoMount invokes the game's own `PlayerCharacter.SummonMount()` / `ceh.lew()` action exactly once. We keep the old mount-slot result only as a clearly labelled diagnostic.

The fix is pushed as source and needs to be locally compiled, published, and confirmed in-game; native mount restrictions have not yet been audited in Ghidra. Avoid testing in a mount-restricted area until this is verified.

**Historical v0.1.1 diagnostic notes (superseded by v0.1.2 above).** The user had enabled the mod later in RebornMenu; the prior log only showed `Enabled=False` at startup and offered no evidence of the later toggle or combat state. v0.1.1 adds targeted evidence so the underlying cause can be verified.

### v0.1.1 diagnostic verification

The log will now contain:
- `[AutoMount/State] Update loop running` — proves the BepInEx IL2CPP behaviour is receiving Unity Update.
- `[AutoMount/State] Enabled changed to True/False` — proves live RebornMenu toggle is read (not just the startup configuration).
- `phase=... player=... playerActive=... combat=... avatar=... mounted=... equippedMount=... nativeSummonMethod=...` — bounded metadata-only state report every 12 seconds, max 25.
- `Combat START` and `Combat END` — actual transitions that schedule the one-time attempt.
- `Summon skipped` or `Normal game summon invoked` — the exact result of the client action. This still does **not** prove server approval or successful mount spawning.

**Test:** install v0.1.1 through RebornManager, launch the game, enable the setting under Settings → Mods → OrbusAutoMount, fight one easy enemy and exit combat. Share only the `[AutoMount/State]` lines if mounting still fails after updating. No manual diagnostics toggle is needed.

Do not confuse startup `Enabled=False` with whether the mod was enabled later. The old v0.1.0 logging could not distinguish those cases. No mounted-state writes or network modification are performed.

## What it does

When the local avatar is ready and you are safely out of combat, try to maintain the mounted state using Orbus's existing `PlayerCharacter.SummonMount()` action. It no longer requires a combat transition.

- Attempts after initial login, after zone loads, after combat ends, and after becoming unmounted while idle.
- Retries at an increasing interval when Orbus refuses a summon; it does **not** spam the native call.
- Delays or skips attempts while in combat, dead, loading, already mounted, or with unknown state.
- Skips if you're already mounted, dead, in a loading/no-local-player state, or if combat has resumed. The native game handles mount item eligibility.
- Uses the game's native summon method; doesn't write mount state, fake network messages, change your position, or bypass server-side restrictions.
- Configs show automatically in **RebornMenu → Mods** with ordinary BepInEx entries.

### Settings

| Setting | Default | Purpose |
|---|---:|---|
| General / Enabled | **Off** | Turn on after first cautious test |
| General / SummonDelaySeconds | 0.9 s | Wait after combat clears; range 0.25–5 s |\n| General / LoadDelaySeconds | 3.5 s | Wait after local avatar loads; range 1–15 s |
| Diagnostics / LogTransitions | Off | Optional combat-transition and skip logs; not in normal RebornMenu |

**Important:** The existence of native summon and combat methods is verified from the two IL2CPP dumps, but we have **not** yet verified whether `SummonMount()` itself rejects every restricted area. Test in a safe open-world area, then a dungeon or mount-restricted area only once the basic behaviour is confirmed.

### IL2CPP mapping

| Readable Community dump | Renewed / Spades dump | Use |
|---|---|---|
| `MagicalActual.PlayerCharacter.Instance` | `ceh.blfr` | Local player |
| `PlayerCharacter.myMonster` | `ceh.betl` | Player combat component |
| `Monster.inCombat` | `bxm.bctj` | True/false state |
| `PlayerCharacter.myPlayerAvatar` | `ceh.betm` | Player avatar |
| `PlayerAvatar.isMounted` | `cbn.beka` | Mounted state |
| `PlayerAvatar.mountSlot` | `cbn.bebp` | Unreliable diagnostic only; **not a summon gate** |
| `InventoryItemInstance.baseItem` | `cum.bjer` | Ensure slot has an actual item |
| `PlayerCharacter.SummonMount()` | `ceh.lew()` | **Only** game action invoked |
| `PlayerCharacter.MonsterInCombatChanged(bool)` | `ceh.ck(bool)` | Known event; not patched in v0.1.0 |

No broad scene scans or Ghidra-derived undocumented writes are required. Runtime checks use the current local player's direct field links at ~6 Hz.

## Clone and publish

In PowerShell (your folder can be anywhere):

```powershell
$root = "$env:USERPROFILE\OneDrive\Documents\OrbusAutoMount"
git clone https://github.com/Hor1zonVR/OrbusAutoMount.git "$root"
$game = "$env:LOCALAPPDATA\OrbusRebornModdingInstances\My Orbus Renewed 2"
& "$root\release.ps1" -GameDir $game
```

If you've **already cloned** the repo, use `git -C "$root" pull --ff-only origin main` instead of `git clone`.

The release script verifies the GitHub login and clean local source, pulls the newest main branch, builds the DLL against **the selected Orbus instance's generated interop**, then publishes `OrbusAutoMount.dll` as a GitHub release asset. The script **does not copy any DLL to the game**.

For build only (no GitHub release):

```powershell
dotnet build "$root\src\OrbusAutoMount\OrbusAutoMount.csproj" -c Release "-p:GameDir=$game"
```

## First verification

1. Publish the DLL to GitHub; update/install in a **test** RebornManager instance when catalogued.
2. Open **Settings → Mods → OrbusAutoMount**. Enable it; leave the 0.9s delay.
3. Equip a mount. Start combat with a low-level enemy; finish the fight. Check that the native mount is summoned **once**.
4. Dismount manually while out of combat. It should **not** re-summon, because there was no new combat exit.
5. Enter combat again, then exit; a new single summon should occur.
6. Repeat while already mounted / with no mount equipped / with a zone swap to check safe skips.
7. Finally test an area that disallows mounting. If it bypasses a restriction unexpectedly, disable the mod and share logs.
8. If the mod never detects combat, enable **Diagnostics / LogTransitions** in its `.cfg` and share `[AutoMount]` lines. Do not enable broad scanning.

Before the first game test, a successful compiler build proves only that the project compiles, **not** that the combat transitions or native mount restrictions have been verified.
