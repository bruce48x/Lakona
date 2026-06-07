#nullable enable

using UnityEngine;

namespace SampleClient.Gameplay
{
    internal static class DotArenaInputUtility
    {
        public static bool IsKeyPressed(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current is { } keyboard)
            {
                return GetControl(key, keyboard)?.isPressed ?? false;
            }
#endif
            return Input.GetKey(key);
        }

        public static bool IsKeyDown(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current is { } keyboard)
            {
                return GetControl(key, keyboard)?.wasPressedThisFrame ?? false;
            }
#endif
            return Input.GetKeyDown(key);
        }

#if ENABLE_INPUT_SYSTEM
        private static UnityEngine.InputSystem.Controls.KeyControl? GetControl(
            KeyCode key,
            UnityEngine.InputSystem.Keyboard keyboard)
        {
            return key switch
            {
                KeyCode.W => keyboard.wKey,
                KeyCode.A => keyboard.aKey,
                KeyCode.S => keyboard.sKey,
                KeyCode.D => keyboard.dKey,
                KeyCode.UpArrow => keyboard.upArrowKey,
                KeyCode.DownArrow => keyboard.downArrowKey,
                KeyCode.LeftArrow => keyboard.leftArrowKey,
                KeyCode.RightArrow => keyboard.rightArrowKey,
                KeyCode.Space => keyboard.spaceKey,
                KeyCode.P => keyboard.pKey,
                _ => null
            };
        }
#endif
    }
}
