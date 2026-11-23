using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Luka.BBEE.KeybindMod;

public static class CharacterCatalog
{
    public static readonly IReadOnlyList<CharacterMeta> Roster = new List<CharacterMeta>
    {
        new(100101, "ragna", "Ragna"),
        new(100201, "jin", "Jin"),
        new(100301, "noel", "Noel"),
        new(100401, "rachel", "Rachel"),
        new(100501, "taokaka", "Taokaka"),
        new(101101, "hakumen", "Hakumen"),
        new(101401, "hazama", "Hazama"),
        new(101801, "platinum", "Platinum"),
        new(102201, "bullet", "Bullet"),
        new(102501, "kokonoe", "Kokonoe"),
        new(102801, "lambda", "No.11 (Lambda-11)"),
        new(102901, "hibiki", "Hibiki"),
        new(103101, "naoto", "Naoto"),
        new(103401, "es", "Es"),
        new(103501, "mai", "Mai"),
        new(103701, "icey", "Icey"),
        new(103801, "beheaded", "Beheaded"),
    };

    private static readonly Dictionary<int, CharacterMeta> ById = Roster.ToDictionary(c => c.ActorId);
    private static readonly Dictionary<int, List<MoveDefinition>> MovesByActor = new();

    static CharacterCatalog()
    {
        ResetToPresets();
    }

    public static CharacterMeta? FindCharacter(int actorId) => ById.TryGetValue(actorId, out var c) ? c : null;

    public static string GetCharacterName(int actorId) => ById.TryGetValue(actorId, out var c) ? c.DisplayName : $"Fighter {actorId}";

    public static IReadOnlyList<MoveDefinition> GetMoves(int actorId)
    {
        return MovesByActor.TryGetValue(actorId, out var list) ? list : Array.Empty<MoveDefinition>();
    }

    public static void ResetToPresets()
    {
        MovesByActor.Clear();
        foreach (var ch in Roster)
            MovesByActor[ch.ActorId] = new List<MoveDefinition>();

        AddRachelPresets(MovesByActor[100401]);
        AddPlatinumPresets(MovesByActor[101801]);
        AddLambdaPresets(MovesByActor[102801]);
        AddNoelPresets(MovesByActor[100301]);
        AddIceyPresets(MovesByActor[103701]);
        AddTaokakaPresets(MovesByActor[100501]);
        AddHibikiPresets(MovesByActor[102901]);

        foreach (var ch in Roster)
        {
            if (MovesByActor[ch.ActorId].Count == 0)
                AddStandardCombos(MovesByActor[ch.ActorId], ch.ActorId, ch.Key);
        }
    }

