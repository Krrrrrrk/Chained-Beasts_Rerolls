using ChainedBeasts.Rerolls.Core;
using UnityEngine;
using SkillList = Il2CppSystem.Collections.Generic.List<SkillType>;
using ShopList = Il2CppSystem.Collections.Generic.List<PurchaseType>;
using PotionList = Il2CppSystem.Collections.Generic.List<Potion>;

namespace ChainedBeasts.Rerolls;

internal sealed class NativeOffers
{
    internal Room Room;
    internal RunDataManager Data;
    internal UIGameLobbyTrainerRpc Trainer;
    internal UIGameLobbyDoctorRpc Doctor;
    internal UIGameLobbyShopRpc Shop;
    internal IntPtr Pointer => Trainer?.Pointer ?? Doctor?.Pointer ?? Shop?.Pointer ?? IntPtr.Zero;

    private static bool Any(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool> values) =>
        values == null || values.ToArray().Any(v => v);

    internal static NativeOffers Read()
    {
        if (!NetworkSession.InstanceExists || NetworkSession.Instance.sceneRoot == null || NetworkSession.Instance.TransitionPending ||
            NetworkSession.Instance.isRestoringFromCheckpoint || NetworkSession.Instance.IsPaused || NetworkSession.Instance.Lobby == null) return null;
        var data = NetworkSession.Instance.runDataManager;
        if (data == null || data.playerDatas == null || data.playerDatas.Count == 0) return null;
        var result = new NativeOffers { Data = data };
        foreach (var t in Resources.FindObjectsOfTypeAll<UIGameLobbyTrainerRpc>())
            if (t?.uiLobbyTrainer != null && t.uiLobbyTrainer.gameObject.activeInHierarchy && t.uiLobbyTrainer.InFocus &&
                !t.tutorialDialogueActive && t.uiState != null && t.uiState.State == UIGameLobbyTrainerRpc.eState.LEVEL_UP &&
                !Any(t.uiState.done) && !Any(t.uiState.waitingForCoroutine) && !Any(t.uiState.upgradeAutoSelect))
            {
                var layer = t.uiState.trainingLayer;
                if (data.trainingSkills == null || layer < 0 || layer >= data.trainingSkills.Count || data.trainingSkills[layer] == null) continue;
                result.Trainer = t;
                return result.Set(OfferKind.Skills, layer, data.trainingSkills[layer].ToArray().Select(i => new Item((int)i)).ToArray());
            }
        foreach (var d in Resources.FindObjectsOfTypeAll<UIGameLobbyDoctorRpc>())
            if (d?.uiLobbyDoctor != null && d.uiLobbyDoctor.gameObject.activeInHierarchy && d.uiLobbyDoctor.InFocus &&
                !d.tutorialDialogueActive && !d.potionsConsumed && !d.executeSelectionsScheduled && !d.executed && d.uiState != null &&
                !Any(d.uiState.selected) && !Any(d.uiState.autoSelect) && !Any(d.uiState.waitingForAutoSelect) && !Any(d.uiState.twitchIsSelected) &&
                data.potions != null && d.uiLobbyDoctor.selectionPages?.Count == data.potions.Count)
            {
                result.Doctor = d;
                return result.Set(OfferKind.Potions, 0, data.potions.ToArray().Select(ToItem).ToArray());
            }
        foreach (var s in Resources.FindObjectsOfTypeAll<UIGameLobbyShopRpc>())
            if (s?.ui != null && s.ui.gameObject.activeInHierarchy && s.ui.InFocus && !s.tutorialDialogueActive &&
                !s.executed && !s.waitingForTransitionOut && s.uiState != null && !Any(s.uiState.ready) && !Any(s.uiState.purchased) && !Any(s.uiState.locked) &&
                data.currentShopOptions != null && s.ui.selectionPages?.Count == data.currentShopOptions.Count)
            {
                result.Shop = s;
                return result.Set(OfferKind.Equipment, 0, data.currentShopOptions.ToArray().Select(i => new Item((int)i)).ToArray());
            }
        return null;
    }

