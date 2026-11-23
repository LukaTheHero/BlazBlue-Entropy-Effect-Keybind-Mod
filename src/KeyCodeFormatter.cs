using System;
using System.Collections.Generic;
using UnityEngine;

namespace Luka.BBEE.KeybindMod;

public static class KeyCodeFormatter
{
    private static KeyCode[]? _bindableKeys;

    public static KeyCode[] GetBindableKeys()
    {
        if (_bindableKeys != null)
            return _bindableKeys;

        var list = new List<KeyCode>();
        // Prioritize mouse buttons & standard keys; Mouse0..Mouse6 are Mouse1..Mouse7
        for (int i = (int)KeyCode.Mouse0; i <= (int)KeyCode.Mouse6; i++)
            list.Add((KeyCode)i);

        foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
        {
            if (kc == KeyCode.None || kc == KeyCode.Escape)
                continue;
            if (kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6)
                continue;
            if (kc >= KeyCode.JoystickButton0)
                continue;
            list.Add(kc);
        }

        _bindableKeys = list.ToArray();
        return _bindableKeys;
    }

    public static string Format(KeyCode key)
    {
        return key switch
        {
            KeyCode.None => "Unassigned",
            KeyCode.Mouse0 => "LMB",
            KeyCode.Mouse1 => "RMB",
            KeyCode.Mouse2 => "Middle Mouse",
            KeyCode.Mouse3 => "Mouse 4",
            KeyCode.Mouse4 => "Mouse 5",
            KeyCode.Mouse5 => "Mouse 6",
            KeyCode.Mouse6 => "Mouse 7",
            KeyCode.Alpha0 => "0",
            KeyCode.Alpha1 => "1",
            KeyCode.Alpha2 => "2",
            KeyCode.Alpha3 => "3",
            KeyCode.Alpha4 => "4",
            KeyCode.Alpha5 => "5",
            KeyCode.Alpha6 => "6",
            KeyCode.Alpha7 => "7",
            KeyCode.Alpha8 => "8",
            KeyCode.Alpha9 => "9",
            KeyCode.LeftControl => "Left Ctrl",
            KeyCode.RightControl => "Right Ctrl",
            KeyCode.LeftShift => "Left Shift",
            KeyCode.RightShift => "Right Shift",
            KeyCode.LeftAlt => "Left Alt",
            KeyCode.RightAlt => "Right Alt",
            KeyCode.BackQuote => "`",
            KeyCode.Return => "Enter",
            _ => key.ToString()
        };
    }
}
