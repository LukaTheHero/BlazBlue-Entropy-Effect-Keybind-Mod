using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GamePlay;
using Localization;
using NOAH.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Luka.BBEE.KeybindMod;

public sealed class KeybindBehaviour : MonoBehaviour
{
    public KeybindBehaviour(IntPtr ptr) : base(ptr) { }

    private static readonly Regex RichTagRegex = new(@"</?(?:color|size|voffset|font|combo|dynamic)(?:=[^>]*)?>|<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineActionTagRegex = new(@"<action=([^>]+)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly InputSequencer _sequencer = new();
    private readonly KeybindMenuUi _menuUi = new();
    private readonly RuntimeStateSnapshot _snapshot = new();

    private bool _menuOpen;
    private bool _releasingMenu;
    private float _nextJsPollTime;
    private float _nextCatalogRetryTime;
    private int _lastSyncedActorId;
    private KeyCode _waitReleaseAfterBind = KeyCode.None;

    private bool _savedCursorState;
    private bool _prevCursorVisible;
    private CursorLockMode _prevCursorLock;
    private readonly List<EventSystem> _blockedEventSystems = new();

    internal void ApplyJsMessage(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("kind", out var kindProp))
                return;

            var kind = kindProp.GetString();
            switch (kind)
            {
                case "ack":
                    Plugin.LogInstance?.LogInfo(root.GetProperty("message").GetString());
                    break;

                case "log":
                    var msg = root.GetProperty("message").GetString();
                    Plugin.LogInstance?.LogWarning($"[JS] {msg}");
                    break;

                case "catalog":
                    ApplyCatalogPayload(root);
                    break;

                case "state":
                    ApplyStatePayload(root);
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogInstance?.LogWarning($"Failed to parse JS bridge message: {ex.Message}");
        }
    }

