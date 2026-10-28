using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChainedBeasts.Rerolls.Core;

public enum OfferKind { None, Skills, Potions, Equipment }
public enum Step { Idle, Voting, Preparing, Applying }
public enum Op { Hello, State, Vote, Cancel, Prepare, Prepared, Apply, Applied, Finish, Abort }
public sealed record Seat(ulong Client, int Player);
public sealed record SavedLedger(int Version, string Run, int Charges, int Revision, int LastFinishedLevel);
public sealed record Item(int Id, int Heal = 0, int Size = 0, bool Shared = false, int Model = 0, string Name = "");
public sealed record Room(string Key, OfferKind Kind, ulong Scene, int Layer, Item[] Items);
public sealed record Packet
{
    public const int Protocol = 1;
    public int Version { get; init; } = Protocol;
    public Op Op { get; init; }
    public ulong Epoch { get; init; }
    public ulong Nonce { get; init; }
    public ulong Sequence { get; init; }
    public string Build { get; init; } = "";
    public string Room { get; init; } = "";
    public OfferKind Kind { get; init; }
    public int Layer { get; init; }
    public int Player { get; init; } = -1;
    public long Transaction { get; init; }
    public long Committed { get; init; }
    public int Charges { get; init; }
    public int Revision { get; init; }
    public Step Step { get; init; }
    public int[] Votes { get; init; } = Array.Empty<int>();
    public Item[] Items { get; init; } = Array.Empty<Item>();
    public bool Ok { get; init; } = true;
    public string Status { get; init; } = "";
}

public static class Wire
{
    public const int MaximumBytes = 16384;
    public static byte[] Encode(Packet value)
    {
        if (!Valid(value)) throw new InvalidDataException("Invalid reroll packet.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Reroll packet too large.");
        return bytes;
    }
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out Packet? value)
    {
        value = null;
        if (bytes.Length is < 2 or > MaximumBytes) return false;
        try { value = JsonSerializer.Deserialize<Packet>(bytes); return value != null && Valid(value); }
        catch (JsonException) { return false; }
    }
    public static bool Valid(Packet p) => p.Version == Packet.Protocol && Enum.IsDefined(typeof(Op), p.Op) &&
        Enum.IsDefined(typeof(OfferKind), p.Kind) && Enum.IsDefined(typeof(Step), p.Step) &&
        p.Player is >= -1 and < 8 && p.Revision >= 0 && p.Transaction >= 0 && p.Committed >= 0 && p.Charges is >= 0 and <= 1000000 &&
        p.Build is { Length: <= 128 } && p.Room is { Length: <= 128 } && p.Status is { Length: <= 240 } &&
        p.Votes is { Length: <= 8 } && p.Votes.All(x => x is >= 0 and < 8) && p.Votes.Distinct().Count() == p.Votes.Length &&
        p.Items is { Length: <= 32 } && p.Items.All(ValidItem);
    public static bool ValidItem(Item? i) => i != null && i.Id is >= -1 and <= 1000000 && i.Heal is >= 0 and <= 100000 &&
        i.Size is >= 0 and <= 100 && i.Model is >= 0 and <= 4096 && i.Name is { Length: <= 160 } && !i.Name.Any(char.IsControl);
    public static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    public static string ItemHash(IEnumerable<Item> items) => Hash(JsonSerializer.Serialize(items));
}

