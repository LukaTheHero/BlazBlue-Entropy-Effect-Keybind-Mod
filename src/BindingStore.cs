using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BepInEx.Logging;
using UnityEngine;

namespace Luka.BBEE.KeybindMod;

public sealed class BindingStore
{
    private readonly string _filePath;
    private readonly string _repeatFilePath;
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, KeyCode> _bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _holdRepeatOverrides = new(StringComparer.OrdinalIgnoreCase);

    public BindingStore(string configPath, ManualLogSource log)
    {
        _filePath = Path.Combine(configPath, "luka.bbee.keybindmod.bindings.json");
        _repeatFilePath = Path.Combine(configPath, "luka.bbee.keybindmod.repeat.json");
        _log = log;
        Load();
        LoadRepeats();
    }

    public KeyCode GetBinding(string moveId)
    {
        return _bindings.TryGetValue(moveId, out var key) ? key : KeyCode.None;
    }

    public bool GetHoldRepeat(MoveDefinition move)
    {
        if (move.Category == MoveCategory.SpellChant)
            return false;
        if (_holdRepeatOverrides.TryGetValue(move.Id, out var enabled))
            return enabled;
        return move.HoldToRepeat;
    }

    public void SetHoldRepeat(string moveId, bool enabled)
    {
        _holdRepeatOverrides[moveId] = enabled;
        SaveRepeats();
    }

    public void SetBinding(string moveId, KeyCode key)
    {
        if (key == KeyCode.None || key == KeyCode.Escape)
            _bindings.Remove(moveId);
        else
            _bindings[moveId] = key;

        Save();
    }

    public void ClearCharacterBindings(IEnumerable<MoveDefinition> characterMoves)
    {
        bool changed = false;
        foreach (var move in characterMoves)
        {
            if (_bindings.Remove(move.Id))
                changed = true;
        }
        if (changed)
            Save();
    }

    public List<MoveDefinition> GetDuplicateMoves(IEnumerable<MoveDefinition> characterMoves, string excludeMoveId, KeyCode key)
    {
        var list = new List<MoveDefinition>();
        if (key == KeyCode.None)
            return list;

        foreach (var move in characterMoves)
        {
            if (move.Id != excludeMoveId && GetBinding(move.Id) == key)
                list.Add(move);
        }
        return list;
    }

    private void Load()
    {
        _bindings.Clear();
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (raw == null)
                return;

            foreach (var kvp in raw)
            {
                if (Enum.TryParse<KeyCode>(kvp.Value, true, out var kc) && kc != KeyCode.None && kc != KeyCode.Escape)
                    _bindings[kvp.Key] = kc;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Failed to load keybinds from {_filePath}: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var serialized = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _bindings)
            {
                if (kvp.Value != KeyCode.None)
                    serialized[kvp.Key] = kvp.Value.ToString();
            }

            var json = JsonSerializer.Serialize(serialized, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Failed to save keybinds to {_filePath}: {ex.Message}");
        }
    }

    private void LoadRepeats()
    {
        _holdRepeatOverrides.Clear();
        if (!File.Exists(_repeatFilePath))
            return;

        try
        {
            var json = File.ReadAllText(_repeatFilePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            if (raw == null)
                return;

            foreach (var kvp in raw)
                _holdRepeatOverrides[kvp.Key] = kvp.Value;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Failed to load hold-repeat toggles from {_repeatFilePath}: {ex.Message}");
        }
    }

    private void SaveRepeats()
    {
        try
        {
            var dir = Path.GetDirectoryName(_repeatFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var serialized = new SortedDictionary<string, bool>(_holdRepeatOverrides, StringComparer.OrdinalIgnoreCase);
            var json = JsonSerializer.Serialize(serialized, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_repeatFilePath, json);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Failed to save hold-repeat toggles to {_repeatFilePath}: {ex.Message}");
        }
    }
}
