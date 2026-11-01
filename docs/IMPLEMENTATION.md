# Implementation notes: 0.1.3

Targets the inspected Chained Beasts 1.0.2 / Steam build 26231777, Unity
6000.3.21f1, BepInEx 6.0.0-be.791+b782da4 and .NET 6. Startup refuses a different
GameAssembly SHA-256:
`ba3ef02fa87da4c41bb27d7610539c9578132204f0fb5713833e2ef93415ce29`.

## Rules and native paths

The plugin GUID is `local.chainedbeasts.rerolls`. It deploys only its own plugin
and pure managed Core assembly. No original binary or asset is changed.

| Purpose | Native observation or integration |
| --- | --- |
| Fresh-run identity | Postfix `RunDataManager.StartNewRunRpc(int seed, int runStartTime, AscensionLevel difficulty, DifficultyConfig difficultyMultipliers, int forceStartingDay)`; verify the manager's seed/start time match the invocation |
| Damage during a battle | Postfix `Player.OnDamageAccounted(int)` and `Player.OnHealthChanged(int,int)`; host only, positive accounted damage or actual HP loss while the level is RUNNING |
| Battle completion | Observe replicated `GameLevel.ActiveGameState` from Update and before `RunDataManager.EvaluateBattleEndEquipment()`; use the battle number captured at start and settle once, excluding tutorial/failures |
| Survivor identity | Current spawned `PlayerManager.players`, `playerData.playerID`, `health.Value > 0`, `!IsDead`; never call the convenience `PlayerData.PlayerObject` getter |
| Final native damage cross-check | `PlayerData.lastFightDamageTaken` before native battle cleanup |
| Skills | `SkillGenerator.GenerateTrainingSkills(seed, null)`, replace only `trainingSkills[uiState.trainingLayer]`, invoke `OnTrainingSkillsChanged(layer)` and refresh the normal trainer |
| Potions | `InjuryGenerator.GeneratePotions(manager, playerCount, seed, out _)`; transmit injury ID, heal amount, size, sharing, model index and localization term; create normal `UILobbyDoctorSelectionPage` instances |
| Merchant | `PurchaseGenerator.GenerateShopOptions(seed, GetAct(completedLevels))`; transmit the entire purchase-ID array and create normal `UILobbyShopSelectionPage` instances |

Generators run with the native eligibility/weights, including installed content
hooks. They may produce repeat items. Up to 24 deterministic candidate seeds
are tried to find a valid set that differs somewhere; an identical/invalid set
does not spend a charge. `RandomUtils.rand` is restored in a finally block.

Trainer done/waiting/auto-select flags, potion confirmation/resolution flags and
merchant ready/purchased/locked flags must all be clear. Tutorial dialogue and
nonfocused/inactive views are excluded. Replacing a skill set preserves native
refresh counters, progress and acquisition logic; the native helper
`ResetUpgradeSelections` is deliberately not used because it clears refresh counts.

The damage ledger records whether each starting participant was hurt, not their
net HP difference. Healing doesn't erase it; zero-damage blocks don't count.
Positive HP reductions during combat are conservatively counted even if a mod
caused them through a health-cost/stat adjustment. One flawless survivor earns
one party charge, regardless of the number of qualifying players.

## Consent and synchronization

`Consensus` binds a proposal to the current offer fingerprint and the complete
`NetworkSession.Clients` roster. It requires one vote per character, validated
against the named-message's real NGO sender. This includes couch co-op seats.
The host owns the balance and generation. All machines need matching plugin/Core
hashes and the inspected game hash. Hello leases last eight seconds and refresh
every two seconds. The Steam host publishes a mod tag to avoid sending probes
to an unmodded server. No admission hook, disconnect, or game-start command is used.

Named messages on `cb.rerolls.v1` carry bounded JSON (16 KiB, 32 offers, eight
seats), host epochs, per-client nonces, ordered request sequences, revisions and
transaction IDs. Only the server may send replacement/state/commit decisions.
Each client validates concrete IDs against its generated runtime game library.
Network callbacks only enqueue; all native work happens from the controller's
Update, never from an RPC/OnGUI/input callback stack.

The sequence is per-character vote, per-machine prepare, per-machine apply,
then host debit/persistence and Finish. Prepare builds hidden cards while
retaining original lists/cards. Apply makes the new set live but retains the
originals until commit. A preparation/apply failure or timeout rolls back and
doesn't debit. Commit receipts also appear in subsequent host state snapshots,
so a missed final notification can be reconciled. Cleanup failure after a
committed reroll is logged rather than rolling back an already spent charge.

During confirmation/transaction, prefixes suppress only the affected screen's
submit/cancel/navigation/purchase/upgrade/execute entry points. Local
`HandleSubmit(ClientIdentifier)` and `HandleCancel(ClientIdentifier)` route the
normal paired Confirm/Back controls to consent/cancellation. Requests are queued
with the original screen fingerprint/revision. Screen/roster changes, timeouts,
unload and errors release the temporary input gate. No UIReveal/localization
detours, raw native addresses, asset replacement or general game input gate is used.

## Persistence and boundaries

