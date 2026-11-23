using System;
using System.Collections.Generic;
using GamePlay;
using NOAH.Core;
using Rewired;
using UnityEngine;

namespace Luka.BBEE.KeybindMod;

public sealed class InputSequencer
{
    public static bool IsInjecting { get; private set; }
    public static bool HasVirtualDirLock { get; private set; }
    public static bool PreserveHorizontalDir { get; private set; }
    public static Vector2 VirtualDirVec { get; private set; } = Vector2.zero;
    public static Vector2 LastPhysicalVec { get; set; } = Vector2.zero;
    public static int ActivePlayerId { get; set; }

    private MoveDefinition? _activeMove;
    private KeyCode _boundKey = KeyCode.None;
    private bool _repeatWhileHeld;
    private int _stepIndex;
    private float _nextStepTime;
    private bool _holdingDir;
    private bool _touchedVirtualDir;
    private List<InputStep> _activeSteps = new();
    private readonly HashSet<InputActionKind> _heldActions = new();

    public bool IsRunning => _activeMove != null;

    public void Start(
        MoveDefinition move,
        KeyCode boundKey,
        int playerId,
        float facingDir,
        string specialOverride,
        bool repeatWhileHeld,
        bool isAirborne)
    {
        if (_activeMove != null)
            Cancel(playerId);

        ActivePlayerId = playerId;
        _activeMove = move;
        _boundKey = boundKey;
        _repeatWhileHeld = repeatWhileHeld;
        _stepIndex = 0;
        _nextStepTime = Time.realtimeSinceStartup;
        _holdingDir = false;
        _touchedVirtualDir = false;
        _heldActions.Clear();
        _activeSteps = BuildRuntimeSteps(move, repeatWhileHeld, isAirborne);

        StepThrough(playerId, facingDir, specialOverride, isAirborne);
    }

    public void Update(
        int playerId,
        int activeActorId,
        bool inputLocked,
        float facingDir,
        string specialOverride,
        bool isAirborne)
    {
        if (_activeMove == null)
            return;

        ActivePlayerId = playerId;

        if (activeActorId != _activeMove.ActorId || inputLocked)
        {
            Cancel(playerId);
            return;
        }

        if (_repeatWhileHeld && (_boundKey == KeyCode.None || !UnityEngine.Input.GetKey(_boundKey)))
        {
            Cancel(playerId);
            return;
        }

        if (Time.realtimeSinceStartup < _nextStepTime)
            return;

        StepThrough(playerId, facingDir, specialOverride, isAirborne);
    }

    public void Cancel(int playerId)
    {
        if (_activeMove == null && !HasVirtualDirLock && !_touchedVirtualDir && _heldActions.Count == 0)
            return;

        ReleaseHeldState(playerId);
        _activeMove = null;
        _boundKey = KeyCode.None;
        _repeatWhileHeld = false;
        _stepIndex = 0;
        _activeSteps.Clear();
    }

    public static Vector2 ApplyVirtualDir(Vector2 physical)
    {
        if (!HasVirtualDirLock)
            return physical;

        if (PreserveHorizontalDir && Mathf.Abs(VirtualDirVec.y) > 0.5f && Mathf.Abs(VirtualDirVec.x) < 0.1f)
            return new Vector2(physical.x, VirtualDirVec.y);

        return VirtualDirVec;
    }

    public static Vector2 GetLivePhysicalVec(int playerId)
    {
        try
        {
            var rwPlayer = ReInput.players?.GetPlayer(playerId);
            if (rwPlayer != null)
            {
                float rawX = rwPlayer.GetAxisRaw(RewiredConsts.Action.MoveX);
                float rawY = rwPlayer.GetAxisRaw(RewiredConsts.Action.MoveY);
                float x = Math.Abs(rawX) >= 0.2f ? Math.Sign(rawX) : 0f;
                float y = Math.Abs(rawY) >= 0.2f ? Math.Sign(rawY) : 0f;
                var vec = new Vector2(x, y);
                LastPhysicalVec = vec;
                return vec;
            }
        }
        catch
        {
            // Fallback to cached physical vector
        }

        return LastPhysicalVec;
    }

    public static void SendAxisDirect(GameInputManager gim, int playerId, Vector2 dir, bool ignoreLock)
    {
        IsInjecting = true;
        try
        {
            gim.ResolveActorAxis(playerId, dir, ignoreLock);
        }
        finally
        {
            IsInjecting = false;
        }
    }

    private List<InputStep> BuildRuntimeSteps(MoveDefinition move, bool repeatWhileHeld, bool isAirborne)
    {
        var steps = new List<InputStep>(move.Steps.Count + 6);
        if (move.RequiresAirborne && !isAirborne)
        {
            steps.Add(InputStep.Press(InputActionKind.Jump));
            steps.Add(InputStep.Wait(25));
            steps.Add(InputStep.Release(InputActionKind.Jump));
            steps.Add(InputStep.Wait(25));
        }

        for (int i = 0; i < move.Steps.Count; i++)
            steps.Add(move.Steps[i]);

        if (repeatWhileHeld && move.RepeatCooldownMs > 0)
        {
            if (steps.Count == 0 || steps[steps.Count - 1].Type != InputStepType.WaitMs)
                steps.Add(InputStep.Wait(move.RepeatCooldownMs));
        }

        return steps;
    }

