# Implementation notes: 0.1.0 preview

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
| Battle completion | Prefix `GameLevel.OnLevelCompleteForPeer()` before native `RunDataManager.OnLevelComplete`; ignore duplicate `hasCompletedLevelForPeer`, tutorial, failures, and already settled levels |
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
