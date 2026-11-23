using System;
using System.Collections.Generic;
using UnityEngine;

namespace Luka.BBEE.KeybindMod;

public enum InputActionKind
{
    Attack,
    Skill,
    Dash,
    Jump,
    Special,
    Special2,
    Summon,
    Dynamic,
    Ultra,
    CustomUse1,
    CustomUse2
}

public enum VirtualDir
{
    Neutral,
    Up,
    Down,
    Left,
    Right,
    Forward,
    Back
}

public enum InputStepType
{
    HoldDir,
    ReleaseDir,
    Press,
    Release,
    WaitMs
}

public readonly struct InputStep
{
    public readonly InputStepType Type;
    public readonly VirtualDir Dir;
    public readonly InputActionKind Action;
    public readonly int DurationMs;

    private InputStep(InputStepType type, VirtualDir dir, InputActionKind action, int durationMs)
    {
        Type = type;
        Dir = dir;
        Action = action;
        DurationMs = durationMs;
    }

    public static InputStep HoldDir(VirtualDir dir) => new(InputStepType.HoldDir, dir, default, 0);
    public static InputStep ReleaseDir() => new(InputStepType.ReleaseDir, VirtualDir.Neutral, default, 0);
    public static InputStep Press(InputActionKind action) => new(InputStepType.Press, VirtualDir.Neutral, action, 0);
    public static InputStep Release(InputActionKind action) => new(InputStepType.Release, VirtualDir.Neutral, action, 0);
    public static InputStep Wait(int ms) => new(InputStepType.WaitMs, VirtualDir.Neutral, default, ms);
}

public enum MoveCategory
{
    CombatCombo,
    SpellChant,
    HoldLoop,
    Discovered
}

public sealed class CharacterMeta
{
    public int ActorId { get; }
    public string Key { get; }
    public string DisplayName { get; }

    public CharacterMeta(int actorId, string key, string displayName)
    {
        ActorId = actorId;
        Key = key;
        DisplayName = displayName;
    }
}

public sealed class MoveDefinition
{
    public string Id { get; init; } = string.Empty;
    public int ActorId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string ComboHint { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public MoveCategory Category { get; init; }
    public uint RequiredPotentialId { get; set; }
    public uint[] RequiredPotentialIds { get; set; } = Array.Empty<uint>();
    public uint[][] ComboPotentialSets { get; set; } = Array.Empty<uint[]>();
    public bool IsBaseMove { get; set; } = true;
    public bool RequiresAirborne { get; set; }
    public int RepeatCooldownMs { get; set; } = 55;
    public uint[] ReplacesActionIds { get; set; } = Array.Empty<uint>();
    public uint ActionConfId { get; set; }
    public bool HoldToRepeat { get; init; }
    public bool IsPreset { get; init; }
    public int SortOrder { get; init; }
    public List<InputStep> Steps { get; init; } = new();

    public bool IsUnlockedInRun(RuntimeStateSnapshot snapshot)
    {
        if (snapshot.InBattle && snapshot.ActiveActorId == ActorId)
        {
            if (ActionConfId != 0 && snapshot.FesActorId == ActorId && snapshot.ObtainedActionIds.Count > 0)
            {
                if (!snapshot.ObtainedActionIds.Contains(ActionConfId))
                    return false;

                if (snapshot.DiscoveredActionsByActor.TryGetValue(ActorId, out var actions))
                {
                    foreach (var act in actions)
                    {
                        if (Array.IndexOf(act.Replace, ActionConfId) >= 0 && snapshot.ObtainedActionIds.Contains(act.Id))
                            return false;
                    }
                }

                return true;
            }

            if (IsBaseMove && RequiredPotentialId == 0 && RequiredPotentialIds.Length == 0 && ComboPotentialSets.Length == 0)
                return true;

            if (RequiredPotentialId != 0 && snapshot.ActivePotentials.Contains(RequiredPotentialId))
                return true;

            for (int i = 0; i < RequiredPotentialIds.Length; i++)
            {
                if (snapshot.ActivePotentials.Contains(RequiredPotentialIds[i]))
                    return true;
            }

            for (int i = 0; i < ComboPotentialSets.Length; i++)
            {
                var set = ComboPotentialSets[i];
                if (set.Length == 0)
                    continue;

                bool allOwned = true;
                for (int j = 0; j < set.Length; j++)
                {
                    if (!snapshot.ActivePotentials.Contains(set[j]))
                    {
                        allOwned = false;
                        break;
                    }
                }
                if (allOwned)
                    return true;
            }

            return false;
        }

        return IsBaseMove && RequiredPotentialId == 0 && RequiredPotentialIds.Length == 0 && ComboPotentialSets.Length == 0;
    }
}

public sealed class DiscoveredActionInfo
{
    public uint Id { get; init; }
    public int ActorId { get; init; }
    public int Type { get; init; }
    public string InputRaw { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsBase { get; init; }
    public uint[] PotentialIds { get; init; } = Array.Empty<uint>();
    public uint[][] ComboPotentialSets { get; init; } = Array.Empty<uint[]>();
    public uint[] Replace { get; init; } = Array.Empty<uint>();
    public uint[] Inherit { get; init; } = Array.Empty<uint>();
    public bool IsUltra { get; init; }
    public int SortOrder { get; init; }
    public string[] Labels { get; init; } = Array.Empty<string>();
    public string Name { get; init; } = string.Empty;
}

public sealed class DiscoveredInstructionInfo
{
    public int Id { get; init; }
    public string Instruction { get; init; } = string.Empty;
    public string SuccessAction { get; init; } = string.Empty;
    public float ActionCd { get; init; }
    public int TriggerId { get; init; }
}

public sealed class RuntimeStateSnapshot
{
    public int ActiveActorId { get; set; }
    public int FesActorId { get; set; }
    public bool InBattle { get; set; }
    public bool InputLocked { get; set; }
    public HashSet<uint> ActivePotentials { get; } = new();
    public HashSet<uint> ObtainedActionIds { get; } = new();
    public HashSet<int> UnlockedFighters { get; } = new();
    public bool CatalogLoaded { get; set; }
    public Dictionary<int, List<DiscoveredActionInfo>> DiscoveredActionsByActor { get; } = new();
    public List<DiscoveredInstructionInfo> DiscoveredInstructions { get; } = new();
}