    private void StepThrough(int playerId, float facingDir, string specialOverride, bool isAirborne)
    {
        if (_activeMove == null)
            return;

        int guard = 0;

        while (_activeMove != null && guard++ < 64)
        {
            if (_stepIndex >= _activeSteps.Count)
            {
                ReleaseHeldState(playerId);
                if (_repeatWhileHeld && _boundKey != KeyCode.None && UnityEngine.Input.GetKey(_boundKey))
                {
                    _activeSteps = BuildRuntimeSteps(_activeMove, true, isAirborne);
                    _stepIndex = 0;
                    continue;
                }

                _activeMove = null;
                _boundKey = KeyCode.None;
                _repeatWhileHeld = false;
                _stepIndex = 0;
                _activeSteps.Clear();
                return;
            }

            var step = _activeSteps[_stepIndex++];
            switch (step.Type)
            {
                case InputStepType.WaitMs:
                    if (step.DurationMs > 0)
                    {
                        _nextStepTime = Time.realtimeSinceStartup + (step.DurationMs / 1000f);
                        return;
                    }
                    break;

                case InputStepType.HoldDir:
                {
                    bool isChant = _activeMove.Category == MoveCategory.SpellChant;
                    PreserveHorizontalDir = !isChant && (step.Dir == VirtualDir.Up || step.Dir == VirtualDir.Down);
                    VirtualDirVec = ToVec2(step.Dir, facingDir);
                    _holdingDir = true;
                    HasVirtualDirLock = true;
                    _touchedVirtualDir = true;
                    SendDir(playerId, ApplyVirtualDir(GetLivePhysicalVec(playerId)), ignoreLock: false);
                    break;
                }

                case InputStepType.ReleaseDir:
                    _holdingDir = false;
                    if (_activeMove.Category == MoveCategory.SpellChant)
                    {
                        PreserveHorizontalDir = false;
                        VirtualDirVec = Vector2.zero;
                        HasVirtualDirLock = true;
                        SendDir(playerId, Vector2.zero, ignoreLock: false);
                    }
                    else
                    {
                        PreserveHorizontalDir = false;
                        VirtualDirVec = Vector2.zero;
                        HasVirtualDirLock = false;
                        _touchedVirtualDir = false;
                        SendDir(playerId, GetLivePhysicalVec(playerId), ignoreLock: true);
                    }
                    break;

                case InputStepType.Press:
                    if (_heldActions.Add(step.Action))
                        SendCmd(playerId, step.Action, true, specialOverride);
                    break;

                case InputStepType.Release:
                    if (_heldActions.Remove(step.Action))
                        SendCmd(playerId, step.Action, false, specialOverride);
                    break;
            }
        }
    }

    private void ReleaseHeldState(int playerId)
    {
        var specialOverride = Plugin.SpecialCmdOverride?.Value ?? "Auto";

        if (_heldActions.Count > 0)
        {
            var copy = new List<InputActionKind>(_heldActions);
            _heldActions.Clear();
            foreach (var action in copy)
                SendCmd(playerId, action, false, specialOverride);
        }

        if (_holdingDir || HasVirtualDirLock || _touchedVirtualDir)
        {
            _holdingDir = false;
            PreserveHorizontalDir = false;
            VirtualDirVec = Vector2.zero;
            HasVirtualDirLock = false;
            _touchedVirtualDir = false;
            SendDir(playerId, GetLivePhysicalVec(playerId), ignoreLock: true);
        }
    }

    private static void SendCmd(int playerId, InputActionKind action, bool pressed, string specialOverride)
    {
        var cmd = InputHooks.ResolveInputCmd(action, specialOverride);
        if (cmd == InputCmd.None)
            return;

        var gim = SingletonBehaviour<GameInputManager>.Instance;
        if (gim == null)
            return;

        var phys = GetLivePhysicalVec(playerId);
        var effDir = HasVirtualDirLock ? ApplyVirtualDir(phys) : phys;

        IsInjecting = true;
        try
        {
            gim.ResolveActorButton(playerId, cmd, pressed, effDir, false);
        }
        finally
        {
            IsInjecting = false;
        }
    }

    private static void SendDir(int playerId, Vector2 dir, bool ignoreLock)
    {
        var gim = SingletonBehaviour<GameInputManager>.Instance;
        if (gim == null)
            return;

        SendAxisDirect(gim, playerId, dir, ignoreLock);
    }

    private static Vector2 ToVec2(VirtualDir dir, float facingDir)
    {
        int x = 0;
        int y = 0;
        switch (dir)
        {
            case VirtualDir.Up:
                y = 1;
                break;
            case VirtualDir.Down:
                y = -1;
                break;
            case VirtualDir.Left:
                x = -1;
                break;
            case VirtualDir.Right:
                x = 1;
                break;
            case VirtualDir.Forward:
                x = facingDir >= 0f ? 1 : -1;
                break;
            case VirtualDir.Back:
                x = facingDir >= 0f ? -1 : 1;
                break;
        }

        return new Vector2(x, y);
    }
}