/// <summary>One shared currency. A transaction is charged only after every participant applied it.</summary>
public sealed class PartyLedger
{
    public int Charges { get; private set; }
    public int Revision { get; private set; }
    public string Run { get; private set; } = "";
    private long _lastCommitted;
    public long LastCommitted => _lastCommitted;
    private readonly HashSet<string> _completed = new();
    private readonly HashSet<int> _hurt = new();
    private readonly HashSet<int> _started = new();
    private string _round = "";
    public void Start(string run, int charges = 1, int revision = 0)
    {
        if (string.IsNullOrEmpty(run) || charges is < 0 or > 1000000 || revision < 0) throw new ArgumentException("Invalid run ledger.");
        Run = run; Charges = charges; Revision = revision; _lastCommitted = 0;
        _completed.Clear(); _hurt.Clear(); _started.Clear(); _round = "";
    }
    public void BeginBattle(string round, IEnumerable<int> players)
    {
        if (round == _round || _completed.Contains(round)) return;
        _round = round; _hurt.Clear(); _started.Clear();
        foreach (var player in players.Where(x => x is >= 0 and < 8)) _started.Add(player);
    }
    public void Damage(int player, int amount) { if (amount > 0 && _started.Contains(player)) _hurt.Add(player); }
    public bool WasDamaged(int player) => _hurt.Contains(player);
    public bool CompleteBattle(string round, bool won, IEnumerable<int> survivors)
    {
        if (Run.Length == 0 || round != _round || !_completed.Add(round)) return false;
        var reward = won && survivors.Any(p => _started.Contains(p) && !_hurt.Contains(p));
        _started.Clear(); _hurt.Clear();
        if (!reward || Charges == 1000000) return false;
        Charges++; Revision++; return true;
    }
    public bool Commit(long transaction)
    {
        if (Run.Length == 0 || Charges < 1 || transaction <= _lastCommitted) return false;
        _lastCommitted = transaction; Charges--; Revision++; return true;
    }
}

/// <summary>Per-character consent followed by per-machine prepare/apply acknowledgements.</summary>
public sealed class Consensus
{
    public Step Step { get; private set; }
    public string Room { get; private set; } = "";
    public long Transaction { get; private set; }
    public IReadOnlyList<Seat> Seats => _seats;
    public int[] Votes => _votes.OrderBy(x => x).ToArray();
    public double Deadline { get; private set; }
    private Seat[] _seats = Array.Empty<Seat>();
    private readonly HashSet<int> _votes = new();
    private readonly HashSet<ulong> _acks = new();
    public bool Begin(string room, IEnumerable<Seat> seats, double now, long transaction)
    {
        var list = seats.OrderBy(s => s.Player).ToArray();
        if (Step != Step.Idle || room.Length == 0 || !double.IsFinite(now) || transaction <= Transaction || list.Length is < 1 or > 8 ||
            list.Any(s => s.Player is < 0 or > 7) || list.Select(s => s.Player).Distinct().Count() != list.Length) return false;
        Room = room; Transaction = transaction; _seats = list; Step = Step.Voting; Deadline = now + 30;
        _votes.Clear(); _acks.Clear(); return true;
    }
    public bool Vote(ulong sender, int player, string room)
    {
        if (Step != Step.Voting || room != Room || !_seats.Any(s => s.Client == sender && s.Player == player)) return false;
        _votes.Add(player); return true;
    }
    public bool AllVoted => Step == Step.Voting && _votes.Count == _seats.Length;
    public bool Prepare(double now)
    {
        if (!AllVoted) return false;
        Step = Step.Preparing; _acks.Clear(); Deadline = now + 12; return true;
    }
    public bool Ack(ulong sender, long transaction, Step phase)
    {
        if (phase != Step || phase is not (Step.Preparing or Step.Applying) || transaction != Transaction || !_seats.Any(s => s.Client == sender)) return false;
        _acks.Add(sender); return true;
    }
    public bool AllAcknowledged => Step is Step.Preparing or Step.Applying && _seats.All(s => _acks.Contains(s.Client));
    public bool Apply(double now)
    {
        if (Step != Step.Preparing || !AllAcknowledged) return false;
        Step = Step.Applying; _acks.Clear(); Deadline = now + 12; return true;
    }
    public bool SameParty(IEnumerable<Seat> seats) => _seats.SequenceEqual(seats.OrderBy(s => s.Player));
    public bool Expired(double now) => Step != Step.Idle && (!double.IsFinite(now) || now >= Deadline);
    public void Clear() { Step = Step.Idle; Room = ""; _seats = Array.Empty<Seat>(); _votes.Clear(); _acks.Clear(); Deadline = 0; }
}
