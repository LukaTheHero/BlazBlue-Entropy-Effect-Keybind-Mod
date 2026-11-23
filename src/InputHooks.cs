using System;
using GamePlay;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using Rewired;
using Unity.Mathematics.FixedPoint;
using UnityEngine;

namespace Luka.BBEE.KeybindMod;

public static class InputHooks
{
    public static InputCmd? ObservedSpecialCmd { get; private set; }
    private static InputCmd? _rewiredSpecialCmd;
    private static bool _rewiredChecked;

    [HarmonyPatch(typeof(GameInputManager), nameof(GameInputManager.NotifyDirChange))]
    private static class NotifyDirChangePatch
    {
        private static bool Prefix(int playerId, InputCmd cmd, Fp2 dir)
        {
            if (InputSequencer.IsInjecting)
                return true;

            if (cmd == InputCmd.Move && playerId == InputSequencer.ActivePlayerId)
            {
                InputSequencer.LastPhysicalDir = dir;
                if (InputSequencer.HasVirtualDirLock)
                    return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(GameInputManager), nameof(GameInputManager.NotifyCmdChange))]
    private static class NotifyCmdChangePatch
    {
        private static void Postfix(int playerId, InputCmd cmd, bool pressed, Fp2 dir)
        {
            if (InputSequencer.IsInjecting || !pressed || playerId != InputSequencer.ActivePlayerId)
                return;

            // Observe which InputCmd corresponds to LeftCtrl/RightCtrl if player uses Chant/Special manually
            if ((UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl)) &&
                (cmd == InputCmd.SpecialAction1 || cmd == InputCmd.SpecialAction2 || cmd == InputCmd.SpecialAction3 ||
                 cmd == InputCmd.SpecialAction4 || cmd == InputCmd.Dynamic))
            {
                if (ObservedSpecialCmd != cmd)
                {
                    ObservedSpecialCmd = cmd;
                    Plugin.LogInstance?.LogInfo($"Observed Special/Chant InputCmd from Ctrl: {cmd}");
                }
            }
        }
    }

    public static InputCmd ResolveInputCmd(InputActionKind kind, string specialOverride)
    {
        switch (kind)
        {
            case InputActionKind.Attack: return InputCmd.Attack;
            case InputActionKind.Skill: return InputCmd.Skill;
            case InputActionKind.Dash: return InputCmd.Dash;
            case InputActionKind.Jump: return InputCmd.Jump;
            case InputActionKind.Ultra: return InputCmd.Ultra;
            case InputActionKind.Summon: return InputCmd.Summon;
            case InputActionKind.CustomUse1: return InputCmd.CustomUse1;
            case InputActionKind.CustomUse2: return InputCmd.CustomUse2;
            case InputActionKind.Special:
                if (!string.IsNullOrWhiteSpace(specialOverride) &&
                    !string.Equals(specialOverride, "Auto", StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse<InputCmd>(specialOverride, true, out var forced))
                {
                    return forced;
                }

                if (ObservedSpecialCmd.HasValue)
                    return ObservedSpecialCmd.Value;

                if (!_rewiredChecked)
                {
                    _rewiredChecked = true;
                    _rewiredSpecialCmd = DetectSpecialCmdFromRewired();
                }

                return _rewiredSpecialCmd ?? InputCmd.SpecialAction1;
            default:
                return InputCmd.None;
        }
    }

    private static InputCmd? DetectSpecialCmdFromRewired()
    {
        try
        {
            var player = ReInput.players?.GetPlayer(0);
            var maps = player?.controllers?.maps;
            if (maps == null)
                return null;

            (int actionId, InputCmd cmd)[] candidates =
            {
                (RewiredConsts.Action.SpecialAction1, InputCmd.SpecialAction1),
                (RewiredConsts.Action.SpecialAction2, InputCmd.SpecialAction2),
                (RewiredConsts.Action.SpecialAction4, InputCmd.SpecialAction4),
                (RewiredConsts.Action.Dynamic, InputCmd.Dynamic),
            };

            // First check which candidate action is bound to Ctrl on keyboard
            var buf = new List<ActionElementMap>();
            foreach (var (actionId, cmd) in candidates)
            {
                buf.Clear();
                maps.GetElementMapsWithAction(ControllerType.Keyboard, actionId, true, buf);
                for (int i = 0; i < buf.Count; i++)
                {
                    var kc = buf[i].keyCode;
                    if (kc == KeyCode.LeftControl || kc == KeyCode.RightControl)
                    {
                        Plugin.LogInstance?.LogInfo($"Resolved Special/Chant InputCmd via Rewired Ctrl binding: {cmd}");
                        return cmd;
                    }
                }
            }

            // Otherwise pick the first bound special action on keyboard
            foreach (var (actionId, cmd) in candidates)
            {
                buf.Clear();
                maps.GetElementMapsWithAction(ControllerType.Keyboard, actionId, true, buf);
                if (buf.Count > 0)
                {
                    Plugin.LogInstance?.LogInfo($"Resolved Special/Chant InputCmd via first bound keyboard action: {cmd}");
                    return cmd;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogInstance?.LogDebug($"Rewired special cmd lookup fallback: {ex.Message}");
        }

        return null;
    }
}
