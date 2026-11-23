using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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

    private static readonly HashSet<int> DedicatedPresetActors = new()
    {
        100401, // Rachel
        101801, // Platinum
        102801, // No.11
        100301, // Noel
        103701, // Icey
    };

    private static readonly Dictionary<int, CharacterMeta> ById = Roster.ToDictionary(c => c.ActorId);
    private static readonly Dictionary<int, List<MoveDefinition>> MovesByActor = new();

    private static readonly Regex ActionTagRegex = new(@"<action=([^>]+)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BranchSplitRegex = new(@"\s+\bor\b\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static CharacterCatalog()
    {
        ResetToPresets(includeFallbackStandardCombos: true);
    }

    public static CharacterMeta? FindCharacter(int actorId) => ById.TryGetValue(actorId, out var c) ? c : null;

    public static string GetCharacterName(int actorId) => ById.TryGetValue(actorId, out var c) ? c.DisplayName : $"Fighter {actorId}";

    public static IReadOnlyList<MoveDefinition> GetMoves(int actorId)
    {
        return MovesByActor.TryGetValue(actorId, out var list) ? list : Array.Empty<MoveDefinition>();
    }

    public static void ResetToPresets(bool includeFallbackStandardCombos = true)
    {
        MovesByActor.Clear();
        foreach (var ch in Roster)
            MovesByActor[ch.ActorId] = new List<MoveDefinition>();

        AddRachelPresets(MovesByActor[100401]);
        AddPlatinumPresets(MovesByActor[101801]);
        AddLambdaPresets(MovesByActor[102801]);
        AddNoelPresets(MovesByActor[100301]);
        AddIceyPresets(MovesByActor[103701]);

        if (includeFallbackStandardCombos)
        {
            foreach (var ch in Roster)
            {
                if (!DedicatedPresetActors.Contains(ch.ActorId))
                    AddStandardCombos(MovesByActor[ch.ActorId], ch.ActorId, ch.Key);
            }
        }
    }

    public static void MergeDiscoveredCatalog(RuntimeStateSnapshot snapshot)
    {
        ResetToPresets(includeFallbackStandardCombos: false);

        foreach (var ch in Roster)
        {
            if (!snapshot.DiscoveredActionsByActor.TryGetValue(ch.ActorId, out var rawActions) || rawActions.Count == 0)
            {
                if (!DedicatedPresetActors.Contains(ch.ActorId))
                    AddStandardCombos(MovesByActor[ch.ActorId], ch.ActorId, ch.Key);
                continue;
            }

            var list = MovesByActor[ch.ActorId];

            // Track presets by canonical key so real ActorActionConf entries enrich them without duplicating
            var unmatchedPresets = new List<(MoveDefinition Move, string Canonical)>();
            foreach (var p in list)
            {
                if (p.Category == MoveCategory.SpellChant)
                    continue;
                unmatchedPresets.Add((p, BuildPresetCanonicalKey(p)));
            }

            var seenDiscovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool assignedLegacyDodge = list.Any(m => m.Id.EndsWith(".dodge_attack", StringComparison.OrdinalIgnoreCase));

            foreach (var act in rawActions)
            {
                if (string.IsNullOrWhiteSpace(act.InputRaw))
                    continue;

                var steps = ParseLocalizedInput(act.InputRaw, out var hint, out var isEligible, out var requiresAirborne, out var canonicalKey);
                if (steps == null || !isEligible)
                    continue;

                // Attach first matching discovered row onto matching preset if any
                bool attachedToPreset = false;
                for (int i = 0; i < unmatchedPresets.Count; i++)
                {
                    var (preset, presetCanon) = unmatchedPresets[i];
                    if (preset.ActionConfId == 0 && CanonicalKeysMatch(presetCanon, canonicalKey))
                    {
                        preset.ActionConfId = act.Id;
                        preset.IsBaseMove = act.IsBase;
                        preset.RequiredPotentialIds = act.PotentialIds;
                        preset.RequiredPotentialId = act.PotentialIds.Length > 0 ? act.PotentialIds[0] : 0u;
                        preset.ComboPotentialSets = act.ComboPotentialSets;
                        preset.ReplacesActionIds = act.Replace;
                        if (!string.IsNullOrWhiteSpace(act.Description))
                            preset.Description = act.Description;
                        attachedToPreset = true;
                        break;
                    }
                }

                if (attachedToPreset)
                    continue;

                string title = !string.IsNullOrWhiteSpace(act.Name)
                    ? act.Name
                    : BuildFallbackActionName(act, hint);

                string dedupeKey = $"{canonicalKey}||{title.Trim()}";
                if (!seenDiscovered.Add(dedupeKey))
                    continue;

                string moveId = $"action.{ch.ActorId}.{act.Id}";
                if (!assignedLegacyDodge
                    && act.IsBase
                    && (string.Equals(canonicalKey, "Dash->Attack", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(canonicalKey, "Attack+Dash", StringComparison.OrdinalIgnoreCase)))
                {
                    moveId = $"{ch.Key}.dodge_attack";
                    assignedLegacyDodge = true;
                }

                string desc = !string.IsNullOrWhiteSpace(act.Description)
                    ? act.Description
                    : (act.PotentialIds.Length > 0
                        ? $"Unlocked by Potential #{act.PotentialIds[0]}"
                        : (act.IsBase ? "Base character move" : "Combo Potential skill"));

                list.Add(new MoveDefinition
                {
                    Id = moveId,
                    ActorId = ch.ActorId,
                    Name = title,
                    ComboHint = hint,
                    Description = desc,
                    Category = MoveCategory.Discovered,
                    IsBaseMove = act.IsBase,
                    RequiredPotentialId = act.PotentialIds.Length > 0 ? act.PotentialIds[0] : 0u,
                    RequiredPotentialIds = act.PotentialIds,
                    ComboPotentialSets = act.ComboPotentialSets,
                    RequiresAirborne = requiresAirborne,
                    ReplacesActionIds = act.Replace,
                    ActionConfId = act.Id,
                    IsPreset = false,
                    SortOrder = (act.IsBase ? 200 : 1000) + act.SortOrder,
                    Steps = steps
                });
            }

            list.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        }
    }

    private static string BuildFallbackActionName(DiscoveredActionInfo act, string hint)
    {
        if (!string.IsNullOrWhiteSpace(act.Description))
        {
            var clause = act.Description.Split(new[] { '.', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(clause))
            {
                if (clause.Length > 34)
                    clause = clause.Substring(0, 31).TrimEnd() + "...";
                return clause;
            }
        }

        if (act.Labels != null && act.Labels.Length > 0)
        {
            var clean = string.Join(" ", act.Labels.Where(l => !string.IsNullOrWhiteSpace(l))).Trim();
            if (!string.IsNullOrEmpty(clean) && !clean.Contains("_"))
                return clean;
        }

        return act.IsUltra ? $"Ultra: {hint}" : hint;
    }

    private static string NormalizeCompatKey(string k)
    {
        if (string.Equals(k, "Attack+Dash", StringComparison.OrdinalIgnoreCase))
            return "Dash->Attack";
        if (string.Equals(k, "Dash+Skill", StringComparison.OrdinalIgnoreCase))
            return "Dash->Skill";
        return k;
    }

    private static bool CanonicalKeysMatch(string presetCanon, string discoveredCanon) =>
        string.Equals(NormalizeCompatKey(presetCanon), NormalizeCompatKey(discoveredCanon), StringComparison.OrdinalIgnoreCase);

    private static string BuildPresetCanonicalKey(MoveDefinition move)
    {
        var partTokens = new List<string>();
        VirtualDir? heldDir = null;
        var currentActs = new List<string>();
        bool hasDashAttack = false;

        foreach (var step in move.Steps)
        {
            if (step.Type == InputStepType.HoldDir)
            {
                if (step.Dir != VirtualDir.Neutral)
                    heldDir = step.Dir;
            }
            else if (step.Type == InputStepType.ReleaseDir)
            {
                if (currentActs.Count > 0 || heldDir.HasValue)
                {
                    FlushPart(partTokens, heldDir, currentActs);
                    heldDir = null;
                }
            }
            else if (step.Type == InputStepType.Press)
            {
                string tok = step.Action.ToString();
                if (!currentActs.Contains(tok))
                    currentActs.Add(tok);
                if (step.Action == InputActionKind.Dash)
                    hasDashAttack = true;
            }
            else if (step.Type == InputStepType.Release && step.Action == InputActionKind.Dash && hasDashAttack && currentActs.Count > 0)
            {
                FlushPart(partTokens, heldDir, currentActs);
                heldDir = null;
            }
        }

        FlushPart(partTokens, heldDir, currentActs);

        // Normalize Rachel Frog-Bat vs Up+Dash+Dash etc.
        var body = string.Join("->", partTokens);
        return move.RequiresAirborne ? $"air:{body}" : body;
    }

    private static void FlushPart(List<string> partTokens, VirtualDir? dir, List<string> acts)
    {
        if (!dir.HasValue && acts.Count == 0)
            return;

        var sorted = new List<string>(acts);
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        if (dir.HasValue)
            sorted.Insert(0, dir.Value.ToString());

        partTokens.Add(string.Join("+", sorted));
        acts.Clear();
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
            IsBaseMove = true,
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
            IsBaseMove = true,
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
            IsBaseMove = true,
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
            IsBaseMove = true,
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

        uint[] rachelChantPot = { 6442 };

        list.Add(CreateChantSpell(
            id: "rachel.tornado",
            actorId: 100401,
            name: "Tornado (Spell Chant)",
            hint: "SP -> U, L, R, D, L, R, U",
            desc: "Performs full casting animation with 10ms directional taps.",
            startupMs: 30,
            tapHoldMs: 10,
            tapGapMs: 10,
            postHoldMs: 20,
            sortOrder: 60,
            requiredPotentials: rachelChantPot,
            VirtualDir.Up, VirtualDir.Left, VirtualDir.Right, VirtualDir.Down,
            VirtualDir.Left, VirtualDir.Right, VirtualDir.Up));

        list.Add(CreateChantSpell(
            id: "rachel.invincibility",
            actorId: 100401,
            name: "Invincibility (Spell Chant)",
            hint: "SP -> U, U, D, D, L, R, L, R",
            desc: "Performs full casting animation with 10ms directional taps.",
            startupMs: 30,
            tapHoldMs: 10,
            tapGapMs: 10,
            postHoldMs: 20,
            sortOrder: 70,
            requiredPotentials: rachelChantPot,
            VirtualDir.Up, VirtualDir.Up, VirtualDir.Down, VirtualDir.Down,
            VirtualDir.Left, VirtualDir.Right, VirtualDir.Left, VirtualDir.Right));

        list.Add(CreateChantSpell(
            id: "rachel.summon_pumpkin",
            actorId: 100401,
            name: "Summon Pumpkin (Spell Chant)",
            hint: "SP -> D, L, D, R, U",
            desc: "Chant spell: Summons giant Pumpkin.",
            startupMs: 30,
            tapHoldMs: 10,
            tapGapMs: 10,
            postHoldMs: 20,
            sortOrder: 75,
            requiredPotentials: rachelChantPot,
            VirtualDir.Down, VirtualDir.Left, VirtualDir.Down, VirtualDir.Right, VirtualDir.Up));

        list.Add(CreateChantSpell(
            id: "rachel.summon_bat",
            actorId: 100401,
            name: "Summon Bat (Spell Chant)",
            hint: "SP -> U, L, U, R, D",
            desc: "Chant spell: Summons George bats.",
            startupMs: 30,
            tapHoldMs: 10,
            tapGapMs: 10,
            postHoldMs: 20,
            sortOrder: 80,
            requiredPotentials: rachelChantPot,
            VirtualDir.Up, VirtualDir.Left, VirtualDir.Up, VirtualDir.Right, VirtualDir.Down));

        list.Add(CreateChantSpell(
            id: "rachel.chant_buff",
            actorId: 100401,
            name: "Wind Chant Buff (Spell Chant)",
            hint: "SP -> L, D, R, U",
            desc: "Chant spell: Grants wind chant buff.",
            startupMs: 30,
            tapHoldMs: 10,
            tapGapMs: 10,
            postHoldMs: 20,
            sortOrder: 85,
            requiredPotentials: rachelChantPot,
            VirtualDir.Left, VirtualDir.Down, VirtualDir.Right, VirtualDir.Up));
    }

    private static void AddPlatinumPresets(List<MoveDefinition> list)
    {
        list.Add(new MoveDefinition
        {
            Id = "platinum.surf_dodge",
            ActorId = 101801,
            Name = "Surf Dodge Attack",
            ComboHint = "Down + Dash",
            Description = "Down (30ms) -> Dash (40ms) -> 30ms. Hold A/D to steer.",
            Category = MoveCategory.CombatCombo,
            IsBaseMove = true,
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

        uint[] platChantPot = { 5222 };

        list.Add(CreateChantSpell(
            id: "platinum.angelic_blast",
            actorId: 101801,
            name: "Angelic Blast (Spell Chant)",
            hint: "SP (280ms) -> (L, R) x4",
            desc: "Performs authentic 280ms chant startup + 55ms directional taps.",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 40,
            requiredPotentials: platChantPot,
            VirtualDir.Left, VirtualDir.Right, VirtualDir.Left, VirtualDir.Right,
            VirtualDir.Left, VirtualDir.Right, VirtualDir.Left, VirtualDir.Right));

        list.Add(CreateChantSpell(
            id: "platinum.shining_layered_force",
            actorId: 101801,
            name: "Shining Layered Force (Spell Chant)",
            hint: "SP (280ms) -> (U, L, D, R) x2",
            desc: "Performs authentic 280ms chant startup + 55ms directional taps.",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 50,
            requiredPotentials: platChantPot,
            VirtualDir.Up, VirtualDir.Left, VirtualDir.Down, VirtualDir.Right,
            VirtualDir.Up, VirtualDir.Left, VirtualDir.Down, VirtualDir.Right));

        list.Add(CreateChantSpell(
            id: "platinum.chant_4dir",
            actorId: 101801,
            name: "Magical 4-Dir Spell (Chant)",
            hint: "SP (280ms) -> U, D, L, R",
            desc: "Chant spell: 4-direction miracle burst.",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 55,
            requiredPotentials: platChantPot,
            VirtualDir.Up, VirtualDir.Down, VirtualDir.Left, VirtualDir.Right));

        list.Add(CreateChantSpell(
            id: "platinum.weapon_yellow",
            actorId: 101801,
            name: "Magical Weapon: Yellow (Chant)",
            hint: "SP (280ms) -> Right x2",
            desc: "280ms chant startup + Right, Right (55ms taps).",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 60,
            requiredPotentials: platChantPot,
            VirtualDir.Right, VirtualDir.Right));

        list.Add(CreateChantSpell(
            id: "platinum.weapon_blue",
            actorId: 101801,
            name: "Magical Weapon: Blue (Chant)",
            hint: "SP (280ms) -> Down x2",
            desc: "280ms chant startup + Down, Down (55ms taps).",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 70,
            requiredPotentials: platChantPot,
            VirtualDir.Down, VirtualDir.Down));

        list.Add(CreateChantSpell(
            id: "platinum.weapon_pink",
            actorId: 101801,
            name: "Magical Weapon: Pink (Chant)",
            hint: "SP (280ms) -> Up x2",
            desc: "280ms chant startup + Up, Up (55ms taps).",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 80,
            requiredPotentials: platChantPot,
            VirtualDir.Up, VirtualDir.Up));

        list.Add(CreateChantSpell(
            id: "platinum.ex_weapon_yellow",
            actorId: 101801,
            name: "EX Weapon: Left x4 (Chant)",
            hint: "SP (280ms) -> Left x4",
            desc: "280ms chant startup + Left x4 EX weapon switch.",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 85,
            requiredPotentials: platChantPot,
            VirtualDir.Left, VirtualDir.Left, VirtualDir.Left, VirtualDir.Left));

        list.Add(CreateChantSpell(
            id: "platinum.ex_weapon_blue",
            actorId: 101801,
            name: "EX Weapon: Down x4 (Chant)",
            hint: "SP (280ms) -> Down x4",
            desc: "280ms chant startup + Down x4 EX weapon switch.",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 88,
            requiredPotentials: platChantPot,
            VirtualDir.Down, VirtualDir.Down, VirtualDir.Down, VirtualDir.Down));

        list.Add(CreateChantSpell(
            id: "platinum.ex_weapon_pink",
            actorId: 101801,
            name: "EX Weapon: Up x4 (Chant)",
            hint: "SP (280ms) -> Up x4",
            desc: "280ms chant startup + Up x4 EX weapon switch.",
            startupMs: 280,
            tapHoldMs: 55,
            tapGapMs: 55,
            postHoldMs: 90,
            sortOrder: 90,
            requiredPotentials: platChantPot,
            VirtualDir.Up, VirtualDir.Up, VirtualDir.Up, VirtualDir.Up));
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
            IsBaseMove = true,
            HoldToRepeat = true,
            RepeatCooldownMs = 0,
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
            IsBaseMove = true,
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
            ComboHint = "Up + SP",
            Description = "Up (20ms) -> SP (40ms) -> 20ms.",
            Category = MoveCategory.CombatCombo,
            IsBaseMove = true,
            IsPreset = true,
            SortOrder = 20,
            Steps = new()
            {
                InputStep.HoldDir(VirtualDir.Up),
                InputStep.Wait(20),
                InputStep.Press(InputActionKind.Summon),
                InputStep.Wait(40),
                InputStep.Release(InputActionKind.Summon),
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
            ComboHint = "SP + Down -> Skill",
            Description = "SP + Down (15ms) -> Skill (15ms) -> 15ms.",
            Category = MoveCategory.CombatCombo,
            IsBaseMove = true,
            IsPreset = true,
            SortOrder = 10,
            Steps = new()
            {
                InputStep.Press(InputActionKind.Summon),
                InputStep.HoldDir(VirtualDir.Down),
                InputStep.Wait(15),
                InputStep.Press(InputActionKind.Skill),
                InputStep.Wait(15),
                InputStep.Release(InputActionKind.Skill),
                InputStep.Wait(15),
                InputStep.ReleaseDir(),
                InputStep.Release(InputActionKind.Summon)
            }
        });

        list.Add(new MoveDefinition
        {
            Id = "icey.sheathing_stance",
            ActorId = 103701,
            Name = "Sheathing Stance",
            ComboHint = "SP + Down",
            Description = "SP + Down (30ms).",
            Category = MoveCategory.CombatCombo,
            IsBaseMove = true,
            IsPreset = true,
            SortOrder = 20,
            Steps = new()
            {
                InputStep.Press(InputActionKind.Summon),
                InputStep.HoldDir(VirtualDir.Down),
                InputStep.Wait(30),
                InputStep.ReleaseDir(),
                InputStep.Release(InputActionKind.Summon)
            }
        });

        list.Add(new MoveDefinition
        {
            Id = "icey.charging_stance",
            ActorId = 103701,
            Name = "Charging Stance",
            ComboHint = "SP + Up",
            Description = "SP + Up (30ms).",
            Category = MoveCategory.CombatCombo,
            IsBaseMove = true,
            IsPreset = true,
            SortOrder = 30,
            Steps = new()
            {
                InputStep.Press(InputActionKind.Summon),
                InputStep.HoldDir(VirtualDir.Up),
                InputStep.Wait(30),
                InputStep.ReleaseDir(),
                InputStep.Release(InputActionKind.Summon)
            }
        });

        list.Add(CreateDodgeAttack("icey.dodge_attack", 103701, 40));
    }

    private static void AddStandardCombos(List<MoveDefinition> list, int actorId, string prefix)
    {
        list.Add(CreateDodgeAttack($"{prefix}.dodge_attack", actorId, 10, "Dash / Dodge Attack"));
        list.Add(CreateHeavyDodgeAttack($"{prefix}.heavy_dodge", actorId, 20, "Heavy Dodge Attack"));
        list.Add(CreateDirAction($"{prefix}.up_attack", actorId, "Up + Attack", VirtualDir.Up, InputActionKind.Attack, 30));
        list.Add(CreateDirAction($"{prefix}.down_attack", actorId, "Down + Attack", VirtualDir.Down, InputActionKind.Attack, 40));
        list.Add(CreateDirAction($"{prefix}.up_skill", actorId, "Up + Skill", VirtualDir.Up, InputActionKind.Skill, 50));
        list.Add(CreateDirAction($"{prefix}.down_skill", actorId, "Down + Skill", VirtualDir.Down, InputActionKind.Skill, 60));
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
            IsBaseMove = true,
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
            IsBaseMove = true,
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
            IsBaseMove = true,
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
        uint[] requiredPotentials,
        params VirtualDir[] sequence)
    {
        var steps = new List<InputStep>
        {
            InputStep.Press(InputActionKind.Summon),
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
        steps.Add(InputStep.Release(InputActionKind.Summon));

        return new MoveDefinition
        {
            Id = id,
            ActorId = actorId,
            Name = name,
            ComboHint = hint,
            Description = desc,
            Category = MoveCategory.SpellChant,
            IsBaseMove = requiredPotentials.Length == 0,
            RequiredPotentialId = requiredPotentials.Length > 0 ? requiredPotentials[0] : 0u,
            RequiredPotentialIds = requiredPotentials,
            RepeatCooldownMs = 0,
            IsPreset = true,
            SortOrder = sortOrder,
            Steps = steps
        };
    }

    private static List<InputStep>? ParseLocalizedInput(
        string inputRaw,
        out string hint,
        out bool isEligible,
        out bool requiresAirborne,
        out string canonicalKey)
    {
        hint = string.Empty;
        isEligible = false;
        requiresAirborne = false;
        canonicalKey = string.Empty;

        if (string.IsNullOrWhiteSpace(inputRaw))
            return null;

        string s = inputRaw.Trim();
        if (s.IndexOf("[Airborne]", StringComparison.OrdinalIgnoreCase) >= 0)
            requiresAirborne = true;

        // Normalize composite dynamic/combo wrappers
        s = Regex.Replace(s, @"<combo>\s*<action=([^>]+)>\s*</combo>\s*<dynamic>\s*<action=[^>]+>\s*</dynamic>", "<action=$1>", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<action=Summon>\s*<action=Dynamic>", "<action=Summon>", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[(?:Airborne|Ground)\]", "", RegexOptions.IgnoreCase);

        // Take primary formula if "or" alternatives are listed
        var branches = BranchSplitRegex.Split(s);
        string branch = branches.Length > 0 ? branches[0].Trim() : s;

        var rawParts = branch.Split(new[] { '·' }, StringSplitOptions.RemoveEmptyEntries);
        var steps = new List<InputStep>();
        var hintParts = new List<string>();
        var canonParts = new List<string>();

        bool hasDir = false;
        bool hasHold = false;
        bool hasRepeat = false;
        bool usesNonBasic = false;
        int totalActions = 0;

        for (int pIdx = 0; pIdx < rawParts.Length; pIdx++)
        {
            string part = rawParts[pIdx].Trim();
            if (string.IsNullOrEmpty(part))
                continue;

            bool partHold = part.IndexOf("hold", StringComparison.OrdinalIgnoreCase) >= 0;
            bool partRepeat = part.IndexOf("repeatedly", StringComparison.OrdinalIgnoreCase) >= 0
                              || part.IndexOf("repeat", StringComparison.OrdinalIgnoreCase) >= 0;

            if (partHold) hasHold = true;
            if (partRepeat) hasRepeat = true;

            var dirs = new List<VirtualDir>();
            var acts = new List<InputActionKind>();

            foreach (Match m in ActionTagRegex.Matches(part))
            {
                string tok = m.Groups[1].Value.Trim();
                if (TryParseDirToken(tok, out var dir))
                {
                    dirs.Add(dir);
                    hasDir = true;
                }
                else if (TryParseActionToken(tok, out var act))
                {
                    acts.Add(act);
                    totalActions++;
                    if (act is InputActionKind.Summon or InputActionKind.Special or InputActionKind.Special2
                        or InputActionKind.Dynamic or InputActionKind.CustomUse1 or InputActionKind.CustomUse2 or InputActionKind.Ultra)
                    {
                        usesNonBasic = true;
                    }
                }
                else
                {
                    return null;
                }
            }

            if (dirs.Count == 0 && acts.Count == 0)
                continue;

            // Special case: double directional tap like MoveX- + MoveX- or MoveY+ + MoveY+
            if (acts.Count == 0 && dirs.Count >= 2)
            {
                var dirHint = new StringBuilder();
                var dirCanon = new List<string>();
                for (int d = 0; d < dirs.Count; d++)
                {
                    if (d > 0)
                    {
                        steps.Add(InputStep.Wait(55));
                        dirHint.Append(" + ");
                    }
                    steps.Add(InputStep.HoldDir(dirs[d]));
                    steps.Add(InputStep.Wait(45));
                    steps.Add(InputStep.ReleaseDir());
                    dirHint.Append(dirs[d]);
                    dirCanon.Add(dirs[d].ToString());
                }
                hintParts.Add(dirHint.ToString());
                canonParts.Add(string.Join("+", dirCanon));
            }
            else if (dirs.Count == 0 && acts.Count == 0)
            {
                continue;
            }
            else
            {
                VirtualDir? stepDir = dirs.Count > 0 ? dirs[0] : null;
                int holdDurationMs = partHold ? 260 : 45;
                int repeatCount = partRepeat ? 3 : 1;

                var hSb = new StringBuilder();
                if (partHold) hSb.Append("Hold ");
                if (stepDir.HasValue)
                {
                    hSb.Append(stepDir.Value);
                    steps.Add(InputStep.HoldDir(stepDir.Value));
                    steps.Add(InputStep.Wait(25));
                }

                for (int rep = 0; rep < repeatCount; rep++)
                {
                    if (rep > 0)
                        steps.Add(InputStep.Wait(50));

                    for (int a = 0; a < acts.Count; a++)
                    {
                        if (rep == 0)
                        {
                            if (hSb.Length > 0 && (stepDir.HasValue || a > 0))
                                hSb.Append(" + ");
                            hSb.Append(FormatActionLabel(acts[a]));
                        }
                        steps.Add(InputStep.Press(acts[a]));
                        if (a < acts.Count - 1)
                            steps.Add(InputStep.Wait(30));
                    }

                    steps.Add(InputStep.Wait(holdDurationMs));

                    for (int a = acts.Count - 1; a >= 0; a--)
                        steps.Add(InputStep.Release(acts[a]));
                }

                if (stepDir.HasValue)
                {
                    steps.Add(InputStep.Wait(25));
                    steps.Add(InputStep.ReleaseDir());
                }

                if (partRepeat)
                    hSb.Append(" (x3)");

                hintParts.Add(hSb.ToString());

                var canonList = acts.Select(a => a.ToString()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                if (stepDir.HasValue)
                    canonList.Insert(0, stepDir.Value.ToString());
                if (partHold)
                    canonList.Add("Hold");
                if (partRepeat)
                    canonList.Add("Repeat");
                canonParts.Add(string.Join("+", canonList));
            }

            if (pIdx < rawParts.Length - 1)
                steps.Add(InputStep.Wait(95));
        }

        if (steps.Count == 0)
            return null;

        // Avoid redundant pure 1-button ground taps (Attack, Skill, Dash, Jump already have native 1-button binds)
        isEligible = requiresAirborne || hasDir || hasHold || hasRepeat || totalActions >= 2 || usesNonBasic;
        string joinedHint = string.Join(" -> ", hintParts);
        hint = requiresAirborne ? $"[Air] {joinedHint}" : joinedHint;

        string canonBody = string.Join("->", canonParts);
        canonicalKey = requiresAirborne ? $"air:{canonBody}" : canonBody;
        return steps;
    }

    private static string FormatActionLabel(InputActionKind act) => act switch
    {
        InputActionKind.Summon => "SP",
        InputActionKind.Special => "Special",
        InputActionKind.Special2 => "Special 2",
        _ => act.ToString()
    };

    private static bool TryParseDirToken(string token, out VirtualDir dir)
    {
        switch (token.ToLowerInvariant())
        {
            case "movey+":
            case "up":
            case "w":
                dir = VirtualDir.Up;
                return true;
            case "movey-":
            case "down":
            case "s":
                dir = VirtualDir.Down;
                return true;
            case "movex-":
            case "left":
            case "a":
                dir = VirtualDir.Left;
                return true;
            case "movex+":
            case "right":
            case "d":
                dir = VirtualDir.Right;
                return true;
            case "movex":
            case "movex,movey":
            case "front":
            case "forward":
                dir = VirtualDir.Forward;
                return true;
            case "movey":
                dir = VirtualDir.Up;
                return true;
            default:
                dir = VirtualDir.Neutral;
                return false;
        }
    }

    private static bool TryParseActionToken(string token, out InputActionKind action)
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
            case "summon":
                action = InputActionKind.Summon;
                return true;
            case "dynamic":
                action = InputActionKind.Dynamic;
                return true;
            case "specialaction1":
            case "special":
            case "specialaction":
            case "legacy":
                action = InputActionKind.Special;
                return true;
            case "specialaction2":
                action = InputActionKind.Special2;
                return true;
            case "ultra":
                action = InputActionKind.Ultra;
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