    private NativeOffers Set(OfferKind kind, int layer, Item[] items)
    {
        if (items.Length is < 1 or > 32) return null;
        var scene = NetworkSession.Instance.sceneRoot.NetworkObjectId;
        var key = Wire.Hash($"{Data.RunSeed}:{Data.RunStartTime}:{Data.CompletedLevels}:{scene}:{kind}:{layer}:{Wire.ItemHash(items)}");
        Room = new(key, kind, scene, layer, items); return this;
    }
    private static Item ToItem(Potion p) => new((int)p.injury, p._healAmount, p.potionSize, p.shared, p.modelIdx, p.nameTerm ?? "");
    internal Item[] Generate(int revision)
    {
        // Native generators install a global RandomUtils generator. Restore it even on failure.
        var previous = RandomUtils.rand;
        try
        {
            for (var attempt = 0; attempt < 24; attempt++)
            {
                var seed = unchecked(Data.RunSeed * 397 ^ Data.CompletedLevels * 8191 ^ (revision + 1) * 104729 ^ attempt * 7919 ^ (int)Room.Kind * 65537);
                Item[] items;
                if (Room.Kind == OfferKind.Skills)
                {
                    var generated = SkillGenerator.GenerateTrainingSkills(seed, null);
                    if (generated == null || generated.Count <= Room.Layer || generated[Room.Layer] == null) continue;
                    items = generated[Room.Layer].ToArray().Select(i => new Item((int)i)).ToArray();
                }
                else if (Room.Kind == OfferKind.Potions)
                {
                    var generated = InjuryGenerator.GeneratePotions(Data, Data.playerDatas.Count, seed, out _);
                    if (generated == null) continue;
                    items = generated.ToArray().Select(ToItem).ToArray();
                }
                else
                {
                    var generated = PurchaseGenerator.GenerateShopOptions(seed, (int)GladiatorConsts.GetAct(Data.CompletedLevels));
                    if (generated == null) continue;
                    items = generated.ToArray().Select(i => new Item((int)i)).ToArray();
                }
                if (items.Length == Room.Items.Length && Wire.ItemHash(items) != Wire.ItemHash(Room.Items)) return items;
            }
            throw new InvalidOperationException("No different eligible offer set was generated; no charge spent.");
        }
        finally { RandomUtils.rand = previous; }
    }

    internal NativeStage Stage(Item[] items)
    {
        if (items.Length != Room.Items.Length || items.Any(i => !Wire.ValidItem(i))) throw new InvalidDataException("Invalid replacement offers.");
        var library = GameLibrary.Instance;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (Room.Kind == OfferKind.Skills && !library.SkillDict.ContainsKey((SkillType)item.Id)) throw new InvalidDataException("Unknown skill ID.");
            if (Room.Kind == OfferKind.Potions && !library.InjuryDict.ContainsKey((InjuryType)item.Id) && item.Id != (int)InjuryType.None)
                throw new InvalidDataException("Unknown potion injury ID.");
            if (Room.Kind == OfferKind.Equipment && !library.PurchaseDict.ContainsKey((PurchaseType)item.Id))
                throw new InvalidDataException("Unknown merchant purchase ID.");
        }
        return new NativeStage(this, items);
    }
}

