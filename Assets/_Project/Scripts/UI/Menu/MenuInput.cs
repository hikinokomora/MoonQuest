using Mehatron.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mehatron.UI.Menu
{
    /// <summary>
    /// Навигация по меню с любого устройства стенда: клавиатура, геймпад, крестовина (hat) руля/джойстика,
    /// поворот руля — влево/вправо, педаль газа — вниз, тормоз — вверх, кнопка «Старт» руля — OK, «Центр VR» — назад.
    /// Удержание направления повторяет шаг.
    /// </summary>
    public class MenuInput
    {
        const float RepeatDelay = 0.38f, RepeatRate = 0.09f;

        readonly InputActionMap map;
        readonly InputAction up, down, left, right, submit, back, tabPrev, tabNext, pause;
        readonly RoverInput rover;

        public int Vertical { get; private set; }
        public int Horizontal { get; private set; }
        public bool Submit { get; private set; }
        public bool Back { get; private set; }
        public int Tab { get; private set; }
        public bool Pause { get; private set; }

        int heldV, heldH;
        float timerV, timerH;
        bool wheelLatchL, wheelLatchR, gasLatch, brakeLatch;

        public MenuInput(RoverInput rover)
        {
            this.rover = rover;
            map = new InputActionMap("Menu");
            up = Button("Up", "<Keyboard>/upArrow", "<Keyboard>/w", "<Gamepad>/dpad/up", "<Gamepad>/leftStick/up", "<Joystick>/hat/up");
            down = Button("Down", "<Keyboard>/downArrow", "<Keyboard>/s", "<Gamepad>/dpad/down", "<Gamepad>/leftStick/down", "<Joystick>/hat/down");
            left = Button("Left", "<Keyboard>/leftArrow", "<Keyboard>/a", "<Gamepad>/dpad/left", "<Gamepad>/leftStick/left", "<Joystick>/hat/left");
            right = Button("Right", "<Keyboard>/rightArrow", "<Keyboard>/d", "<Gamepad>/dpad/right", "<Gamepad>/leftStick/right", "<Joystick>/hat/right");
            submit = Button("Submit", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            back = Button("Back", "<Keyboard>/escape", "<Keyboard>/backspace", "<Gamepad>/buttonEast");
            tabPrev = Button("TabPrev", "<Keyboard>/q", "<Gamepad>/leftShoulder");
            tabNext = Button("TabNext", "<Keyboard>/e", "<Keyboard>/tab", "<Gamepad>/rightShoulder");
            pause = Button("Pause", "<Keyboard>/escape", "<Gamepad>/start");
            map.Enable();
        }

        InputAction Button(string name, params string[] paths)
        {
            var a = map.AddAction(name, InputActionType.Button);
            foreach (var p in paths) a.AddBinding(p);
            return a;
        }

        public void Dispose()
        {
            map.Disable();
            map.Dispose();
        }

        /// <summary>Порог с «защёлкой»: срабатывает при пересечении вверх, отпускается ниже половины.</summary>
        static bool Edge(float v, float threshold, ref bool latch)
        {
            if (!latch && v > threshold) { latch = true; return true; }
            if (latch && v < threshold * 0.5f) latch = false;
            return false;
        }

        public void Update()
        {
            float dt = Time.unscaledDeltaTime;
            int v = up.IsPressed() ? -1 : down.IsPressed() ? 1 : 0;
            int h = left.IsPressed() ? -1 : right.IsPressed() ? 1 : 0;
            // Крестовина обода MOZA и хатка T.16000M приходят через плагины.
            var dpad = PluginInput.Dpad;
            if (v == 0) v = -dpad.y;
            if (h == 0) h = dpad.x;
            Vertical = Repeat(v, ref heldV, ref timerV, dt);
            Horizontal = Repeat(h, ref heldH, ref timerH, dt);

            Submit = submit.WasPressedThisFrame();
            Back = back.WasPressedThisFrame();
            Pause = pause.WasPressedThisFrame();
            Tab = tabPrev.WasPressedThisFrame() ? -1 : tabNext.WasPressedThisFrame() ? 1 : 0;

            if (rover == null) return;
            // Руль и педали стенда: однократные шаги по порогу.
            // Клавиатура (W/S, A/D, стрелки) тоже рулит ровером — если нажата прямая навигация, педали/руль не учитываем.
            bool direct = v != 0 || h != 0;
            bool l = Edge(-rover.Steer, 0.5f, ref wheelLatchL), r = Edge(rover.Steer, 0.5f, ref wheelLatchR);
            bool g = Edge(rover.Throttle, 0.6f, ref gasLatch), b = Edge(rover.Brake, 0.6f, ref brakeLatch);
            if (!direct)
            {
                if (l) Horizontal = -1;
                if (r) Horizontal = 1;
                if (g) Vertical = 1;
                if (b) Vertical = -1;
            }
            // Клавиатурный Enter уже учтён выше; кнопка «Старт» руля/джойстика приходит через RoverInput.
            if (rover.StartPressed) Submit = true;
            if (rover.RecenterPressed) Back = true;
        }

        static int Repeat(int dir, ref int held, ref float timer, float dt)
        {
            if (dir == 0) { held = 0; return 0; }
            if (dir != held)
            {
                held = dir;
                timer = RepeatDelay;
                return dir;
            }
            timer -= dt;
            if (timer > 0f) return 0;
            timer = RepeatRate;
            return dir;
        }
    }
}
