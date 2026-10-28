using System.Text.Json;
using BepInEx;
using ChainedBeasts.Rerolls.Core;

namespace ChainedBeasts.Rerolls;

internal static class LedgerStore
{
    private static string Folder => Path.Combine(Paths.ConfigPath, "ChainedBeasts.Rerolls");
    private static string FileName(string run) => Path.Combine(Folder, Wire.Hash(run) + ".json");
    internal static SavedLedger Load(string run)
    {
        var path = FileName(run);
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length > 4096) throw new InvalidDataException("Reroll ledger too large.");
        var value = JsonSerializer.Deserialize<SavedLedger>(File.ReadAllText(path));
        if (value == null || value.Version != 1 || value.Run != run || value.Charges is < 0 or > 1000000 ||
            value.Revision < 0 || value.LastFinishedLevel < 0) throw new InvalidDataException("Invalid reroll ledger.");
        return value;
    }
    internal static void Save(SavedLedger value)
    {
        Directory.CreateDirectory(Folder);
        var temporary = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value)); File.Move(temporary, FileName(value.Run), true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
