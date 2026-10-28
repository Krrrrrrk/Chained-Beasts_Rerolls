using System.Security.Cryptography;
using ChainedBeasts.Rerolls.Core;
using Unity.Netcode;
using UnityEngine;

namespace ChainedBeasts.Rerolls;

internal sealed class Service : IDisposable
{
    private sealed class Peer
    {
        internal ulong Nonce, Last; internal double Until; internal string Room;
        internal Peer(ulong nonce, ulong last, double until, string room) { Nonce=nonce;Last=last;Until=until;Room=room; }
    }
    private readonly Dictionary<ulong, Peer> _peers = new();
    private readonly Consensus _consensus = new();
    private readonly Queue<(int Player, bool Cancel, string Room, int Revision)> _commands = new();
    private readonly PartyLedger _ledger = new();
    private Transport _transport;
    private NativeOffers _room;
    private NativeStage _stage;
    private Item[] _replacement;
    private string _run = "", _fresh = "", _round = "";
    private int _lastFinished;
    private IntPtr _session;
    private ulong _epoch, _nonce = Nonce(), _sequence;
    private long _transaction, _clientTransaction;
    private double _helloAt, _advertAt, _clientDeadline, _hostSeen;
    private Step _clientStep;
    private bool _failed;
    internal Packet State = new() { Status = "Waiting for a run." };
    internal NativeOffers Visible => _room;
    internal bool Busy => !_failed && (_consensus.Step != Step.Idle || _clientStep != Step.Idle);
    internal Seat[] Seats => ReadSeats();
    private bool Host => _transport?.Manager.IsServer == true;
    private ulong Local => _transport?.Manager.LocalClientId ?? ulong.MaxValue;
    private static ulong Nonce() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) | 1;
    private static string Key(RunDataManager data) => data.RunSeed + ":" + data.RunStartTime;

    internal void Fresh(RunDataManager data, int seed, int start)
    { if (data.RunSeed == seed && data.RunStartTime == start) { _fresh = Key(data); if (Host && _run == _fresh) Initialize(data, true); } }
    private void Initialize(RunDataManager data, bool fresh)
    {
        Abort("Run changed."); _run = Key(data); _epoch = Nonce(); _peers.Clear();
        var saved = fresh ? null : LedgerStore.Load(_run);
        _ledger.Start(_run, fresh ? 1 : saved?.Charges ?? 0, saved?.Revision ?? 0);
        _lastFinished = fresh ? data.CompletedLevels : saved?.LastFinishedLevel ?? data.CompletedLevels;
        _round = "";
        if (fresh) { Save(); _fresh = ""; }
        Status(fresh ? "Party starts with one shared reroll." : saved != null ? "Party rerolls restored." : "No reroll ledger for this resumed run; no starting charge granted.");
    }
    private void Save(int? charges = null, int? revision = null) => LedgerStore.Save(new(1, _run, charges ?? _ledger.Charges, revision ?? _ledger.Revision, _lastFinished));
    internal void Tick()
    {
        if (_failed) return;
        try { TickCore(); }
        catch (Exception e) { Fail(e); }
    }
    private void TickCore()
    {
        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !NetworkSession.InstanceExists || !NetworkSession.Instance.IsSpawned ||
            !GameManager.InstanceExists || GameManager.Instance.State == GameManager.eState.MENU)
        { ResetConnection(); return; }
        var session = NetworkSession.Instance;
        if (_transport?.Manager.Pointer != manager.Pointer || _session != session.Pointer)
        {
            ResetConnection(); _transport = new(manager); _session = session.Pointer; _nonce = Nonce(); _sequence = 0;
        }
        var now = Time.realtimeSinceStartupAsDouble;
        if (now >= _advertAt) { _advertAt = now + 2; _transport.HostAdvertised(); }
        var data = session.runDataManager;
        if (data == null || data.RunStartTime <= 0 || data.playerDatas == null || data.playerDatas.Count == 0) return;
        if (Host && _run != Key(data)) Initialize(data, _fresh == Key(data) && !data.checkpointRestored);
        ObserveBattle();
        var visible = NativeOffers.Read();
        if (Busy)
        {
            var valid = visible != null && _room != null && visible.Pointer == _room.Pointer && visible.Room.Kind == _room.Room.Kind && visible.Room.Layer == _room.Room.Layer;
            var applying = Host ? _consensus.Step == Step.Applying : _clientStep == Step.Applying;
            var changedOffers = valid && (applying ? Wire.ItemHash(visible.Room.Items) != Wire.ItemHash(_room.Room.Items) &&
                (_replacement == null || Wire.ItemHash(visible.Room.Items) != Wire.ItemHash(_replacement)) : visible.Room.Key != _room.Room.Key);
            if (!valid || changedOffers || Host && !_consensus.SameParty(ReadSeats()))
                Abort("Screen or party changed; no reroll charge spent.");
        }
        if (!Busy) _room = visible;
        foreach (var item in _transport.Drain()) Process(item.Sender, item.Packet, now);
        while (_commands.Count > 0)
        {
            var command = _commands.Dequeue();
            if (command.Room != _room?.Room.Key || command.Revision != State.Revision) continue;
            if (command.Cancel) Cancel(command.Player); else Vote(command.Player);
        }
        if (Host)
        {
            foreach (var peer in _peers.Where(p => p.Value.Until <= now || !ReadSeats().Any(s => s.Client == p.Key)).Select(p => p.Key).ToArray()) _peers.Remove(peer);
            if (Busy && (_consensus.Expired(now) || !AllPresent())) Abort("Confirmation timed out or a player left; no charge spent.");
            Advance(now);
            State = Snapshot();
        }
        else
        {
            if (Busy && now >= _clientDeadline) { Abort("Reroll host timed out; original offers restored."); }
            if (now >= _helloAt && _transport.HostAdvertised())
            {
                _helloAt = now + 2;
                _transport.Send(NetworkManager.ServerClientId, new() { Op = Op.Hello, Nonce = _nonce, Sequence = ++_sequence,
                    Build = Plugin.Stamp, Room = _room?.Room.Key ?? "" });
            }
            if (now - _hostSeen > 8) State = State with { Ok = false, Status = "Waiting for a matching rerolls host." };
        }
    }
    private Seat[] ReadSeats()
    {
        if (!NetworkSession.InstanceExists || NetworkSession.Instance.Clients == null) return Array.Empty<Seat>();
        var clients = NetworkSession.Instance.Clients; var result = new List<Seat>();
        var count = clients.Cast<Il2CppSystem.Collections.Generic.ICollection<ClientIdentifier>>().Count;
        for (var i = 0; i < Math.Min(8, count); i++) if (clients[i] != null && clients[i].PlayerIndex < 8)
            result.Add(new(clients[i].NetworkID, (int)clients[i].PlayerIndex));
        return result.OrderBy(s => s.Player).ToArray();
    }
    private bool AllPresent() => ReadSeats().All(s => s.Client == Local || _peers.TryGetValue(s.Client, out var peer) &&
        peer.Until > Time.realtimeSinceStartupAsDouble && peer.Room == (_room?.Room.Key ?? ""));
    private Packet Snapshot() => new() { Op = Op.State, Epoch = _epoch, Room = _room?.Room.Key ?? "", Kind = _room?.Room.Kind ?? OfferKind.None,
        Layer = _room?.Room.Layer ?? 0, Charges = _ledger.Charges, Revision = _ledger.Revision, Transaction = _consensus.Transaction,
        Step = _consensus.Step, Votes = _consensus.Votes, Committed = _ledger.LastCommitted, Ok = _room != null && AllPresent(),
        Status = State.Status.Length == 0 ? "Shared party rerolls." : State.Status };
    private void Status(string text) { State = State with { Status = text }; if (Host) Broadcast(Snapshot()); }
    private void Broadcast(Packet packet)
    { foreach (var client in ReadSeats().Select(s => s.Client).Distinct().Where(c => c != Local && _peers.ContainsKey(c)))
        _transport.Send(client, packet.Op == Op.State ? packet with { Nonce = _peers[client].Nonce } : packet); }
    private void Process(ulong sender, Packet packet, double now)
    {
        if (Host)
        {
            if (!ReadSeats().Any(s => s.Client == sender) || sender == Local) return;
            if (packet.Op == Op.Hello)
            {
                if (packet.Nonce == 0 || packet.Sequence == 0 || packet.Build != Plugin.Stamp) return;
                if (_peers.TryGetValue(sender, out var prior) && prior.Nonce == packet.Nonce && packet.Sequence <= prior.Last) return;
                if (Busy && prior != null && prior.Nonce != packet.Nonce) Abort("A player reconnected; reroll cancelled.");
                _peers[sender] = new(packet.Nonce, packet.Sequence, now + 8, packet.Room);
                _transport.Send(sender, Snapshot() with { Nonce = packet.Nonce }); return;
            }
            if (!_peers.TryGetValue(sender, out var peer) || packet.Nonce != peer.Nonce || packet.Sequence <= peer.Last || packet.Epoch != _epoch || peer.Until <= now) return;
            _peers[sender] = new(peer.Nonce, packet.Sequence, peer.Until, peer.Room);
            Command(sender, packet, now); return;
        }
        if (sender != NetworkManager.ServerClientId) return;
        if (packet.Op == Op.State)
        {
            if (packet.Nonce != _nonce || packet.Epoch == 0) return;
            if (_epoch != 0 && _epoch != packet.Epoch) Abort("Host run changed.");
            if (_clientStep == Step.Applying && packet.Committed == _clientTransaction) FinishLocal();
            _epoch = packet.Epoch; _hostSeen = now; State = packet;
            if (packet.Step == Step.Idle && _clientStep == Step.Voting) _clientStep = Step.Idle;
            if (packet.Step == Step.Voting && _room?.Room.Key == packet.Room) { _clientStep = Step.Voting; _clientDeadline = now + 35; }
            return;
        }
        if (_epoch == 0 || packet.Epoch != _epoch) return;
        if (packet.Op == Op.Abort) { AbortLocal(packet.Status); return; }
        if (packet.Op == Op.Prepare)
        {
            if (_room == null || packet.Room != _room.Room.Key || packet.Transaction <= _clientTransaction || packet.Kind != _room.Room.Kind || packet.Layer != _room.Room.Layer) return;
            _clientTransaction = packet.Transaction; _clientStep = Step.Preparing; _clientDeadline = now + 15;
            _replacement = packet.Items;
            try { _stage = _room.Stage(packet.Items); Reply(Op.Prepared, true); }
            catch (Exception e) { Plugin.Logger.LogWarning("Reroll prepare rejected: " + e.Message); Reply(Op.Prepared, false); }
        }
        else if (packet.Transaction == _clientTransaction && packet.Op == Op.Apply && _stage != null && _clientStep == Step.Preparing)
        {
            _clientStep = Step.Applying; _clientDeadline = now + 15;
            try { _stage.Apply(); Reply(Op.Applied, true); } catch (Exception e) { Plugin.Logger.LogWarning("Reroll apply rejected: " + e.Message); Reply(Op.Applied, false); }
        }
        else if (packet.Transaction == _clientTransaction && packet.Op == Op.Finish && _clientStep == Step.Applying)
        {
            FinishLocal(); State = packet with { Op = Op.State, Step = Step.Idle };
        }
    }
    private void Reply(Op op, bool ok) => _transport.Send(NetworkManager.ServerClientId, new() { Op = op, Epoch = _epoch, Nonce = _nonce,
        Sequence = ++_sequence, Transaction = _clientTransaction, Ok = ok });
    internal void QueueVote(int player, bool cancel = false)
    { if (_commands.Count < 16 && _room != null) _commands.Enqueue((player, cancel, _room.Room.Key, State.Revision)); }
    private void Vote(int player)
    {
        if (_failed || _room == null || !ReadSeats().Any(s => s.Client == Local && s.Player == player)) return;
        var command = new Packet { Op = Op.Vote, Player = player, Room = _room.Room.Key, Revision = State.Revision, Epoch = _epoch, Nonce = _nonce, Sequence = ++_sequence };
        if (Host) Command(Local, command, Time.realtimeSinceStartupAsDouble); else if (State.Ok && _epoch != 0) _transport.Send(NetworkManager.ServerClientId, command);
    }
    internal void Cancel(int player)
    {
        if (!ReadSeats().Any(s => s.Client == Local && s.Player == player)) return;
        if (Host) Abort("Party cancelled; no charge spent."); else if (_epoch != 0)
            _transport.Send(NetworkManager.ServerClientId, new() { Op = Op.Cancel, Player = player, Room = _room?.Room.Key ?? "", Epoch = _epoch, Nonce = _nonce, Sequence = ++_sequence });
    }
    private void Command(ulong sender, Packet p, double now)
    {
        if (p.Op == Op.Cancel && Busy && ReadSeats().Any(s => s.Client == sender && s.Player == p.Player)) { Abort("Party cancelled; no charge spent."); return; }
        if (p.Op is Op.Prepared or Op.Applied)
        {
            if (p.Transaction != _consensus.Transaction) return;
            if (!p.Ok) { Abort("A player could not prepare the offers; no charge spent."); return; }
            _consensus.Ack(sender, p.Transaction, p.Op == Op.Prepared ? Step.Preparing : Step.Applying); Advance(now); return;
        }
        if (p.Op != Op.Vote || _room == null || p.Room != _room.Room.Key || p.Revision != _ledger.Revision || _ledger.Charges == 0 || !AllPresent()) return;
        if (_consensus.Step == Step.Idle && !_consensus.Begin(p.Room, ReadSeats(), now, ++_transaction)) return;
        if (_consensus.Vote(sender, p.Player, p.Room)) { Status("Confirm a party reroll. Normal Confirm accepts; Back cancels."); Advance(now); }
    }
    private void Advance(double now)
    {
        var committed = false;
        try
        {
            if (_consensus.AllVoted)
            {
                _replacement = _room.Generate(_ledger.Revision); _stage = _room.Stage(_replacement); _consensus.Prepare(now);
                _consensus.Ack(Local, _consensus.Transaction, Step.Preparing);
                Broadcast(new() { Op = Op.Prepare, Epoch = _epoch, Room = _room.Room.Key, Kind = _room.Room.Kind, Layer = _room.Room.Layer,
                    Transaction = _consensus.Transaction, Items = _replacement });
            }
            if (_consensus.Step == Step.Preparing && _consensus.AllAcknowledged)
            {
                _consensus.Apply(now); _stage.Apply(); _consensus.Ack(Local, _consensus.Transaction, Step.Applying);
                Broadcast(new() { Op = Op.Apply, Epoch = _epoch, Transaction = _consensus.Transaction });
            }
            if (_consensus.Step == Step.Applying && _consensus.AllAcknowledged)
            {
                Save(_ledger.Charges - 1, _ledger.Revision + 1);
                if (!_ledger.Commit(_consensus.Transaction)) throw new InvalidOperationException("Reroll commit was rejected.");
                committed = true;
                FinishLocal();
                Broadcast(Snapshot() with { Op = Op.Finish, Step = Step.Idle, Charges = _ledger.Charges, Revision = _ledger.Revision, Status = "Offers rerolled. One shared charge spent." });
                _consensus.Clear(); Status("Offers rerolled. One shared charge spent.");
                Plugin.Logger.LogInfo("Rerolled " + _room.Room.Kind + "; party charges=" + _ledger.Charges);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("Reroll coordination warning: " + e.Message);
            if (committed) { _consensus.Clear(); State = State with { Status = "Reroll committed; synchronizing final status." }; }
            else Abort("Reroll could not complete; original offers restored, no charge spent.");
        }
    }
    internal void ObserveBattle()
    {
        if (!Host || _run.Length == 0 || !NetworkSession.InstanceExists) return;
        var level = NetworkSession.Instance.Level;
        if (level == null || level.TryCast<GameLevelTutorial>() != null || !level.playersSpawned || level.ActiveGameState != GameLevel.eGameState.RUNNING) return;
        var data = NetworkSession.Instance.runDataManager;
        var round = _run + ":" + data.CompletedLevels + ":" + level.sceneName;
        if (_round == round) return;
        _round = round; _ledger.BeginBattle(round, LivePlayers().Select(p => p.playerData.playerID));
    }
    internal void Damage(Player player, int amount)
    { if (Host && amount > 0) { ObserveBattle(); if (NetworkSession.Instance.Level?.ActiveGameState == GameLevel.eGameState.RUNNING && player?.playerData != null) _ledger.Damage(player.playerData.playerID, amount); } }
    internal void Completed(GameLevel level)
    {
        if (!Host || level.hasCompletedLevelForPeer || level.ActiveGameState != GameLevel.eGameState.LEVEL_COMPLETE || level.TryCast<GameLevelTutorial>() != null) return;
        var next = NetworkSession.Instance.runDataManager.CompletedLevels + 1;
        if (next <= _lastFinished) return;
        foreach (var player in LivePlayers()) if (player.playerData.lastFightDamageTaken > 0) _ledger.Damage(player.playerData.playerID, player.playerData.lastFightDamageTaken);
        var rewarded = _ledger.CompleteBattle(_round, true, LivePlayers().Where(p => !p.IsDead && p.health.Value > 0).Select(p => p.playerData.playerID));
        _lastFinished = next; Save();
        if (rewarded) { Status("Damage-free survivor: +1 party reroll."); Plugin.Logger.LogInfo("Damage-free survivor awarded one party reroll; charges=" + _ledger.Charges); }
    }
    private static Player[] LivePlayers()
    {
        if (!PlayerManager.InstanceExists || PlayerManager.Instance.players == null) return Array.Empty<Player>();
        return PlayerManager.Instance.players.ToArray().Where(p => p != null && p.IsSpawned && p.playerData != null && p.clientID?.Value != null).ToArray();
    }
    private void Abort(string reason)
    { if (Host && Busy) Broadcast(new() { Op = Op.Abort, Epoch = _epoch, Transaction = _consensus.Transaction, Status = reason }); AbortLocal(reason); }
    private void AbortLocal(string reason)
    {
        try { _stage?.Dispose(); } finally { _stage = null; _replacement = null; _consensus.Clear(); _clientStep = Step.Idle; State = State with { Step = Step.Idle, Votes = Array.Empty<int>(), Status = reason }; }
    }
    private void FinishLocal()
    {
        // A committed transaction must never be rolled back because old-card cleanup failed.
        try { _stage?.Finish(); } catch (Exception e) { Plugin.Logger.LogWarning("Reroll committed; old-card cleanup warning: " + e.GetType().Name); }
        _stage = null; _clientStep = Step.Idle;
    }
    private void ResetConnection()
    {
        if (_transport == null) return;
        AbortLocal("Waiting for a run."); _transport.Dispose(); _transport = null; _session = IntPtr.Zero;
        _peers.Clear(); _commands.Clear(); _room = null; _epoch = 0; _run = ""; _round = ""; _clientTransaction = 0; _hostSeen = _helloAt = _advertAt = 0;
        State = new() { Status = "Waiting for a run." };
    }
    private void Fail(Exception e)
    { try { Abort("Rerolls unavailable for this launch."); } catch { } _failed = true; Plugin.Logger.LogError("Rerolls disabled safely: " + e); }
    public void Dispose() { ResetConnection(); _failed = true; }
}