/// <summary>Build replacement cards first. Keep original lists/cards until every peer applies.</summary>
internal sealed class NativeStage : IDisposable
{
    private readonly NativeOffers _room;
    private readonly SkillList _skills, _oldSkills;
    private readonly PotionList _potions, _oldPotions;
    private readonly ShopList _shop, _oldShop;
    private readonly List<UILobbyDoctorSelectionPage> _doctorPages = new(), _oldDoctor = new();
    private readonly List<UILobbyShopSelectionPage> _shopPages = new(), _oldShopPages = new();
    private bool _applied, _finished;
    internal NativeStage(NativeOffers room, Item[] items)
    {
        _room = room;
        try
        {
            if (room.Trainer != null)
            {
                _oldSkills = room.Data.trainingSkills[room.Room.Layer]; _skills = new();
                foreach (var item in items) _skills.Add((SkillType)item.Id);
            }
            else if (room.Doctor != null)
            {
                _oldPotions = room.Data.potions; _potions = new();
                foreach (var item in items) _potions.Add(new Potion { injury = (InjuryType)item.Id, _healAmount = item.Heal,
                    potionSize = item.Size, shared = item.Shared, modelIdx = item.Model, nameTerm = item.Name });
                var ui = room.Doctor.uiLobbyDoctor;
                for (var i = 0; i < items.Length; i++)
                {
                    var old = ui.selectionPages[i]; _oldDoctor.Add(old);
                    var card = UnityEngine.Object.Instantiate(ui.selectionPagePrefab, old.transform.parent); _doctorPages.Add(card);
                    card.gameObject.SetActive(false); card.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
                    card.Initialise(ui, i, _potions[i], old.potionPosition, old.cameraPosition);
                }
            }
            else
            {
                _oldShop = room.Data.currentShopOptions; _shop = new();
                foreach (var item in items) _shop.Add((PurchaseType)item.Id);
                var ui = room.Shop.ui;
                try
                {
                    room.Data.currentShopOptions = _shop;
                    for (var i = 0; i < items.Length; i++)
                    {
                        var old = ui.selectionPages[i]; _oldShopPages.Add(old);
                        var card = UnityEngine.Object.Instantiate(ui.selectionPagePrefab, old.transform.parent); _shopPages.Add(card);
                        card.gameObject.SetActive(false); card.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
                        card.Initialise(ui, i, old.cameraPosition, old.worldAnchor);
                    }
                }
                finally { room.Data.currentShopOptions = _oldShop; }
            }
        }
        catch { Dispose(); throw; }
    }
    internal void Apply()
    {
        if (_applied) return;
        _applied = true;
        if (_room.Trainer != null)
        {
            _room.Data.trainingSkills[_room.Room.Layer] = _skills;
            // Preserve native per-player refresh counts, training progress and selection bookkeeping.
            _room.Trainer.OnTrainingSkillsChanged(_room.Room.Layer);
            _room.Trainer.uiLobbyTrainer.Refresh(_room.Trainer.uiState);
        }
        else if (_room.Doctor != null)
        {
            var ui = _room.Doctor.uiLobbyDoctor; _room.Data.potions = _potions;
            for (var i = 0; i < _doctorPages.Count; i++) { _oldDoctor[i].gameObject.SetActive(false); ui.selectionPages[i] = _doctorPages[i]; _doctorPages[i].gameObject.SetActive(true); }
            ui.Refresh(_room.Doctor.uiState);
        }
        else
        {
            var ui = _room.Shop.ui; _room.Data.currentShopOptions = _shop;
            for (var i = 0; i < _shopPages.Count; i++) { _oldShopPages[i].gameObject.SetActive(false); ui.selectionPages[i] = _shopPages[i]; _shopPages[i].gameObject.SetActive(true); }
            ui.previousCost = 0; ui.previousCostAboveZero = false; ui.Refresh(_room.Shop.uiState);
        }
    }
    internal void Finish()
    {
        _finished = true;
        foreach (var card in _oldDoctor) UnityEngine.Object.Destroy(card.gameObject);
        foreach (var card in _oldShopPages) UnityEngine.Object.Destroy(card.gameObject);
    }
    public void Dispose()
    {
        if (_finished) return;
        var sameScene = NetworkSession.InstanceExists && NetworkSession.Instance.sceneRoot != null &&
            NetworkSession.Instance.sceneRoot.NetworkObjectId == _room.Room.Scene;
        var ownsList = sameScene && (_skills != null ? _room.Data.trainingSkills != null && _room.Room.Layer < _room.Data.trainingSkills.Count &&
            _room.Data.trainingSkills[_room.Room.Layer]?.Pointer == _skills.Pointer : _potions != null ? _room.Data.potions?.Pointer == _potions.Pointer : _room.Data.currentShopOptions?.Pointer == _shop?.Pointer);
        try
        {
        if (_applied && ownsList)
        {
            if (_room.Trainer != null)
            {
                _room.Data.trainingSkills[_room.Room.Layer] = _oldSkills;
                _room.Trainer.OnTrainingSkillsChanged(_room.Room.Layer); _room.Trainer.uiLobbyTrainer.Refresh(_room.Trainer.uiState);
            }
            else if (_room.Doctor != null)
            {
                _room.Data.potions = _oldPotions;
                for (var i = 0; i < _oldDoctor.Count; i++) { _room.Doctor.uiLobbyDoctor.selectionPages[i] = _oldDoctor[i]; _oldDoctor[i].gameObject.SetActive(true); }
                _room.Doctor.uiLobbyDoctor.Refresh(_room.Doctor.uiState);
            }
            else
            {
                _room.Data.currentShopOptions = _oldShop;
                for (var i = 0; i < _oldShopPages.Count; i++) { _room.Shop.ui.selectionPages[i] = _oldShopPages[i]; _oldShopPages[i].gameObject.SetActive(true); }
                _room.Shop.ui.Refresh(_room.Shop.uiState);
            }
        }
        }
        finally
        {
        foreach (var card in _doctorPages) UnityEngine.Object.Destroy(card.gameObject);
        foreach (var card in _shopPages) UnityEngine.Object.Destroy(card.gameObject);
        _finished = true;
        }
    }
}
