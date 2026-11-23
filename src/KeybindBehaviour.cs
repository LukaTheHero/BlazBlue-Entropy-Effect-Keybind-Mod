using System;
using GamePlay;
using NOAH.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Luka.BBEE.KeybindMod;

public sealed class KeybindBehaviour : MonoBehaviour
{
    public KeybindBehaviour(IntPtr ptr) : base(ptr) { }

    private readonly InputSequencer _sequencer = new();
    private readonly KeybindMenuUi _menuUi = new();
    private readonly RuntimeStateSnapshot _snapshot = new();

    private bool _menuOpen;
    private float _nextJsPollTime;
    private int _lastSyncedActorId;

    private bool _savedCursorState;
    private bool _prevCursorVisible;
    private CursorLockMode _prevCursorLock;
    private bool _prevEventSystemEnabled = true;

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

        if (root.TryGetProperty("actions", out var actionsEl) && actionsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in actionsEl.EnumerateArray())
            {
                int actorId = item.TryGetProperty("actorId", out var aEl) ? aEl.GetInt32() : 0;
                if (actorId == 0)
                    continue;

                var info = new DiscoveredActionInfo
                {
                    Id = item.TryGetProperty("id", out var idEl) ? idEl.GetUInt32() : 0,
                    ActorId = actorId,
                    Type = item.TryGetProperty("type", out var tEl) ? tEl.GetInt32() : 0,
                    Buttons = ReadStringArray(item, "buttons"),
                    FromPotential = item.TryGetProperty("fromPotential", out var fpEl) ? fpEl.GetUInt32() : 0,
                    FromPotentialCombo = item.TryGetProperty("fromPotentialCombo", out var fpcEl) ? fpcEl.GetUInt32() : 0,
                    Replace = ReadUIntArray(item, "replace"),
                    IsUltra = item.TryGetProperty("isUltra", out var uEl) && uEl.GetBoolean(),
                    ShowInBattle = item.TryGetProperty("showInBattle", out var sEl) && sEl.GetBoolean(),
                    SortOrder = item.TryGetProperty("sortOrder", out var soEl) ? soEl.GetInt32() : 0,
                    Labels = ReadStringArray(item, "labels"),
                    Name = item.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? string.Empty) : string.Empty
                };

                if (!_snapshot.DiscoveredActionsByActor.TryGetValue(actorId, out var list))
                {
                    list = new System.Collections.Generic.List<DiscoveredActionInfo>();
                    _snapshot.DiscoveredActionsByActor[actorId] = list;
                }
                list.Add(info);
            }
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

        _snapshot.CatalogLoaded = true;
        CharacterCatalog.MergeDiscoveredCatalog(_snapshot);

        if (Plugin.LogDiscoveredCatalog?.Value == true)
        {
            Plugin.LogInstance?.LogInfo($"Discovered {_snapshot.DiscoveredActionsByActor.Count} character action tables and {_snapshot.DiscoveredInstructions.Count} instruction rules.");
        }
    }

    private void ApplyStatePayload(System.Text.Json.JsonElement root)
    {
        if (root.TryGetProperty("activeActorId", out var actorEl))
        {
            int jsActor = actorEl.GetInt32();
            if (jsActor != 0)
                _snapshot.ActiveActorId = jsActor;
        }

        if (root.TryGetProperty("inBattle", out var inBattleEl))
            _snapshot.InBattle = inBattleEl.GetBoolean();

        if (root.TryGetProperty("inputLocked", out var lockEl))
            _snapshot.InputLocked = lockEl.GetBoolean();

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
            return;

        // 1. Read live C# state directly every frame (zero lag across Tag Team swaps)
        int playerId = 0;
        float facing = 1f;
        bool csharpInBattle = false;
        bool csharpLocked = false;
        int csharpActorId = 0;

        try
        {
            var battle = BattleBase.Cur;
            var player = battle?.PlayerSelf;
            if (battle != null && player != null)
            {
                csharpInBattle = true;
                csharpActorId = player.BaseID;
                facing = player.Dir;
                if (player.Input != null)
                    playerId = player.Input.PlayerId;

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

        // 2. Menu toggle hotkey (default F4)
        var menuKey = Plugin.MenuKey?.Value ?? KeyCode.F4;
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
        _sequencer.Update(playerId, csharpActorId, csharpLocked, facing, specialOverride);

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
                _sequencer.Start(move, key, playerId, facing, specialOverride);
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
            CloseMenu();
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
        _menuOpen = true;
        _menuUi.CancelListening();

        if (_snapshot.InBattle && _snapshot.ActiveActorId != 0)
            _menuUi.SelectedCharacterId = _snapshot.ActiveActorId;

        Plugin.RequestJsSnapshot(false);
        Plugin.SetJsBattlePaused(true);

        if (!_savedCursorState)
        {
            _savedCursorState = true;
            _prevCursorVisible = Cursor.visible;
            _prevCursorLock = Cursor.lockState;
            if (EventSystem.current != null)
                _prevEventSystemEnabled = EventSystem.current.enabled;
        }

        MaintainMenuCursor();
    }

    private void CloseMenu()
    {
        if (!_menuOpen)
            return;

        _menuOpen = false;
        _menuUi.CancelListening();
        Plugin.SetJsBattlePaused(false);

        if (_savedCursorState)
        {
            _savedCursorState = false;
            if (EventSystem.current != null)
                EventSystem.current.enabled = _prevEventSystemEnabled;
            Cursor.visible = _prevCursorVisible;
            Cursor.lockState = _prevCursorLock;
        }
    }

    private static void MaintainMenuCursor()
    {
        if (EventSystem.current != null && EventSystem.current.enabled)
            EventSystem.current.enabled = false;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void HandleMenuInput()
    {
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
                return;
            }
        }
    }

    public void OnDisable()
    {
        CloseMenu();
        _sequencer.Cancel(0);
    }

    private static string[] ReadStringArray(System.Text.Json.JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new System.Collections.Generic.List<string>();
        foreach (var v in el.EnumerateArray())
            list.Add(v.GetString() ?? string.Empty);
        return list.ToArray();
    }

    private static uint[] ReadUIntArray(System.Text.Json.JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
            return Array.Empty<uint>();

        var list = new System.Collections.Generic.List<uint>();
        foreach (var v in el.EnumerateArray())
            list.Add(v.GetUInt32());
        return list.ToArray();
    }
}
