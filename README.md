# Chained Beasts Rerolls

I wanted rerolls to be something the party earns and decides on together.

**Version 0.1.1 for Chained Beasts 1.0.2 / Steam build 26231777.**
Single-player, including the damage-free reroll bonus, is working in my initial
testing. **Multiplayer has not been tested in-game yet.** Its confirmation and
sync logic passes nongame checks, but host/client gameplay still needs testing.
Broader controller, save/resume, and mod-interaction testing is also outstanding.
This is a separate mod; it doesn't require Foundation, Terms & Contusions,
Battle Numbers, or Matchmaking.

## How it works

- Every fresh run starts with **one shared party reroll**.
- One charge replaces **all currently offered skills**, **all potions**, or
  **every merchant offer**, including weapons, instant items, passives and equipment.
- Those screens spend from the **same balance**. Two charges can be used twice
  on potions, once on potions and once on skills, or any other combination.
- Finish a battle with **at least one surviving player who took no damage** to
  earn **one extra party charge**. Multiple flawless survivors still earn one.
- Taking damage and healing back to full doesn't count as flawless. A dead
  player doesn't qualify. Failed battles and the tutorial don't award charges.
- Native offer generation, eligibility and prices are used. Items can reappear.
  If no different valid set can be generated, no charge is spent.
- Surgeries are outside this mod.

## Use it

On a skill-choice, potion, or merchant screen, click the gold **REROLL / count**
badge at the lower right. It only appears when that screen is ready and current
selections are unconfirmed. Finish tutorials/dialogue and unconfirm purchases or
potions first. It does not appear in combat or on the surgery screen.

In a one-player run, one click rerolls and spends one charge.

In multiplayer, clicking proposes the reroll and confirms for that local player.
**Every character must accept**, including separate couch co-op characters.
The panel has per-player Accept/Cancel controls. While it is open, each player
can also use their normal game **Confirm / Back** controls to accept or cancel.
Initiating the proposal currently uses the mouse badge; this preview doesn't
add that badge to the game's controller navigation graph.

The host prepares the replacement set and every machine acknowledges it before
the charge is committed. Cancelling, timing out, leaving the screen, a party
change, or a failed preparation cancels without spending. Ordinary card input
is held only during the reroll confirmation/transaction, then released.

## Install

Requires Windows x64, Unity 6000.3.21f1 and **BepInEx 6.0.0-be.791+b782da4 for
Unity IL2CPP**. Get it from the [official BepInEx build page](https://builds.bepinex.dev/projects/bepinex_be/791)
and follow the [IL2CPP installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html).
Game files and the loader aren't included.

[**Download Rerolls 0.1.1**](https://github.com/Krrrrrrk/Chained-Beasts_Rerolls/releases/download/v0.1.1/ChainedBeasts.Rerolls-0.1.1.zip)
from the [GitHub release](https://github.com/Krrrrrrk/Chained-Beasts_Rerolls/releases/tag/v0.1.1).
With the game closed, merge the ZIP's `BepInEx` folder into the game folder.
Install these two files together:

```text
BepInEx/plugins/ChainedBeasts.Rerolls/ChainedBeasts.Rerolls.dll
BepInEx/plugins/ChainedBeasts.Rerolls/ChainedBeasts.Rerolls.Core.dll
```

For multiplayer, **everyone needs the same two DLLs**, with the host installed
too. This preview negotiates through Steam lobbies; Unity Relay/cross-platform
sessions are not supported. Missing/mismatched installs disable party rerolls
instead of changing the game's normal joining or progression.

Matching gameplay mods/settings are still recommended. This mod verifies its
own binaries and checks replacement IDs against the local game library; it
doesn't certify another mod's settings or mechanics.

## Configuration and saved balance

After a launch, `BepInEx/config/local.chainedbeasts.rerolls.cfg` contains
`[General] Enabled = true`. Set it to `false` and restart to disable the mod.

The host keeps the balance in a separate ledger under
`BepInEx/config/ChainedBeasts.Rerolls/`, keyed to the native run seed and start
time. Game saves are not edited. The ledger is local to the host and isn't a
Steam Cloud save. Resuming with no ledger starts with **zero** rerolls rather
than granting a free starting charge; new runs still start with one.

Restoring an older native save does not rewind the separate reroll ledger.
This preview saves the charge balance, not an exact snapshot of rerolled cards;
the native game may regenerate offers after a restart.
Host migration, moving a run to another PC, joining mid-run, and reconnecting
during a reroll aren't supported by this preview. Use fresh runs for initial tests.

## Remove it

Close the game and remove the `ChainedBeasts.Rerolls` plugin folder. Its config
and ledger folder can be removed separately. Removing the ledger loses the
balance for resumed runs. No native files need restoring, and other mods are
untouched. If using `scripts/build.ps1 -Deploy`, its deployment record identifies
the previous DLL/config/user-data backup for rollback.

## Build

Requires **.NET SDK 7 or later**, the installed game's generated runtime interop
assemblies, and **PowerShell 7**. The plugin targets `net6.0` and the installed
BepInEx runtime. No Cpp2IL inspection stubs are runtime dependencies.

```powershell
pwsh -NoProfile -File scripts/build.ps1 -Package
# Optional, with the game closed:
pwsh -NoProfile -File scripts/build.ps1 -Deploy -SkipBuild
```

The build runs the production-core, source-linked service/transport/store,
native-offer adapter, presentation, and metadata checks. They use inert game
substitutes; they don't launch Unity or prove real networking/gameplay.

## Manual test checklist

1. Start a fresh solo run: verify one charge. Reroll each eligible screen and
   confirm the entire current offer set changes and exactly one charge is used.
2. Take damage, heal, and finish: no flawless bonus for that player. Finish a
   battle without damage: one extra charge. Check dead players don't qualify.
3. Earn two charges and spend them on different screens, then try twice on one
   screen. The balance should be shared and never fall below zero.
4. In multiplayer, propose and wait: no replacement or debit until every
   character accepts. Cancel once. Confirm again. Compare offers and balances
   on host and client, including couch co-op and normal Confirm/Back controls.
5. Test a disconnect/timeout while waiting. Check ordinary choices and the next
   battle transition still work. Test a native checkpoint resume separately.

**Bug reports:** include `BepInEx/LogOutput.log` and
`%USERPROFILE%/AppData/LocalLow/Featherweight Games/ChainedBeasts/Player.log`
(and `Player-prev.log` if you've relaunched). Tell me who hosted, the screen,
party size, charge count, and which mods were installed on each PC.

Artwork is CC0; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
Native hooks, protocol and limitations: [implementation notes](docs/IMPLEMENTATION.md).