    private void ApplyCatalogPayload(System.Text.Json.JsonElement root)
    {
        _snapshot.DiscoveredActionsByActor.Clear();
        _snapshot.DiscoveredInstructions.Clear();

        LocalizationManager? locMgr = null;
        try
        {
            var inst = LocalizationManager.Instance;
            if (inst != null && inst.IsValid)
                locMgr = inst;
        }
        catch
        {
            locMgr = null;
        }

        var rawEntries = new List<DiscoveredActionInfo>();
        var rowById = new Dictionary<uint, DiscoveredActionInfo>();
        var directNames = new Dictionary<uint, string>();

        if (root.TryGetProperty("actions", out var actionsEl) && actionsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in actionsEl.EnumerateArray())
            {
                int actorId = item.TryGetProperty("actorId", out var aEl) ? aEl.GetInt32() : 0;
                uint id = item.TryGetProperty("id", out var idEl) ? idEl.GetUInt32() : 0;
                if (actorId == 0 || id == 0)
                    continue;

                string inputRaw = item.TryGetProperty("inputRaw", out var irEl) ? (irEl.GetString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrWhiteSpace(inputRaw) && locMgr != null)
                    inputRaw = TryNativeLocale(locMgr, $"ActorActionInput_{id}");

                string name = item.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrWhiteSpace(name) && locMgr != null)
                {
                    name = CleanRichText(TryNativeLocale(locMgr, $"ActorActionName_{id}"));
                    if (string.IsNullOrWhiteSpace(name))
                        name = CleanRichText(TryNativeLocale(locMgr, $"ActorActionName2_{id}"));
                }

                string desc = item.TryGetProperty("desc", out var dEl) ? (dEl.GetString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrWhiteSpace(desc) && locMgr != null)
                    desc = CleanRichText(TryNativeLocale(locMgr, $"ActorActionDesc_{id}"));

                var inherit = ReadUIntArray(item, "inherit");
                var potIds = ReadUIntArray(item, "potentialIds");

                if (!string.IsNullOrWhiteSpace(name))
                    directNames[id] = name;

                var info = new DiscoveredActionInfo
                {
                    Id = id,
                    ActorId = actorId,
                    Type = item.TryGetProperty("type", out var tEl) ? tEl.GetInt32() : 0,
                    InputRaw = inputRaw,
                    Description = desc,
                    IsBase = item.TryGetProperty("isBase", out var ibEl) && ibEl.GetBoolean(),
                    PotentialIds = potIds,
                    ComboPotentialSets = ReadUIntMatrix(item, "comboPotentialSets"),
                    Replace = ReadUIntArray(item, "replace"),
                    Inherit = inherit,
                    IsUltra = item.TryGetProperty("isUltra", out var uEl) && uEl.GetBoolean(),
                    SortOrder = item.TryGetProperty("sortOrder", out var soEl) ? soEl.GetInt32() : 0,
                    Labels = ReadStringArray(item, "labels"),
                    Name = name
                };

                rawEntries.Add(info);
                rowById[id] = info;
            }
        }

        int resolvedCount = 0;
        for (int i = 0; i < rawEntries.Count; i++)
        {
            var entry = rawEntries[i];
            if (string.IsNullOrWhiteSpace(entry.InputRaw))
                continue;

            string finalName = entry.Name;
            if (string.IsNullOrWhiteSpace(finalName) && entry.Inherit.Length > 0)
                finalName = ResolveNameFromInherit(entry.Inherit, rowById, directNames);

            if (!string.Equals(finalName, entry.Name, StringComparison.Ordinal))
            {
                entry = new DiscoveredActionInfo
                {
                    Id = entry.Id,
                    ActorId = entry.ActorId,
                    Type = entry.Type,
                    InputRaw = entry.InputRaw,
                    Description = entry.Description,
                    IsBase = entry.IsBase,
                    PotentialIds = entry.PotentialIds,
                    ComboPotentialSets = entry.ComboPotentialSets,
                    Replace = entry.Replace,
                    Inherit = entry.Inherit,
                    IsUltra = entry.IsUltra,
                    SortOrder = entry.SortOrder,
                    Labels = entry.Labels,
                    Name = finalName
                };
            }

            if (!_snapshot.DiscoveredActionsByActor.TryGetValue(entry.ActorId, out var list))
            {
                list = new List<DiscoveredActionInfo>();
                _snapshot.DiscoveredActionsByActor[entry.ActorId] = list;
            }
            list.Add(entry);
            resolvedCount++;
        }

        if (root.TryGetProperty("instructions", out var instEl) && instEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in instEl.EnumerateArray())
            {
                _snapshot.DiscoveredInstructions.Add(new DiscoveredInstructionInfo
                {
                    Id = item.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0,
                    Instruction = item.TryGetProperty("instruction", out var inEl) ? (inEl.GetString() ?? string.Empty) : string.Empty,
                    SuccessAction = item.TryGetProperty("successAction", out var saEl) ? (saEl.GetString() ?? string.Empty) : string.Empty,
                    ActionCd = item.TryGetProperty("actionCd", out var cdEl) ? (float)cdEl.GetDouble() : 0f,
                    TriggerId = item.TryGetProperty("triggerId", out var trEl) ? trEl.GetInt32() : 0
                });
            }
        }

        if (resolvedCount > 0 && _snapshot.DiscoveredActionsByActor.Count > 0)
        {
            _snapshot.CatalogLoaded = true;
            CharacterCatalog.MergeDiscoveredCatalog(_snapshot);
            Plugin.LogInstance?.LogInfo($"Synced BBEE catalog: {resolvedCount} localized actions across {_snapshot.DiscoveredActionsByActor.Count} fighters -> {CharacterCatalog.TotalMoveCount} total bindable moves ({_snapshot.DiscoveredInstructions.Count} chant rules).");
        }
        else
        {
            _snapshot.CatalogLoaded = false;
            Plugin.LogInstance?.LogDebug($"Catalog poll deferred (raw={rawEntries.Count}, resolved={resolvedCount}, locValid={locMgr != null}).");
        }
    }

    private static string TryNativeLocale(LocalizationManager mgr, string key)
    {
        try
        {
            if (!mgr.IsExist(key))
                return string.Empty;
            string s = mgr.GetString(key);
            return !string.IsNullOrEmpty(s) && s != key ? s : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CleanRichText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string s = RichTagRegex.Replace(raw, " ");
        s = InlineActionTagRegex.Replace(s, m => m.Groups[1].Value switch
        {
            "MoveY+" => "Up",
            "MoveY-" => "Down",
            "MoveX+" => "Right",
            "MoveX-" => "Left",
            "MoveX" => "Forward",
            "MoveY" => "Up/Down",
            "Summon" => "SP",
            "SpecialAction1" => "Special",
            "SpecialAction2" => "Special 2",
            "Dynamic" => "SP",
            _ => m.Groups[1].Value
        });
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string ResolveNameFromInherit(
        uint[] initialInherit,
        Dictionary<uint, DiscoveredActionInfo> rowById,
        Dictionary<uint, string> directNames)
    {
        var visited = new HashSet<uint>();
        var queue = new Queue<uint>(initialInherit);
        int hops = 0;

        while (queue.Count > 0 && hops++ < 10)
        {
            uint cur = queue.Dequeue();
            if (cur == 0 || !visited.Add(cur))
                continue;

            if (directNames.TryGetValue(cur, out var nm) && !string.IsNullOrWhiteSpace(nm))
                return nm;

            if (rowById.TryGetValue(cur, out var parent))
            {
                for (int i = 0; i < parent.Inherit.Length; i++)
                    queue.Enqueue(parent.Inherit[i]);
            }
        }

        return string.Empty;
    }

    private void ApplyStatePayload(System.Text.Json.JsonElement root)
    {
        if (root.TryGetProperty("fesActorId", out var fesEl))
            _snapshot.FesActorId = fesEl.GetInt32();

        _snapshot.ActivePotentials.Clear();
        if (root.TryGetProperty("potentials", out var potsEl) && potsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var p in potsEl.EnumerateArray())
            {
                uint pid = p.GetUInt32();
                if (pid > 0)
                    _snapshot.ActivePotentials.Add(pid);
            }
        }

        _snapshot.ObtainedActionIds.Clear();
        if (root.TryGetProperty("obtainedActionIds", out var oaEl) && oaEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var a in oaEl.EnumerateArray())
            {
                uint aid = a.GetUInt32();
                if (aid > 0)
                    _snapshot.ObtainedActionIds.Add(aid);
            }
        }

        _snapshot.UnlockedFighters.Clear();
        if (root.TryGetProperty("unlockedFighters", out var fEl) && fEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var f in fEl.EnumerateArray())
                _snapshot.UnlockedFighters.Add(f.GetInt32());
        }
    }

    public void Update()
    {
        if (Plugin.Enabled?.Value != true)
        {
            if (_menuOpen || _releasingMenu)
                ReleaseMenuImmediate();
            return;
        }

        // Poll until game Xlsx + LocalizationManager finish loading after boot
        if (!_snapshot.CatalogLoaded && Time.realtimeSinceStartup >= _nextCatalogRetryTime)
        {
            _nextCatalogRetryTime = Time.realtimeSinceStartup + 1.5f;
            bool locValid = false;
            try
            {
                var loc = LocalizationManager.Instance;
                locValid = loc != null && loc.IsValid;
            }
            catch
            {
                locValid = false;
            }

            if (locValid)
                Plugin.RequestJsSnapshot(true);
        }

        // 1. Read live C# combat state directly every frame (only playable fighters count as InBattle)
        int playerId = 0;
        float facing = 1f;
        bool csharpInBattle = false;
        bool csharpLocked = false;
        int csharpActorId = 0;
        bool isAirborne = false;

        try
        {
            var battle = BattleBase.Cur;
            var player = battle?.PlayerSelf;
            if (battle != null && player != null && CharacterCatalog.FindCharacter(player.BaseID) != null)
            {
                csharpInBattle = true;
                csharpActorId = player.BaseID;
                facing = player.Dir;
                isAirborne = player.ActorPosition?.IsOffGround ?? false;
                if (player.Input != null)
                    playerId = player.Input.PlayerId;

                InputSequencer.ActivePlayerId = playerId;

                var gim = SingletonBehaviour<GameInputManager>.Instance;
                if (gim != null)
                {
                    if ((int)gim.ActorActionFlag != 0 || gim.ActorActionLock || battle.TimeScale <= 0f)
                        csharpLocked = true;
                }
            }
        }
        catch
        {
            // Out of battle or scene transition
        }

        _snapshot.InBattle = csharpInBattle;
        _snapshot.ActiveActorId = csharpActorId;
        _snapshot.InputLocked = csharpLocked;

        if (csharpInBattle && (csharpActorId != _lastSyncedActorId || Time.realtimeSinceStartup >= _nextJsPollTime))
        {
            _lastSyncedActorId = csharpActorId;
            _nextJsPollTime = Time.realtimeSinceStartup + 1.25f;
            Plugin.RequestJsSnapshot(false);
        }

        // 2. Menu toggle hotkey (default F4) & deferred release guard
        var menuKey = Plugin.MenuKey?.Value ?? KeyCode.F4;
        if (_releasingMenu)
        {
            MaintainMenuCursor();
            if (!IsCloseInputHeld(menuKey))
                ReleaseMenuImmediate();
            return;
        }

        if (menuKey != KeyCode.None && UnityEngine.Input.GetKeyDown(menuKey))
        {
            ToggleMenu();
            return;
        }

        // 3. Menu open: capture keybinds cleanly, cancel combat loops
        if (_menuOpen)
        {
            if (_sequencer.IsRunning)
                _sequencer.Cancel(playerId);

            MaintainMenuCursor();
            HandleMenuInput();
            return;
        }

        // 4. Live combat keybind execution (strictly only when unlocked in battle)
        if (!csharpInBattle || csharpLocked || csharpActorId == 0)
        {
            if (_sequencer.IsRunning)
                _sequencer.Cancel(playerId);
            return;
        }

        var specialOverride = Plugin.SpecialCmdOverride?.Value ?? "Auto";
        _sequencer.Update(playerId, csharpActorId, csharpLocked, facing, specialOverride, isAirborne);

        if (Plugin.Bindings == null)
            return;

        var moves = CharacterCatalog.GetMoves(csharpActorId);
        for (int i = 0; i < moves.Count; i++)
        {
            var move = moves[i];
            if (!move.IsUnlockedInRun(_snapshot))
                continue;

            var key = Plugin.Bindings.GetBinding(move.Id);
            if (key == KeyCode.None)
                continue;

            if (UnityEngine.Input.GetKeyDown(key))
            {
                bool repeatWhileHeld = Plugin.Bindings.GetHoldRepeat(move);
                _sequencer.Start(move, key, playerId, facing, specialOverride, repeatWhileHeld, isAirborne);
                break;
            }
        }
    }

    public void OnGUI()
    {
        if (Plugin.Enabled?.Value != true)
            return;

        try
        {
            if (_menuOpen && Plugin.Bindings != null)
            {
                _menuUi.Draw(
                    _snapshot,
                    Plugin.Bindings,
                    Plugin.MenuKey?.Value ?? KeyCode.F4,
                    Plugin.ShowActiveHud?.Value ?? false,
                    show =>
                    {
                        if (Plugin.ShowActiveHud != null)
                            Plugin.ShowActiveHud.Value = show;
                    },
                    CloseMenu);
            }
            else if ((Plugin.ShowActiveHud?.Value ?? false) && Plugin.Bindings != null)
            {
                _menuUi.DrawMiniHud(_snapshot, Plugin.Bindings);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogInstance?.LogError($"Keybind UI error (auto-closing safely): {ex}");
            ReleaseMenuImmediate();
        }
    }

    private void ToggleMenu()
    {
        if (_menuOpen)
            CloseMenu();
        else
            OpenMenu();
    }

    private void OpenMenu()
    {
        if (_menuOpen)
            return;

        _releasingMenu = false;
        _menuOpen = true;
        _waitReleaseAfterBind = KeyCode.None;
        _menuUi.ClicksLocked = false;
        _menuUi.SuppressClicksUntil = 0f;
        _menuUi.CancelListening();

        if (_snapshot.InBattle && _snapshot.ActiveActorId != 0)
            _menuUi.SelectedCharacterId = _snapshot.ActiveActorId;

        Plugin.RequestJsSnapshot(!_snapshot.CatalogLoaded);
        Plugin.SetJsBattlePaused(true);

        if (!_savedCursorState)
        {
            _savedCursorState = true;
            _prevCursorVisible = Cursor.visible;
            _prevCursorLock = Cursor.lockState;
        }

        MaintainMenuCursor();
    }

    private void CloseMenu()
    {
        if (!_menuOpen)
            return;

        _menuOpen = false;
        _releasingMenu = true;
        _waitReleaseAfterBind = KeyCode.None;
        _menuUi.ClicksLocked = false;
        _menuUi.CancelListening();
    }

    internal void ForceCloseMenu() => ReleaseMenuImmediate();

    private void ReleaseMenuImmediate()
    {
        if (!_menuOpen && !_releasingMenu && !_savedCursorState && _blockedEventSystems.Count == 0)
            return;

        _menuOpen = false;
        _releasingMenu = false;
        _waitReleaseAfterBind = KeyCode.None;
        _menuUi.ClicksLocked = false;
        _menuUi.SuppressClicksUntil = 0f;
        _menuUi.CancelListening();
        Plugin.SetJsBattlePaused(false);

        for (int i = 0; i < _blockedEventSystems.Count; i++)
        {
            var es = _blockedEventSystems[i];
            try
            {
                if (es != null)
                    es.enabled = true;
            }
            catch
            {
                // Ignore destroyed EventSystem across scene transitions
            }
        }
        _blockedEventSystems.Clear();

        if (_savedCursorState)
        {
            _savedCursorState = false;
            try
            {
                Cursor.lockState = _prevCursorLock;
            }
            catch
            {
                // Ignore cursor state cleanup errors on teardown
            }

            try
            {
                Cursor.visible = _prevCursorVisible;
            }
            catch
            {
                // Ignore cursor visibility cleanup errors on teardown
            }
        }
    }

    private void MaintainMenuCursor()
    {
        var current = EventSystem.current;
        if (current != null && current.enabled)
        {
            current.enabled = false;
            bool alreadyTracked = false;
            for (int i = 0; i < _blockedEventSystems.Count; i++)
            {
                var existing = _blockedEventSystems[i];
                if (existing != null && existing.Pointer == current.Pointer)
                {
                    alreadyTracked = true;
                    break;
                }
            }
            if (!alreadyTracked)
                _blockedEventSystems.Add(current);
        }

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private static bool IsCloseInputHeld(KeyCode menuKey) =>
        UnityEngine.Input.GetKey(KeyCode.Mouse0)
        || UnityEngine.Input.GetKey(KeyCode.Escape)
        || (menuKey != KeyCode.None && UnityEngine.Input.GetKey(menuKey));

    private void HandleMenuInput()
    {
        if (_waitReleaseAfterBind != KeyCode.None)
        {
            _menuUi.ClicksLocked = true;
            if (!UnityEngine.Input.GetKey(_waitReleaseAfterBind) && !UnityEngine.Input.GetKey(KeyCode.Mouse0))
            {
                _waitReleaseAfterBind = KeyCode.None;
                _menuUi.ClicksLocked = false;
            }
            else
            {
                return;
            }
        }

        if (_menuUi.ListeningMoveId == null)
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                CloseMenu();
            return;
        }

        if (_menuUi.WaitingMouseReleaseBeforeListen)
        {
            if (!UnityEngine.Input.GetKey(KeyCode.Mouse0))
                _menuUi.WaitingMouseReleaseBeforeListen = false;
            return;
        }

        if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
        {
            Plugin.Bindings?.SetBinding(_menuUi.ListeningMoveId, KeyCode.None);
            _menuUi.CancelListening();
            _waitReleaseAfterBind = KeyCode.Escape;
            _menuUi.ClicksLocked = true;
            _menuUi.SuppressClicksUntil = Time.realtimeSinceStartup + 0.18f;
            return;
        }

        var menuKey = Plugin.MenuKey?.Value ?? KeyCode.F4;
        var bindable = KeyCodeFormatter.GetBindableKeys();
        for (int i = 0; i < bindable.Length; i++)
        {
            var kc = bindable[i];
            if (kc == menuKey)
                continue;

            if (UnityEngine.Input.GetKeyDown(kc))
            {
                Plugin.Bindings?.SetBinding(_menuUi.ListeningMoveId, kc);
                _menuUi.CancelListening();
                _waitReleaseAfterBind = kc;
                _menuUi.ClicksLocked = true;
                _menuUi.SuppressClicksUntil = Time.realtimeSinceStartup + 0.18f;
                return;
            }
        }
    }

    public void OnDisable()
    {
        ReleaseMenuImmediate();
        _sequencer.Cancel(0);
    }

    public void OnDestroy()
    {
        ReleaseMenuImmediate();
        _sequencer.Cancel(0);
    }

    private static string[] ReadStringArray(System.Text.Json.JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var v in el.EnumerateArray())
            list.Add(v.GetString() ?? string.Empty);
        return list.ToArray();
    }

    private static uint[] ReadUIntArray(System.Text.Json.JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
            return Array.Empty<uint>();

        var list = new List<uint>();
        foreach (var v in el.EnumerateArray())
            list.Add(v.GetUInt32());
        return list.ToArray();
    }

    private static uint[][] ReadUIntMatrix(System.Text.Json.JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
            return Array.Empty<uint[]>();

        var outer = new List<uint[]>();
        foreach (var innerEl in el.EnumerateArray())
        {
            if (innerEl.ValueKind != System.Text.Json.JsonValueKind.Array)
                continue;

            var row = new List<uint>();
            foreach (var v in innerEl.EnumerateArray())
                row.Add(v.GetUInt32());
            if (row.Count > 0)
                outer.Add(row.ToArray());
        }
        return outer.ToArray();
    }
}
