using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace Luka.BBEE.KeybindMod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "luka.bbee.keybindmod";
    public const string PluginName = "BBEE Keybind Mod";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource? LogInstance { get; private set; }
    internal static BindingStore? Bindings { get; private set; }
    internal static KeybindBehaviour? Behaviour { get; private set; }

    internal static ConfigEntry<bool>? Enabled { get; private set; }
    internal static ConfigEntry<KeyCode>? MenuKey { get; private set; }
    internal static ConfigEntry<bool>? ShowActiveHud { get; private set; }
    internal static ConfigEntry<bool>? LogDiscoveredCatalog { get; private set; }
    internal static ConfigEntry<string>? SpecialCmdOverride { get; private set; }

    private static object? _jsEnv;
    private static MethodInfo? _jsEvalMethod;
    private static int _jsEvalParamCount;
    private static Harmony? _harmony;

    public override void Load()
    {
        LogInstance = Log;

        Enabled = Config.Bind("General", "Enabled", true, "Enable per-character custom skill keybinds.");
        ShowActiveHud = Config.Bind("General", "ShowActiveHud", false, "Show a minimal bottom-left active character keybind status pill during runs.");
        LogDiscoveredCatalog = Config.Bind("General", "LogDiscoveredCatalog", false, "Log discovered ActorActionConf/instruction counts on startup for debugging.");
        MenuKey = Config.Bind("Input", "MenuKey", KeyCode.F4, "Keyboard key to open/close the Keybind Mod menu.");
        SpecialCmdOverride = Config.Bind("Input", "SpecialCmdOverride", "Auto", "Override which game InputCmd triggers Chant/Special skills (Auto, SpecialAction1, SpecialAction2, SpecialAction3, SpecialAction4, Dynamic).");

        Bindings = new BindingStore(Paths.ConfigPath, Log);

        if (!Enabled.Value)
        {
            Log.LogInfo($"{PluginName} v{PluginVersion} is disabled in config.");
            return;
        }

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(InputHooks).Assembly);

        var jsManagerType = typeof(JsManager);
        var initMethod = AccessTools.Method(jsManagerType, nameof(JsManager.InitCsCallJs));
        var broadMethod = AccessTools.Method(jsManagerType, nameof(JsManager.BroadCSEvent), new[] { typeof(string), typeof(string) });

        if (initMethod != null)
            _harmony.Patch(initMethod, postfix: new HarmonyMethod(typeof(Plugin), nameof(OnJsEnvironmentReady)));
        else
            Log.LogWarning("JsManager.InitCsCallJs not found; falling back to C#-only character detection.");

        if (broadMethod != null)
            _harmony.Patch(broadMethod, postfix: new HarmonyMethod(typeof(Plugin), nameof(OnJsBroadcastEvent)));

        Behaviour = AddComponent<KeybindBehaviour>();
        Log.LogInfo($"{PluginName} v{PluginVersion} loaded! Press {MenuKey.Value} in-game to bind character skills.");
    }

    public override bool Unload()
    {
        Behaviour?.OnDisable();
        _harmony?.UnpatchSelf();
        return true;
    }

    private static void OnJsEnvironmentReady(object __0)
    {
        try
        {
            if (__0 == null)
                return;

            _jsEnv = __0;
            _jsEvalMethod = __0.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "Eval" && !m.IsGenericMethodDefinition)
                .Where(m =>
                {
                    var p = m.GetParameters();
                    return p.Length is >= 1 and <= 2 && p[0].ParameterType == typeof(string);
                })
                .OrderBy(m => m.GetParameters().Length)
                .FirstOrDefault();

            if (_jsEvalMethod == null)
            {
                LogInstance?.LogWarning("Could not locate Puerts.JsEnv.Eval(string[,string]).");
                return;
            }

            _jsEvalParamCount = _jsEvalMethod.GetParameters().Length;

            var bridgeJs = LoadEmbeddedResource("keybind-bridge.js");
            EvalJs(bridgeJs, "keybind-bridge.js");
            EvalJs("if (globalThis.__lukaKeybindModInstall) globalThis.__lukaKeybindModInstall(require);", "keybind-bridge-init.js");
        }
        catch (Exception ex)
        {
            LogInstance?.LogError($"Failed to initialize PuerTS keybind bridge: {ex}");
        }
    }

    private static void OnJsBroadcastEvent(string __0, string __1)
    {
        if (__0 == "LukaKeybindMod" && !string.IsNullOrEmpty(__1))
            Behaviour?.ApplyJsMessage(__1);
    }

    internal static void RequestJsSnapshot(bool includeCatalog)
    {
        if (_jsEnv == null || _jsEvalMethod == null)
            return;

        try
        {
            EvalJs($"globalThis.__lukaKeybindModV1 && globalThis.__lukaKeybindModV1.getSnapshot({(includeCatalog ? "true" : "false")});", "keybind-snapshot.js");
        }
        catch (Exception ex)
        {
            LogInstance?.LogDebug($"RequestJsSnapshot failed: {ex.Message}");
        }
    }

    internal static void SetJsBattlePaused(bool paused)
    {
        if (_jsEnv == null || _jsEvalMethod == null)
            return;

        try
        {
            EvalJs($"globalThis.__lukaKeybindModV1 && globalThis.__lukaKeybindModV1.setPaused({(paused ? "true" : "false")});", "keybind-pause.js");
        }
        catch (Exception ex)
        {
            LogInstance?.LogDebug($"SetJsBattlePaused failed: {ex.Message}");
        }
    }

    private static void EvalJs(string chunk, string chunkName)
    {
        if (_jsEnv == null || _jsEvalMethod == null)
            return;

        object?[] args = _jsEvalParamCount == 1
            ? new object?[] { chunk }
            : new object?[] { chunk, chunkName };

        _jsEvalMethod.Invoke(_jsEnv, args);
    }

    private static string LoadEmbeddedResource(string name)
    {
        var asm = typeof(Plugin).Assembly;
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
