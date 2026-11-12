using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Nerv.Client
{
    /// <summary>
    /// Тонкая обёртка над вводом: работает и с новой Input System, и со старым Input Manager.
    /// Это и есть «адаптер устройства» из архитектуры: на десктопе — мышь и клавиатура,
    /// в VR его заменит адаптер контроллеров/рук, а слой намерений (NervInteraction) не изменится.
    /// </summary>
    public static class NervInput
    {
        public static Vector2 PointerPosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Mouse m = Mouse.current;
                return m != null ? m.position.ReadValue() : Vector2.zero;
#else
                return Input.mousePosition;
#endif
            }
        }

        public static bool LeftPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
                return Input.GetMouseButtonDown(0);
#endif
            }
        }

        public static bool LeftHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
                return Input.GetMouseButton(0);
#endif
            }
        }

        public static bool LeftReleased
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
                return Input.GetMouseButtonUp(0);
#endif
            }
        }

        public static bool RightHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
                return Input.GetMouseButton(1);
#endif
            }
        }

        public static bool MiddleHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.middleButton.isPressed;
#else
                return Input.GetMouseButton(2);
#endif
            }
        }

        /// <summary>Прокрутка в «щелчках» колеса (трекпад даёт дробные значения).</summary>
        public static float Scroll
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
                return Input.mouseScrollDelta.y;
#endif
            }
        }

        public static bool ShiftHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
#else
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            }
        }

        public static bool AltHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.altKey.isPressed;
#else
                return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#endif
            }
        }

        /// <summary>Нажата ли цифра 1–9 в этом кадре (0 — нет).</summary>
        public static int DigitPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard k = Keyboard.current;
                if (k == null) return 0;
                if (k.digit1Key.wasPressedThisFrame) return 1;
                if (k.digit2Key.wasPressedThisFrame) return 2;
                if (k.digit3Key.wasPressedThisFrame) return 3;
                if (k.digit4Key.wasPressedThisFrame) return 4;
                if (k.digit5Key.wasPressedThisFrame) return 5;
                if (k.digit6Key.wasPressedThisFrame) return 6;
                if (k.digit7Key.wasPressedThisFrame) return 7;
                if (k.digit8Key.wasPressedThisFrame) return 8;
                if (k.digit9Key.wasPressedThisFrame) return 9;
                return 0;
#else
                for (int d = 1; d <= 9; d++)
                    if (Input.GetKeyDown(KeyCode.Alpha0 + d)) return d;
                return 0;
#endif
            }
        }

        public static bool SpacePressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.Space);
#endif
            }
        }

        /// <summary>Клавиша T — таблица Менделеева.</summary>
        public static bool TablePressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.T);
#endif
            }
        }

        public static bool EscapePressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.Escape);
#endif
            }
        }
    }
}