    public static void MergeDiscoveredCatalog(RuntimeStateSnapshot snapshot)
    {
        ResetToPresets();

        foreach (var kvp in snapshot.DiscoveredActionsByActor)
        {
            if (!MovesByActor.TryGetValue(kvp.Key, out var list))
            {
                list = new List<MoveDefinition>();
                MovesByActor[kvp.Key] = list;
            }

            var seenSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in list)
                seenSignatures.Add(BuildCanonicalSignature(preset));

            foreach (var act in kvp.Value)
            {
                // Skip pure 1-button moves unless directional+button (game already binds 1-button inputs)
                var steps = ParseButtonSequence(act.Buttons, out var hint, out var isMultiInput);
                if (steps == null || !isMultiInput)
                    continue;

                var sig = string.Join("+", act.Buttons);
                if (seenSignatures.Contains(sig))
                    continue;

                seenSignatures.Add(sig);
                string title = !string.IsNullOrWhiteSpace(act.Name)
                    ? act.Name
                    : BuildFallbackActionName(act, hint);

                list.Add(new MoveDefinition
                {
                    Id = $"action.{kvp.Key}.{act.Id}",
                    ActorId = kvp.Key,
                    Name = title,
                    ComboHint = hint,
                    Description = act.FromPotential > 0
                        ? $"Potential #{act.FromPotential} skill combo"
                        : "Base multi-button move combo",
                    Category = MoveCategory.Discovered,
                    RequiredPotentialId = act.FromPotential,
                    ReplacesActionIds = act.Replace,
                    ActionConfId = act.Id,
                    IsPreset = false,
                    SortOrder = 1000 + act.SortOrder,
                    Steps = steps
                });
            }

            list.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        }
    }

    private static string BuildFallbackActionName(DiscoveredActionInfo act, string hint)
    {
        if (act.Labels != null && act.Labels.Length > 0)
        {
            var clean = string.Join(" ", act.Labels.Where(l => !string.IsNullOrWhiteSpace(l))).Trim();
            if (!string.IsNullOrEmpty(clean) && !clean.Contains("_"))
                return clean;
        }
        return act.IsUltra ? $"Ultra Combo ({hint})" : $"Combo: {hint}";
    }

    private static string BuildCanonicalSignature(MoveDefinition move)
    {
        return move.ComboHint;
    }

    private static void AddRachelPresets(List<MoveDefinition> list)
    {
        list.Add(new MoveDefinition
        {
            Id = "rachel.tiny_lobelia",
            ActorId = 100401,
            Name = "Tiny Lobelia",
            ComboHint = "Up + Attack",
            Description = "W Hold (111ms) -> Attack (59ms) -> 387ms recovery.",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = 10,
            Steps = new()
            {
                InputStep.HoldDir(VirtualDir.Up),
                InputStep.Wait(111),
                InputStep.Press(InputActionKind.Attack),
                InputStep.Wait(59),
                InputStep.Release(InputActionKind.Attack),
                InputStep.Wait(387),
                InputStep.ReleaseDir()
            }
        });

        list.Add(new MoveDefinition
        {
            Id = "rachel.frog_bat",
            ActorId = 100401,
            Name = "Frog-Bat",
            ComboHint = "Up + 2x Dash",
            Description = "W Hold (60ms) -> Double Dash tap (60ms/90ms) -> 120ms.",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = 20,
            Steps = new()
            {
                InputStep.HoldDir(VirtualDir.Up),
                InputStep.Wait(60),
                InputStep.Press(InputActionKind.Dash),
                InputStep.Wait(60),
                InputStep.Release(InputActionKind.Dash),
                InputStep.Wait(90),
                InputStep.Press(InputActionKind.Dash),
                InputStep.Wait(60),
                InputStep.Release(InputActionKind.Dash),
                InputStep.Wait(120),
                InputStep.ReleaseDir()
            }
        });

        list.Add(new MoveDefinition
        {
            Id = "rachel.tempest_dahlia",
            ActorId = 100401,
            Name = "Tempest Dahlia",
            ComboHint = "Up + Skill",
            Description = "W Hold (40ms) -> Skill (50ms) -> 40ms.",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = 30,
            Steps = new()
            {
                InputStep.HoldDir(VirtualDir.Up),
                InputStep.Wait(40),
                InputStep.Press(InputActionKind.Skill),
                InputStep.Wait(50),
                InputStep.Release(InputActionKind.Skill),
                InputStep.Wait(40),
                InputStep.ReleaseDir()
            }
        });

        list.Add(CreateDodgeAttack("rachel.dodge_attack", 100401, 40));

        list.Add(new MoveDefinition
        {
            Id = "rachel.block",
            ActorId = 100401,
            Name = "Rachel Block",
            ComboHint = "Down + Attack",
            Description = "S Hold (111ms) -> Attack (59ms) -> 387ms recovery.",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = 50,
            Steps = new()
            {
                InputStep.HoldDir(VirtualDir.Down),
                InputStep.Wait(111),
                InputStep.Press(InputActionKind.Attack),
                InputStep.Wait(59),
                InputStep.Release(InputActionKind.Attack),
                InputStep.Wait(387),
                InputStep.ReleaseDir()
            }
        });

        list.Add(CreateChantSpell(
            id: "rachel.tornado",
            actorId: 100401,
            name: "Tornado (Spell Chant)",
            hint: "Special -> U, L, R, D, L, R, U",
            desc: "Performs full casting animation with 10ms directional taps.",
            startupMs: 30,
            tapHoldMs: 10,
            tapGapMs: 10,
            postHoldMs: 20,
            sortOrder: 60,
            VirtualDir.Up, VirtualDir.Left, VirtualDir.Right, VirtualDir.Down,
            VirtualDir.Left, VirtualDir.Right, VirtualDir.Up));

        list.Add(CreateChantSpell(
            id: "rachel.invincibility",
            actorId: 100401,
            name: "Invincibility (Spell Chant)",
            hint: "Special -> U, U, D, D, L, R, L, R",
            desc: "Performs full casting animation with 10ms directional taps.",
            startupMs: 30,
            tapHoldMs: 10,
            tapGapMs: 10,
            postHoldMs: 20,
            sortOrder: 70,
            VirtualDir.Up, VirtualDir.Up, VirtualDir.Down, VirtualDir.Down,
            VirtualDir.Left, VirtualDir.Right, VirtualDir.Left, VirtualDir.Right));
    }

    private static void AddPlatinumPresets(List<MoveDefinition> list)
    {
        list.Add(new MoveDefinition
        {
            Id = "platinum.surf_dodge",
            ActorId = 101801,
            Name = "Surf Dodge Attack",
            ComboHint = "Down + Dash",
            Description = "Down (30ms) -> Dash (40ms) -> 30ms.",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = 10,
            Steps = new()
            {
                InputStep.HoldDir(VirtualDir.Down),
                InputStep.Wait(30),
                InputStep.Press(InputActionKind.Dash),
                InputStep.Wait(40),
                InputStep.Release(InputActionKind.Dash),
                InputStep.Wait(30),
                InputStep.ReleaseDir()
            }
        });

        list.Add(CreateDodgeAttack("platinum.normal_dodge", 101801, 20, "Normal Dodge Attack"));
        list.Add(CreateHeavyDodgeAttack("platinum.heavy_dodge", 101801, 30, "Heavy Dodge Attack"));

        list.Add(CreateChantSpell(
            id: "platinum.angelic_blast",
            actorId: 101801,
            name: "Angelic Blast (Spell Chant)",
            hint: "Special (280ms) -> (L, R) x4",
            desc: "Performs authentic 280ms chant startup + 55ms directional taps.",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 40,
            VirtualDir.Left, VirtualDir.Right, VirtualDir.Left, VirtualDir.Right,
            VirtualDir.Left, VirtualDir.Right, VirtualDir.Left, VirtualDir.Right));

        list.Add(CreateChantSpell(
            id: "platinum.shining_layered_force",
            actorId: 101801,
            name: "Shining Layered Force (Spell Chant)",
            hint: "Special (280ms) -> (U, L, D, R) x2",
            desc: "Performs authentic 280ms chant startup + 55ms directional taps.",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 50,
            VirtualDir.Up, VirtualDir.Left, VirtualDir.Down, VirtualDir.Right,
            VirtualDir.Up, VirtualDir.Left, VirtualDir.Down, VirtualDir.Right));

        list.Add(CreateChantSpell(
            id: "platinum.weapon_yellow",
            actorId: 101801,
            name: "Magical Weapon: Yellow (Chant)",
            hint: "Special (280ms) -> Right x2",
            desc: "280ms chant startup + Right, Right (55ms taps).",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 60,
            VirtualDir.Right, VirtualDir.Right));

        list.Add(CreateChantSpell(
            id: "platinum.weapon_blue",
            actorId: 101801,
            name: "Magical Weapon: Blue (Chant)",
            hint: "Special (280ms) -> Down x2",
            desc: "280ms chant startup + Down, Down (55ms taps).",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 70,
            VirtualDir.Down, VirtualDir.Down));

        list.Add(CreateChantSpell(
            id: "platinum.weapon_pink",
            actorId: 101801,
            name: "Magical Weapon: Pink (Chant)",
            hint: "Special (280ms) -> Up x2",
            desc: "280ms chant startup + Up, Up (55ms taps).",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 80,
            VirtualDir.Up, VirtualDir.Up));
    }

    private static void AddLambdaPresets(List<MoveDefinition> list)
    {
        list.Add(CreateDodgeAttack("no11.dodge_attack", 102801, 10, "Dodge Attack"));

        list.Add(new MoveDefinition
        {
            Id = "no11.crescent_loop",
            ActorId = 102801,
            Name = "Crescent Strike Loop (Hold)",
            ComboHint = "Hold -> Jump (25ms) -> 25ms -> Attack (30ms) -> 145ms",
            Description = "Continuous jump-cancel Crescent Strike loop (~225ms/cycle) while held.",
            Category = MoveCategory.HoldLoop,
            HoldToRepeat = true,
            IsPreset = true,
            SortOrder = 20,
            Steps = new()
            {
                InputStep.Press(InputActionKind.Jump),
                InputStep.Wait(25),
                InputStep.Release(InputActionKind.Jump),
                InputStep.Wait(25),
                InputStep.Press(InputActionKind.Attack),
                InputStep.Wait(30),
                InputStep.Release(InputActionKind.Attack),
                InputStep.Wait(145)
            }
        });

        list.Add(new MoveDefinition
        {
            Id = "no11.crescent_single",
            ActorId = 102801,
            Name = "Crescent Strike (Single Tap)",
            ComboHint = "Jump (25ms) -> 25ms -> Attack (30ms)",
            Description = "Performs one low jump-cancel Crescent Strike.",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = 30,
            Steps = new()
            {
                InputStep.Press(InputActionKind.Jump),
                InputStep.Wait(25),
                InputStep.Release(InputActionKind.Jump),
                InputStep.Wait(25),
                InputStep.Press(InputActionKind.Attack),
                InputStep.Wait(30),
                InputStep.Release(InputActionKind.Attack)
            }
        });
    }

    private static void AddNoelPresets(List<MoveDefinition> list)
    {
        list.Add(CreateDodgeAttack("noel.dodge_attack", 100301, 10, "Dash / Dodge Attack"));

        list.Add(new MoveDefinition
        {
            Id = "noel.mark_detonation",
            ActorId = 100301,
            Name = "Mark Detonation",
            ComboHint = "Up + Special",
            Description = "Up (20ms) -> Special (40ms) -> 20ms.",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = 20,
            Steps = new()
            {
                InputStep.HoldDir(VirtualDir.Up),
                InputStep.Wait(20),
                InputStep.Press(InputActionKind.Special),
                InputStep.Wait(40),
                InputStep.Release(InputActionKind.Special),
                InputStep.Wait(20),
                InputStep.ReleaseDir()
            }
        });
    }

    private static void AddIceyPresets(List<MoveDefinition> list)
    {
        list.Add(new MoveDefinition
        {
            Id = "icey.sheathe_dodge",
            ActorId = 103701,
            Name = "Sheathe Dodge Attack",
            ComboHint = "Special + Down -> Skill",
            Description = "Special + Down (15ms) -> Skill (15ms) -> 15ms.",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = 10,
            Steps = new()
            {
                InputStep.Press(InputActionKind.Special),
                InputStep.HoldDir(VirtualDir.Down),
                InputStep.Wait(15),
                InputStep.Press(InputActionKind.Skill),
                InputStep.Wait(15),
                InputStep.Release(InputActionKind.Skill),
                InputStep.Wait(15),
                InputStep.ReleaseDir(),
                InputStep.Release(InputActionKind.Special)
            }
        });

        list.Add(new MoveDefinition
        {
            Id = "icey.sheathing_stance",
            ActorId = 103701,
            Name = "Sheathing Stance",
            ComboHint = "Special + Down",
            Description = "Special + Down (30ms).",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = 20,
            Steps = new()
            {
                InputStep.Press(InputActionKind.Special),
                InputStep.HoldDir(VirtualDir.Down),
                InputStep.Wait(30),
                InputStep.ReleaseDir(),
                InputStep.Release(InputActionKind.Special)
            }
        });

        list.Add(new MoveDefinition
        {
            Id = "icey.charging_stance",
            ActorId = 103701,
            Name = "Charging Stance",
            ComboHint = "Special + Up",
            Description = "Special + Up (30ms).",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = 30,
            Steps = new()
            {
                InputStep.Press(InputActionKind.Special),
                InputStep.HoldDir(VirtualDir.Up),
                InputStep.Wait(30),
                InputStep.ReleaseDir(),
                InputStep.Release(InputActionKind.Special)
            }
        });

        list.Add(CreateDodgeAttack("icey.dodge_attack", 103701, 40));
    }

    private static void AddTaokakaPresets(List<MoveDefinition> list)
    {
        list.Add(CreateDodgeAttack("taokaka.dodge_attack", 100501, 10, "Dash / Dodge Attack"));
        AddStandardCombos(list, 100501, "taokaka", includeDodgeAttack: false);
    }

    private static void AddHibikiPresets(List<MoveDefinition> list)
    {
        list.Add(CreateDodgeAttack("hibiki.dodge_attack", 102901, 10, "Dash / Dodge Attack"));
        AddStandardCombos(list, 102901, "hibiki", includeDodgeAttack: false);
    }

    private static void AddStandardCombos(List<MoveDefinition> list, int actorId, string prefix, bool includeDodgeAttack = true)
    {
        if (includeDodgeAttack)
            list.Add(CreateDodgeAttack($"{prefix}.dodge_attack", actorId, 10, "Dash / Dodge Attack"));

        list.Add(CreateHeavyDodgeAttack($"{prefix}.heavy_dodge", actorId, 20, "Heavy Dodge Attack"));
        list.Add(CreateDirAction($"{prefix}.up_attack", actorId, "Up + Attack", VirtualDir.Up, InputActionKind.Attack, 30));
        list.Add(CreateDirAction($"{prefix}.down_attack", actorId, "Down + Attack", VirtualDir.Down, InputActionKind.Attack, 40));
        list.Add(CreateDirAction($"{prefix}.up_skill", actorId, "Up + Skill", VirtualDir.Up, InputActionKind.Skill, 50));
        list.Add(CreateDirAction($"{prefix}.down_skill", actorId, "Down + Skill", VirtualDir.Down, InputActionKind.Skill, 60));
        list.Add(CreateDirAction($"{prefix}.up_special", actorId, "Up + Special", VirtualDir.Up, InputActionKind.Special, 70));
        list.Add(CreateDirAction($"{prefix}.down_special", actorId, "Down + Special", VirtualDir.Down, InputActionKind.Special, 80));
    }

    private static MoveDefinition CreateDodgeAttack(string id, int actorId, int sortOrder, string name = "Dodge Attack")
    {
        return new MoveDefinition
        {
            Id = id,
            ActorId = actorId,
            Name = name,
            ComboHint = "Dash + Attack",
            Description = "Dash (40ms) -> Attack (40ms) -> 40ms.",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = sortOrder,
            Steps = new()
            {
                InputStep.Press(InputActionKind.Dash),
                InputStep.Wait(40),
                InputStep.Press(InputActionKind.Attack),
                InputStep.Wait(40),
                InputStep.Release(InputActionKind.Attack),
                InputStep.Wait(40),
                InputStep.Release(InputActionKind.Dash)
            }
        };
    }

    private static MoveDefinition CreateHeavyDodgeAttack(string id, int actorId, int sortOrder, string name = "Heavy Dodge Attack")
    {
        return new MoveDefinition
        {
            Id = id,
            ActorId = actorId,
            Name = name,
            ComboHint = "Dash + Skill",
            Description = "Dash (40ms) -> Skill (40ms) -> 40ms.",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = sortOrder,
            Steps = new()
            {
                InputStep.Press(InputActionKind.Dash),
                InputStep.Wait(40),
                InputStep.Press(InputActionKind.Skill),
                InputStep.Wait(40),
                InputStep.Release(InputActionKind.Skill),
                InputStep.Wait(40),
                InputStep.Release(InputActionKind.Dash)
            }
        };
    }

    private static MoveDefinition CreateDirAction(string id, int actorId, string name, VirtualDir dir, InputActionKind act, int sortOrder)
    {
        return new MoveDefinition
        {
            Id = id,
            ActorId = actorId,
            Name = name,
            ComboHint = $"{dir} + {act}",
            Description = $"Holds {dir} (40ms) and presses {act} (50ms).",
            Category = MoveCategory.CombatCombo,
            IsPreset = true,
            SortOrder = sortOrder,
            Steps = new()
            {
                InputStep.HoldDir(dir),
                InputStep.Wait(40),
                InputStep.Press(act),
                InputStep.Wait(50),
                InputStep.Release(act),
                InputStep.Wait(40),
                InputStep.ReleaseDir()
            }
        };
    }

    private static MoveDefinition CreateChantSpell(
        string id,
        int actorId,
        string name,
        string hint,
        string desc,
        int startupMs,
        int tapHoldMs,
        int tapGapMs,
        int postHoldMs,
        int sortOrder,
        params VirtualDir[] sequence)
    {
        var steps = new List<InputStep>
        {
            InputStep.Press(InputActionKind.Special),
            InputStep.Wait(startupMs)
        };

        for (int i = 0; i < sequence.Length; i++)
        {
            steps.Add(InputStep.HoldDir(sequence[i]));
            steps.Add(InputStep.Wait(tapHoldMs));
            steps.Add(InputStep.ReleaseDir());
            if (i < sequence.Length - 1)
                steps.Add(InputStep.Wait(tapGapMs));
        }

        if (postHoldMs > 0)
            steps.Add(InputStep.Wait(postHoldMs));
        steps.Add(InputStep.Release(InputActionKind.Special));

        return new MoveDefinition
        {
            Id = id,
            ActorId = actorId,
            Name = name,
            ComboHint = hint,
            Description = desc,
            Category = MoveCategory.SpellChant,
            IsPreset = true,
            SortOrder = sortOrder,
            Steps = steps
        };
    }

    private static List<InputStep>? ParseButtonSequence(string[] buttons, out string hint, out bool isMultiInput)
    {
        hint = string.Empty;
        isMultiInput = false;
        if (buttons == null || buttons.Length == 0)
            return null;

        // Parse ActorActionConf button tokens like "Up+Attack", "Dash+Attack", "Down+Skill", "Jump+Attack", etc.
        var steps = new List<InputStep>();
        var hintParts = new List<string>();
        int distinctActions = 0;
        bool hasDir = false;

        foreach (var raw in buttons)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var parts = raw.Split(new[] { '+', ' ', '&' }, StringSplitOptions.RemoveEmptyEntries);
            VirtualDir? stepDir = null;
            var stepActs = new List<InputActionKind>();

            foreach (var p in parts)
            {
                var token = p.Trim();
                if (TryParseDir(token, out var d))
                {
                    stepDir = d;
                    hasDir = true;
                }
                else if (TryParseAction(token, out var a))
                {
                    stepActs.Add(a);
                    distinctActions++;
                }
                else
                {
                    // Unknown token (e.g. hold icon / passive marker) -> skip auto-synthesis safely
                    return null;
                }
            }

            if (stepDir == null && stepActs.Count == 0)
                continue;

            var partDesc = new StringBuilder();
            if (stepDir.HasValue)
            {
                partDesc.Append(stepDir.Value);
                steps.Add(InputStep.HoldDir(stepDir.Value));
                steps.Add(InputStep.Wait(35));
            }

            for (int i = 0; i < stepActs.Count; i++)
            {
                if (partDesc.Length > 0)
                    partDesc.Append(" + ");
                partDesc.Append(stepActs[i]);
                steps.Add(InputStep.Press(stepActs[i]));
                if (i < stepActs.Count - 1)
                    steps.Add(InputStep.Wait(35));
            }

            steps.Add(InputStep.Wait(45));

            for (int i = stepActs.Count - 1; i >= 0; i--)
                steps.Add(InputStep.Release(stepActs[i]));

            if (stepDir.HasValue)
            {
                steps.Add(InputStep.Wait(20));
                steps.Add(InputStep.ReleaseDir());
            }

            if (partDesc.Length > 0)
                hintParts.Add(partDesc.ToString());
        }

        if (steps.Count == 0)
            return null;

        isMultiInput = hasDir || distinctActions >= 2 || hintParts.Count >= 2;
        hint = string.Join(" -> ", hintParts);
        return steps;
    }

    private static bool TryParseDir(string token, out VirtualDir dir)
    {
        switch (token.ToLowerInvariant())
        {
            case "up":
            case "w":
                dir = VirtualDir.Up;
                return true;
            case "down":
            case "s":
                dir = VirtualDir.Down;
                return true;
            case "left":
            case "a":
                dir = VirtualDir.Left;
                return true;
            case "right":
            case "d":
                dir = VirtualDir.Right;
                return true;
            case "front":
            case "forward":
                dir = VirtualDir.Forward;
                return true;
            case "back":
            case "backward":
                dir = VirtualDir.Back;
                return true;
            default:
                dir = VirtualDir.Neutral;
                return false;
        }
    }

    private static bool TryParseAction(string token, out InputActionKind action)
    {
        switch (token.ToLowerInvariant())
        {
            case "attack":
            case "normalattack":
            case "atk":
            case "lbutton":
                action = InputActionKind.Attack;
                return true;
            case "skill":
            case "sp":
            case "rbutton":
                action = InputActionKind.Skill;
                return true;
            case "dash":
            case "dodge":
            case "shift":
                action = InputActionKind.Dash;
                return true;
            case "jump":
            case "space":
                action = InputActionKind.Jump;
                return true;
            case "special":
            case "specialaction1":
            case "specialaction":
            case "legacy":
            case "dynamic":
                action = InputActionKind.Special;
                return true;
            case "ultra":
                action = InputActionKind.Ultra;
                return true;
            case "summon":
                action = InputActionKind.Summon;
                return true;
            case "customuse1":
                action = InputActionKind.CustomUse1;
                return true;
            case "customuse2":
                action = InputActionKind.CustomUse2;
                return true;
            default:
                action = default;
                return false;
        }
    }
}
