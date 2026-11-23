using System.Collections.Generic;
using GamePlay;
using NOAH.Core;
using Unity.Mathematics.FixedPoint;
using UnityEngine;

namespace Luka.BBEE.KeybindMod;

public sealed class InputSequencer
{
    public static bool IsInjecting { get; private set; }
    public static bool HasVirtualDirLock { get; private set; }
    public static Fp2 LastPhysicalDir { get; set; } = Fp2.zero;
    public static int ActivePlayerId { get; private set; }

    private MoveDefinition? _activeMove;
    private KeyCode _boundKey = KeyCode.None;
    private int _stepIndex;
    private float _nextStepTime;
    private bool _holdingDir;
    private Fp2 _currentHeldDir = Fp2.zero;
    private readonly HashSet<InputActionKind> _heldActions = new();

    public bool IsRunning => _activeMove != null;

    public void Start(MoveDefinition move, KeyCode boundKey, int playerId, float facingDir, string specialOverride)
    {
        if (_activeMove != null)
            Cancel(playerId);

        ActivePlayerId = playerId;
        _activeMove = move;
        _boundKey = boundKey;
        _stepIndex = 0;
        _nextStepTime = Time.realtimeSinceStartup;
        _holdingDir = false;
        _currentHeldDir = Fp2.zero;
        _heldActions.Clear();

        StepThrough(playerId, facingDir, specialOverride);
    }

    public void Update(int playerId, int activeActorId, bool inputLocked, float facingDir, string specialOverride)
    {
        if (_activeMove == null)
            return;

        ActivePlayerId = playerId;

        if (activeActorId != _activeMove.ActorId || inputLocked)
        {
            Cancel(playerId);
            return;
        }

        if (_activeMove.HoldToRepeat && (_boundKey == KeyCode.None || !UnityEngine.Input.GetKey(_boundKey)))
        {
            Cancel(playerId);
            return;
        }

        if (Time.realtimeSinceStartup < _nextStepTime)
            return;

        StepThrough(playerId, facingDir, specialOverride);
    }

    public void Cancel(int playerId)
    {
        if (_activeMove == null && !HasVirtualDirLock && _heldActions.Count == 0)
            return;

        ReleaseHeldState(playerId);
        _activeMove = null;
        _boundKey = KeyCode.None;
        _stepIndex = 0;
    }

    private void StepThrough(int playerId, float facingDir, string specialOverride)
    {
        if (_activeMove == null)
            return;

        var steps = _activeMove.Steps;
        int guard = 0;

        while (_activeMove != null && guard++ < 64)
        {
            if (_stepIndex >= steps.Count)
            {
                ReleaseHeldState(playerId);
                if (_activeMove.HoldToRepeat && _boundKey != KeyCode.None && UnityEngine.Input.GetKey(_boundKey))
                {
                    _stepIndex = 0;
                    continue;
                }

                _activeMove = null;
                _boundKey = KeyCode.None;
                _stepIndex = 0;
                return;
            }

            var step = steps[_stepIndex++];
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
                    _currentHeldDir = ToFp2(step.Dir, facingDir);
                    _holdingDir = true;
                    HasVirtualDirLock = true;
                    SendDir(playerId, _currentHeldDir);
                    break;

                case InputStepType.ReleaseDir:
                    _holdingDir = false;
                    _currentHeldDir = Fp2.zero;
                    HasVirtualDirLock = false;
                    SendDir(playerId, Fp2.zero);
                    break;

                case InputStepType.Press:
                    if (_heldActions.Add(step.Action))
                    {
                        var cmd = InputHooks.ResolveInputCmd(step.Action, specialOverride);
                        SendCmd(playerId, cmd, true, _holdingDir ? _currentHeldDir : Fp2.zero);
                    }
                    break;

                case InputStepType.Release:
                    if (_heldActions.Remove(step.Action))
                    {
                        var cmd = InputHooks.ResolveInputCmd(step.Action, specialOverride);
                        SendCmd(playerId, cmd, false, _holdingDir ? _currentHeldDir : Fp2.zero);
                    }
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
            {
                var cmd = InputHooks.ResolveInputCmd(action, specialOverride);
                SendCmd(playerId, cmd, false, Fp2.zero);
            }
        }

        if (_holdingDir || HasVirtualDirLock)
        {
            _holdingDir = false;
            _currentHeldDir = Fp2.zero;
            HasVirtualDirLock = false;
            SendDir(playerId, LastPhysicalDir);
        }
    }

    private static void SendCmd(int playerId, InputCmd cmd, bool pressed, Fp2 dir)
    {
        if (cmd == InputCmd.None)
            return;

        var gim = SingletonBehaviour<GameInputManager>.Instance;
        if (gim == null)
            return;

        IsInjecting = true;
        try
        {
            gim.NotifyCmdChange(playerId, cmd, pressed, dir);
        }
        finally
        {
            IsInjecting = false;
        }
    }

    private static void SendDir(int playerId, Fp2 dir)
    {
        var gim = SingletonBehaviour<GameInputManager>.Instance;
        if (gim == null)
            return;

        IsInjecting = true;
        try
        {
            gim.NotifyDirChange(playerId, InputCmd.Move, dir);
        }
        finally
        {
            IsInjecting = false;
        }
    }

    private static Fp2 ToFp2(VirtualDir dir, float facingDir)
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

        return x == 0 && y == 0 ? Fp2.zero : new Fp2((Fp)x, (Fp)y);
    }
}
