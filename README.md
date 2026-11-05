# OrbusAutoMount

A lightweight, client-side BepInEx / IL2CPP quality-of-life mod for **OrbusVR Reborn**.

**Status: experimental v0.1.1 source. v0.1.0 loads but did not auto-summon in the first user test.** The user had enabled the mod later in RebornMenu; the prior log only showed `Enabled=False` at startup and offered no evidence of the later toggle or combat state. v0.1.1 adds targeted evidence so the underlying cause can be verified.

### v0.1.1 diagnostic verification

The log will now contain:
- `[AutoMount/State] Update loop running` — proves the BepInEx IL2CPP behaviour is receiving Unity Update.
- `[AutoMount/State] Enabled changed to True/False` — proves live RebornMenu toggle is read (not just the startup configuration).
- `phase=... player=... playerActive=... combat=... avatar=... mounted=... equippedMount=... nativeSummonMethod=...` — bounded metadata-only state report every 12 seconds, max 25.
- `Combat START` and `Combat END` — actual transitions that schedule the one-time attempt.
- `Summon skipped` or `Normal game summon invoked` — the exact result of the client action. This still does **not** prove server approval or successful mount spawning.

**Test:** install v0.1.1 through RebornManager, launch the game, enable the setting under Settings → Mods → OrbusAutoMount, fight one easy enemy and exit combat. Share only the `[AutoMount/State]` lines if mounting still fails. No manual diagnostics toggle is needed.

Do not confuse startup `Enabled=False` with whether the mod was enabled later. The old v0.1.0 logging could not distinguish those cases. No mounted-state writes or network modification are performed.

## What it does

When **your own character leaves combat**, wait for a configurable delay and attempt to summon the currently equipped mount using the **game's existing** `PlayerCharacter.SummonMount()` action. It intentionally does **not** require you to have been riding before combat.

- Triggers only after observing an actual **in-combat → out-of-combat** transition; it will not summon just because you've logged in.
- Makes **one** summon attempt for each combat exit. No repeated summon spam.
- Cancels the queued attempt if you re-enter combat.
- Skips if you're already mounted, dead, in a loading/no-local-player state, or no valid mount item appears to be equipped.
- Uses the game's native summon method; doesn't write mount state, fake network messages, change your position, or bypass server-side restrictions.
- Configs show automatically in **RebornMenu → Mods** with ordinary BepInEx entries.

### Settings

| Setting | Default | Purpose |
|---|---:|---|
| General / Enabled | **Off** | Turn on after first cautious test |
| General / SummonDelaySeconds | 0.9 s | Wait for combat lock to clear; range 0.25–5 s |
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
| `PlayerAvatar.mountSlot` | `cbn.bebp` | Equipped mount slot |
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
