#if UNITY_EDITOR
#nullable enable
using System;
using System.Reflection;
using SampleClient.Gameplay;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEditor;
#if UNITY_2022_3_OR_NEWER
using UnityEngine;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

[InitializeOnLoad]
internal static class FocusGameViewOnPlay
{
    private static readonly Type? GameViewType = Type.GetType("UnityEditor.GameView, UnityEditor");
#if ENABLE_INPUT_SYSTEM
    private static bool _simulatedHoldActive;
    private static double _simulatedHoldUntil;
#endif

    static FocusGameViewOnPlay()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#if ENABLE_INPUT_SYSTEM
        EditorApplication.update += UpdateSimulatedHold;
        EditorApplication.update += UpdateEditorKeyboardBridge;
#endif
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
        {
            return;
        }

        EditorApplication.delayCall += FocusGameView;
    }

    [MenuItem("Tools/Lakona.Rpc/Focus Game View")]
    private static void FocusGameView()
    {
        if (GameViewType == null)
        {
            return;
        }

        var gameView = EditorWindow.GetWindow(GameViewType);
        if (gameView == null)
        {
            return;
        }

        gameView.Focus();
        gameView.Repaint();
    }

#if ENABLE_INPUT_SYSTEM
    [MenuItem("Tools/Lakona.Rpc/Simulate/Press Enter")]
    private static void SimulateEnter()
    {
        FocusGameView();
        QueueKeyTap(Key.Enter);
    }

    [MenuItem("Tools/Lakona.Rpc/Simulate/Hold W 0.4s")]
    private static void SimulateHoldW()
    {
        FocusGameView();
        BeginKeyHold(Key.W, 0.4d);
    }

    private static void QueueKeyTap(Key key)
    {
        var keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        InputSystem.Update();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        InputSystem.Update();
    }

    private static void BeginKeyHold(Key key, double seconds)
    {
        var keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
        _simulatedHoldActive = true;
        _simulatedHoldUntil = EditorApplication.timeSinceStartup + seconds;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        InputSystem.Update();
    }

    private static void UpdateSimulatedHold()
    {
        if (!_simulatedHoldActive || EditorApplication.timeSinceStartup < _simulatedHoldUntil)
        {
            return;
        }

        var keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
        _simulatedHoldActive = false;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        InputSystem.Update();
    }

    private static void UpdateEditorKeyboardBridge()
    {
        if (!EditorApplication.isPlaying)
        {
            ClearEditorInputOverride();
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            ClearEditorInputOverride();
            return;
        }

        var game = UnityEngine.Object.FindObjectOfType<DotArenaGame>();
        if (game == null)
        {
            ClearEditorInputOverride();
            return;
        }

        if (IsModeSelect(game) && IsStartPressed(keyboard))
        {
            Debug.Log("[Lakona.Rpc] Editor keyboard bridge starting single-player session.");
            InvokeNonPublic(game, "BeginSinglePlayerMatch");
        }

        var localMatch = GetField<ArenaSimulation>(game, "_localMatch");
        if (localMatch == null)
        {
            ClearEditorInputOverride(game);
            return;
        }

        var move = ReadEditorMove(keyboard);
        SetField(game, "_editorMoveOverride", move);
        SetField(game, "_hasEditorInputOverride", move != Vector2.zero);
    }

    private static bool IsModeSelect(DotArenaGame game)
    {
        var value = GetField<object>(game, "_entryMenuState");
        return string.Equals(value?.ToString(), "ModeSelect", StringComparison.Ordinal);
    }

    private static bool IsStartPressed(Keyboard keyboard)
    {
        return keyboard.enterKey.wasPressedThisFrame ||
               keyboard.numpadEnterKey.wasPressedThisFrame ||
               keyboard.spaceKey.wasPressedThisFrame ||
               keyboard.wKey.wasPressedThisFrame ||
               keyboard.aKey.wasPressedThisFrame ||
               keyboard.sKey.wasPressedThisFrame ||
               keyboard.dKey.wasPressedThisFrame ||
               keyboard.upArrowKey.wasPressedThisFrame ||
               keyboard.leftArrowKey.wasPressedThisFrame ||
               keyboard.downArrowKey.wasPressedThisFrame ||
               keyboard.rightArrowKey.wasPressedThisFrame;
    }

    private static Vector2 ReadEditorMove(Keyboard keyboard)
    {
        var x = 0f;
        var y = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;

        var move = new Vector2(x, y);
        return move.sqrMagnitude > 1f ? move.normalized : move;
    }

    private static void InvokeNonPublic(object target, string methodName)
    {
        target.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)?
            .Invoke(target, null);
    }

    private static T? GetField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            return default;
        }

        return (T?)field.GetValue(target);
    }

    private static void SetField<T>(object target, string fieldName, T value)
    {
        target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
    }

    private static void ClearEditorInputOverride(DotArenaGame? game = null)
    {
        game ??= UnityEngine.Object.FindObjectOfType<DotArenaGame>();
        if (game == null)
        {
            return;
        }

        SetField(game, "_editorMoveOverride", Vector2.zero);
        SetField(game, "_hasEditorInputOverride", false);
    }
#endif

    [MenuItem("Tools/Lakona.Rpc/Simulate/Direct Start Single Player")]
    private static void DirectStartSinglePlayer()
    {
        var game = UnityEngine.Object.FindObjectOfType<DotArenaGame>();
        if (game == null)
        {
            Debug.LogWarning("[Lakona.Rpc] DotArenaGame not found.");
            return;
        }

        var method = typeof(DotArenaGame).GetMethod("BeginSinglePlayerMatch", BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(game, null);
    }

    [MenuItem("Tools/Lakona.Rpc/Simulate/Direct Move Up")]
    private static void DirectMoveUp()
    {
        var game = UnityEngine.Object.FindObjectOfType<DotArenaGame>();
        if (game == null)
        {
            Debug.LogWarning("[Lakona.Rpc] DotArenaGame not found.");
            return;
        }

        var localMatchField = typeof(DotArenaGame).GetField("_localMatch", BindingFlags.Instance | BindingFlags.NonPublic);
        var localPlayerIdField = typeof(DotArenaGame).GetField("_localPlayerId", BindingFlags.Instance | BindingFlags.NonPublic);
        var inputTickField = typeof(DotArenaGame).GetField("_inputTick", BindingFlags.Instance | BindingFlags.NonPublic);

        if (localMatchField?.GetValue(game) is not ArenaSimulation localMatch ||
            localPlayerIdField?.GetValue(game) is not string playerId ||
            string.IsNullOrWhiteSpace(playerId) ||
            inputTickField == null)
        {
            Debug.LogWarning("[Lakona.Rpc] Single-player session is not active.");
            return;
        }

        var nextTick = ((int)inputTickField.GetValue(game)!) + 1;
        inputTickField.SetValue(game, nextTick);
        localMatch.SubmitInput(new InputMessage
        {
            PlayerId = playerId,
            MoveX = 0f,
            MoveY = 1f,
            Tick = nextTick
        });
    }
}
#endif
