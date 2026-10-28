using ChainedBeasts.Rerolls.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Steamworks;
using Unity.Netcode;
using UnityEngine;
using Allocator = Unity.Collections.Allocator;

namespace ChainedBeasts.Rerolls;

internal sealed class Transport : IDisposable
{
    internal const string Channel = "cb.rerolls.v1", Tag = "cb_rerolls_v1";
    internal NetworkManager Manager;
    private CustomMessagingManager.HandleNamedMessageDelegate _handler;
    private readonly Queue<(ulong Sender, Packet Packet)> _inbox = new();
    private CSteamID _lobby;
    private bool _disposed;
    internal Transport(NetworkManager manager)
    {
        Manager = manager;
        _handler = (Action<ulong, FastBufferReader>)Receive;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(Channel, _handler);
    }
    internal bool HostAdvertised()
    {
        if (!NetworkManagerEx.InstanceExists) return false;
        var transport = NetworkManagerEx.Instance.Transport?.TryCast<SteamTransportController>();
        var lobby = transport?.lobbyID ?? default;
        if (!lobby.IsValid() || !lobby.IsLobby()) return false;
        _lobby = lobby;
        var owner = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID;
        if (Manager.IsServer && owner == SteamUser.GetSteamID().m_SteamID)
            SteamMatchmaking.SetLobbyData(lobby, Tag, owner + ":" + Plugin.Version);
        return owner != 0 && SteamMatchmaking.GetLobbyData(lobby, Tag) == owner + ":" + Plugin.Version;
    }
    private void Receive(ulong sender, FastBufferReader reader)
    {
        if (_disposed || _inbox.Count >= 64) return;
        try
        {
            var length = reader.Length - reader.Position;
            if (length is < 2 or > Wire.MaximumBytes || !reader.TryBeginRead(length)) return;
            var bytes = new Il2CppStructArray<byte>(length); reader.ReadBytesSafe(ref bytes, length);
            if (Wire.TryDecode(bytes.ToArray(), out var packet)) _inbox.Enqueue((sender, packet));
        }
        catch (Exception e) { Plugin.Logger.LogWarning("Reroll packet rejected: " + e.GetType().Name); }
    }
    internal (ulong Sender, Packet Packet)[] Drain()
    { var result = _inbox.ToArray(); _inbox.Clear(); return result; }
    internal bool Send(ulong client, Packet packet)
    {
        if (_disposed || !Manager.IsListening) return false;
        var bytes = Wire.Encode(packet); var writer = new FastBufferWriter(bytes.Length, Allocator.Temp, bytes.Length);
        try
        {
            writer.WriteBytesSafe((Il2CppStructArray<byte>)bytes);
            Manager.CustomMessagingManager.SendNamedMessage(Channel, client, writer, NetworkDelivery.ReliableSequenced);
            return true;
        }
        catch (Exception e) { Plugin.Logger.LogWarning("Reroll message delivery failed: " + e.GetType().Name); return false; }
        finally { writer.Dispose(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { Manager.CustomMessagingManager?.UnregisterNamedMessageHandler(Channel); } catch { }
        try { if (Manager.IsServer && _lobby.IsValid() && SteamMatchmaking.GetLobbyOwner(_lobby).m_SteamID == SteamUser.GetSteamID().m_SteamID)
            SteamMatchmaking.DeleteLobbyData(_lobby, Tag); } catch { }
        _inbox.Clear(); _handler = null;
    }
}