Only the host writes `BepInEx/config/ChainedBeasts.Rerolls/<run-hash>.json`, using
an atomic temporary-file replacement. It contains the run identity, current
shared balance/revision, and last settled battle. The native save is untouched.
No ledger on resume means zero starting charges; a new observed run means one.
Restoring an older native save doesn't rewind this separate ledger. The ledger
isn't transferred through Steam Cloud or host migration. Native checkpoint
restoration may regenerate offers; this preview persists the balance, not an
exact snapshot of rerolled cards across a restart.

Steam transport, matched installs and a party assembled before the run are the
supported preview scope. Initiation uses the mouse badge; its insertion into
the native controller navigation graph, translated strings, controller-specific
glyphs, surgery rerolls, mid-run joining, and host migration are not implemented.
Normal Confirm/Back routing still needs manual controller and host/client testing.

## Validation limits

The build uses installed BepInEx-generated runtime assemblies; they are never
shipped. Core tests exercise shared charges, damage-free rewards, unanimity,
per-machine receipts, timeout/replay rules and malformed packets. Source-linked
tests run the production service/transport/store against inert NGO/Steam/world
substitutes, the production native-offer adapter against inert cards/generators,
and the production drawing helper against inert GUI/texture substitutes. Metadata
checks inspect every exact declared native hook target and the shipped callback
surface without loading the plugin. Optional Pillow renders test layout records.

These checks cannot prove IL2CPP patching/marshalling, native card models and
animations, actual NGO delivery, every conflict with other mods, native
controller routes, or save/resume/gameplay correctness. Manual tests in the
README remain required. The game is never launched by the build script.


## 0.1.1: damage-free award lifecycle correction

The 0.1.0 logs showed normal native completed-level progression but no completed
battles in the reroll ledger. The late private completion hook and its guards
did not provide a reliable award trigger in gameplay. 0.1.1 no longer depends
on it. Host Update observes RUNNING -> LEVEL_COMPLETE / DEMO_COMPLETE (win) or
LEVEL_FAILED, with an additional observation before native battle-end equipment
evaluation, while survivor/damage data are still available. A complete starting
roster is required; delayed player registration is retried instead of latching
an empty roster. Early damage is retained while waiting for registration.

The native level number is captured at battle start, so later native increments
and `hasCompletedLevelForPeer` cannot skip or double-award the battle. Logs record
one tracking line and one settlement line per battle, including outcome, live
HP/survival, damage flags/native counter, award and balance, or the explicit skip
reason. Existing ledgers remain readable. No missed historical bonus is fabricated
or inserted into a save. The charge/offer/confirmation protocol is unchanged, but
all peers still need matching updated binaries for its normal build handshake.

## Reported gameplay status

0.1.1 passed an initial manual single-player test, including the corrected
damage-free bonus. A later multiplayer test found that the joining player
could not see reroll controls. 0.1.2 addresses the client screen-detection
failure below; its actual host/client behavior still awaits another manual test.
Inert checks establish coordination only under their simulated cases, not
actual NGO/Steam delivery or multiplayer gameplay success.

## 0.1.2: remote client screen detection

The supplied client BepInEx log confirms Rerolls 0.1.1 loaded, without a rerolls
exception. It does not contain a rerolls handshake or screen-readiness trace.
The supplied Player.log excerpt is truncated and covers startup/initial lobby
creation, so it cannot establish the later runtime flag values.

Native 1.0.2 code inspection establishes a concrete adapter defect:
`NetworkSession.TransitionToSceneCallback(string)` stores the pending scene
name on peers. Online peers send `TransitionToSceneReadyRpc(ClientIdentifier)`;
the server's ready handler eventually calls `TransitionToSceneReady()`, loads
the next scene and clears the pending name. A remote client does not execute
that host loading/clear path. Consequently `TransitionPending` is not a valid
remote-client readiness gate, even when the new cards are visible and focused.
The old common gate in `NativeOffers.Read()` rejected those client screens.

The adapter now checks `TransitionPending` only on the server. Both roles still
require the actual lobby scene, focused/active native cards, initialized lists
and UI state, no pause/checkpoint restore/tutorial dialogue, and no already
confirmed/purchased/resolving selections. It does not clear or modify the game's
transition field. This applies to skills, potions and all merchant offers.

Bounded logs now identify host/client transport setup, host run initialization,
matching handshake receipts, mismatched rerolls DLLs, and changes in screen,
party size and readiness. They do not log every frame or every heartbeat.
Host authority, unanimous character voting, exact binary matching, native offer
generation, charge persistence and rollback rules are unchanged.

Manual retest: put both 0.1.2 DLLs on every machine, assemble the party before
starting a fresh run, and visit each supported screen. Both machines must see
the badge and the same balance. One vote must wait; the second must replace
the same offers and deduct exactly one charge everywhere. Check cancellation,
damage-free rewards and a solo reroll as well. Surgeries remain unsupported.

## 0.1.3: full reroll panel click target

The idle badge now uses one transparent button over the entire colored panel,
including its icon, accent border and padding. The text stays in its original
position. The caption above and shadow outside the panel are excluded. The
existing charge/readiness gate and queued vote path are unchanged. Inert input
checks cover edges, the icon, outside clicks, disabled states and duplicate draw
calls at four resolutions. Actual Unity mouse interaction awaits manual testing.
