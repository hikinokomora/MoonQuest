using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mehatron.Vehicle
{
    /// <summary>
    /// Ввод: руль/педали и самолётный джойстик по путям из <see cref="InputCalibration"/>,
    /// плюс клавиатура (WASD/стрелки, Space, Enter) и геймпад.
    /// Если подключены руль MOZA или джойстик T.16000M, их данные берутся из плагинов (<see cref="PluginInput"/>),
    /// а Input System для этих устройств не читается — иначе одно и то же считалось бы дважды.
    /// Руль и самолётный джойстик — оба <c>Joystick</c>, поэтому у каждого своя карта действий,
    /// привязанная только к его устройствам (иначе поворот ручки читался бы как педаль тормоза).
    /// Действия создаются в коде, чтобы калибровку можно было менять без правки .inputactions.
    /// </summary>
    public class RoverInput : MonoBehaviour
    {
        [SerializeField] InputCalibration calibration;
        [SerializeField] float keyboardSteerSpeed = 3f;

        /// <summary>Калибровка (настройки меню меняют её копию, а не ассет).</summary>
        public InputCalibration Calibration => calibration;
        public float KeyboardSteerSpeed { get => keyboardSteerSpeed; set => keyboardSteerSpeed = value; }

        /// <summary>Подменить калибровку рабочей копией, чтобы правки из меню не меняли ассет на диске.</summary>
        public InputCalibration UseRuntimeCopy()
        {
            if (calibration != null && !runtimeCopy)
            {
                calibration = Instantiate(calibration);
                runtimeCopy = true;
            }
            return calibration;
        }

        bool runtimeCopy;

        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public bool Handbrake { get; private set; }
        public bool StartPressed { get; private set; }
        public bool RecenterPressed { get; private set; }
        public bool ResetPressed { get; private set; }

        /// <summary>Названия подключённых рулей и самолётных джойстиков (для панели оператора).</summary>
        public IReadOnlyList<string> WheelDevices => wheelNames;
        public IReadOnlyList<string> FlightStickDevices => stickNames;

        /// <summary>Отладочное переопределение (тесты, автопилот): если задано, заменяет руль и педали.</summary>
        public struct Override { public float steer, throttle, brake; public bool handbrake; }
        public Override? DebugOverride { get; set; }

        struct Buttons
        {
            public InputAction handbrake, start, recenter, reset;

            public static Buttons Create(InputActionMap map, string handbrake, string start, string recenter, string reset) => new Buttons
            {
                handbrake = map.AddAction("Handbrake", InputActionType.Button, handbrake),
                start = map.AddAction("Start", InputActionType.Button, start),
                recenter = map.AddAction("Recenter", InputActionType.Button, recenter),
                reset = map.AddAction("ResetRover", InputActionType.Button, reset),
            };
        }

        InputActionMap commonMap, wheelMap, stickMap;
        InputAction altSteer, altThrottle, altBrake;
        InputAction wheelSteer, throttlePedal, brakePedal;
        InputAction stickRoll, stickTwist, stickPitch, throttleLever;
        Buttons commonButtons, wheelButtons, stickButtons;
        float keyboardSteer;
        bool pluginWheel, pluginStick;
        PedalWatch throttleWatch, brakeWatch, leverWatch;
        readonly List<string> wheelNames = new List<string>();
        readonly List<string> stickNames = new List<string>();

        /// <summary>
        /// Педаль с диапазоном -1..1 до первого отчёта устройства читается как 0 → «наполовину нажата».
        /// Поэтому педаль (и РУД) учитывается только после того, как её значение хоть раз изменилось.
        /// </summary>
        struct PedalWatch
        {
            bool hasFirst, active;
            float first;

            public bool Update(float raw)
            {
                if (!hasFirst) { first = raw; hasFirst = true; }
                if (!active && Mathf.Abs(raw - first) > 0.05f) active = true;
                return active;
            }
        }

        void OnEnable()
        {
            var c = calibration != null ? calibration : ScriptableObject.CreateInstance<InputCalibration>();
            if (calibration == null) runtimeCopy = true;
            calibration = c;

            // Клавиатура и геймпад — без ограничения устройств
            commonMap = new InputActionMap("Rover");
            altSteer = commonMap.AddAction("AltSteer", InputActionType.Value);
            altSteer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            altSteer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            altSteer.AddBinding("<Gamepad>/leftStick/x");
            altThrottle = commonMap.AddAction("AltThrottle", InputActionType.Value);
            altThrottle.AddBinding("<Keyboard>/w");
            altThrottle.AddBinding("<Keyboard>/upArrow");
            altThrottle.AddBinding("<Gamepad>/rightTrigger");
            altBrake = commonMap.AddAction("AltBrake", InputActionType.Value);
            altBrake.AddBinding("<Keyboard>/s");
            altBrake.AddBinding("<Keyboard>/downArrow");
            altBrake.AddBinding("<Gamepad>/leftTrigger");
            commonButtons = Buttons.Create(commonMap, "<Keyboard>/space", "<Keyboard>/enter", "<Gamepad>/select", "<Keyboard>/backspace");
            commonButtons.handbrake.AddBinding("<Gamepad>/buttonEast");
            commonButtons.start.AddBinding("<Gamepad>/start");
            commonButtons.start.AddBinding("<Gamepad>/buttonSouth");
            commonButtons.reset.AddBinding("<Gamepad>/buttonNorth");

            // Руль и педали стенда
            wheelMap = new InputActionMap("Wheel");
            wheelSteer = wheelMap.AddAction("WheelSteer", InputActionType.Value, c.steerPath);
            throttlePedal = wheelMap.AddAction("ThrottlePedal", InputActionType.Value, c.throttlePath);
            brakePedal = wheelMap.AddAction("BrakePedal", InputActionType.Value, c.brakePath);
            wheelButtons = Buttons.Create(wheelMap, c.handbrakeButtonPath, c.startButtonPath, c.recenterButtonPath, c.resetRoverButtonPath);

            // Самолётный джойстик
            stickMap = new InputActionMap("FlightStick");
            stickRoll = stickMap.AddAction("StickRoll", InputActionType.Value, c.stickRollPath);
            stickTwist = stickMap.AddAction("StickTwist", InputActionType.Value, c.stickTwistPath);
            stickPitch = stickMap.AddAction("StickPitch", InputActionType.Value, c.stickPitchPath);
            throttleLever = stickMap.AddAction("ThrottleLever", InputActionType.Value, c.throttleLeverPath);
            stickButtons = Buttons.Create(stickMap, c.stickHandbrakeButtonPath, c.stickStartButtonPath, c.stickRecenterButtonPath, c.stickResetRoverButtonPath);

            PluginInput.ConfigureStick();
            AssignDevices();
            InputSystem.onDeviceChange += OnDeviceChange;
            commonMap.Enable();
            wheelMap.Enable();
            stickMap.Enable();
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            foreach (var m in new[] { commonMap, wheelMap, stickMap })
            {
                m?.Disable();
                m?.Dispose();
            }
            commonMap = wheelMap = stickMap = null;
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is not Joystick) return;
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed ||
                change == InputDeviceChange.Reconnected || change == InputDeviceChange.Disconnected)
                AssignDevices();
        }

        /// <summary>Раскладывает подключённые джойстики по картам «руль» и «самолётный джойстик».</summary>
        void AssignDevices()
        {
            var wheels = new List<InputDevice>();
            var sticks = new List<InputDevice>();
            wheelNames.Clear();
            stickNames.Clear();
            foreach (var d in InputSystem.devices)
            {
                if (d is not Joystick || !d.added) continue;
                bool stick = calibration.IsFlightStick(d);
                (stick ? sticks : wheels).Add(d);
                (stick ? stickNames : wheelNames).Add(d.displayName);
            }
            // Устройства, которые читаются через плагины, показываем под их именами и без дублей.
            pluginWheel = PluginInput.WheelConnected;
            pluginStick = PluginInput.StickConnected;
            if (pluginWheel)
            {
                wheelNames.RemoveAll(n => n.IndexOf("moza", System.StringComparison.OrdinalIgnoreCase) >= 0);
                wheelNames.Insert(0, PluginInput.WheelName);
            }
            if (pluginStick)
            {
                stickNames.RemoveAll(n => n.IndexOf("T.16000", System.StringComparison.OrdinalIgnoreCase) >= 0);
                stickNames.Insert(0, PluginInput.StickName);
            }
            // Пустой список = карта не привязана ни к одному устройству (null означало бы «ко всем»).
            wheelMap.devices = wheels.ToArray();
            stickMap.devices = sticks.ToArray();
            throttleWatch = brakeWatch = leverWatch = default;
        }

        void Update()
        {
            if (commonMap == null) return;
            var c = calibration;
            float dt = Time.deltaTime;

            // Плагины подключаются и отключаются на лету.
            if (pluginWheel != PluginInput.WheelConnected || pluginStick != PluginInput.StickConnected) AssignDevices();
            bool moza = pluginWheel, t16 = pluginStick;

            // Руль
            float wheel = moza ? PluginInput.WheelSteer : ReadAxis(wheelSteer);
            if (c.invertSteer) wheel = -wheel;
            wheel = Mathf.Abs(wheel) < c.steerDeadzone ? 0f : Mathf.Clamp(wheel * c.steerSensitivity, -1f, 1f);

            // Самолётный джойстик: крен/поворот ручки — руль, ручка от себя/на себя — газ/тормоз
            float roll = c.ShapeStickAxis(t16 ? PluginInput.StickRoll : ReadAxis(stickRoll));
            float twist = c.ShapeStickAxis(t16 ? PluginInput.StickTwist : ReadAxis(stickTwist));
            float stickSteer = c.stickSteerAxis switch
            {
                StickSteerAxis.Roll => roll,
                StickSteerAxis.Twist => twist,
                _ => Mathf.Abs(roll) >= Mathf.Abs(twist) ? roll : twist,
            };
            stickSteer = Mathf.Clamp(stickSteer * c.stickSteerSensitivity, -1f, 1f);
            float pitch = c.ShapeStickAxis(t16 ? PluginInput.StickPitch : ReadAxis(stickPitch));
            if (c.invertStickPitch) pitch = -pitch;
            float lever = !c.useThrottleLever ? 0f
                : t16 ? ReadPluginPedal(c.invertThrottleLever ? 1f - PluginInput.StickLever : PluginInput.StickLever, ref leverWatch)
                : ReadPedal(throttleLever, c.throttleLeverRange, ref leverWatch);
            float gas = moza ? ApplyPedalDeadzone(PluginInput.WheelThrottle) : ReadPedal(throttlePedal, c.throttleRange, ref throttleWatch);
            float brake = moza ? ApplyPedalDeadzone(PluginInput.WheelBrake) : ReadPedal(brakePedal, c.brakeRange, ref brakeWatch);

            float alt = altSteer.ReadValue<float>();
            keyboardSteer = Mathf.MoveTowards(keyboardSteer, alt, keyboardSteerSpeed * dt);
            Steer = Strongest(Strongest(wheel, stickSteer), keyboardSteer);

            // Педали
            Throttle = Mathf.Max(Mathf.Max(gas, altThrottle.ReadValue<float>()), Mathf.Max(pitch, lever));
            Brake = Mathf.Max(Mathf.Max(brake, altBrake.ReadValue<float>()), -pitch);

            Handbrake = commonButtons.handbrake.IsPressed()
                || (moza ? PluginInput.WheelHandbrake || PluginInput.WheelButton(c.mozaHandbrakeButton) : wheelButtons.handbrake.IsPressed())
                || (t16 ? PluginInput.StickButton(c.stickHandbrakeButton) : stickButtons.handbrake.IsPressed());
            StartPressed = commonButtons.start.WasPressedThisFrame()
                || (moza ? PluginInput.WheelButtonDown(c.mozaStartButton) : wheelButtons.start.WasPressedThisFrame())
                || (t16 ? PluginInput.StickButtonDown(c.stickStartButton) : stickButtons.start.WasPressedThisFrame());
            RecenterPressed = commonButtons.recenter.WasPressedThisFrame()
                || (moza ? PluginInput.WheelButtonDown(c.mozaRecenterButton) : wheelButtons.recenter.WasPressedThisFrame())
                || (t16 ? PluginInput.StickButtonDown(c.stickRecenterButton) : stickButtons.recenter.WasPressedThisFrame());
            ResetPressed = commonButtons.reset.WasPressedThisFrame()
                || (moza ? PluginInput.WheelButtonDown(c.mozaResetRoverButton) : wheelButtons.reset.WasPressedThisFrame())
                || (t16 ? PluginInput.StickButtonDown(c.stickResetRoverButton) : stickButtons.reset.WasPressedThisFrame());

            if (DebugOverride is Override o)
            {
                Steer = o.steer;
                Throttle = o.throttle;
                Brake = o.brake;
                Handbrake = o.handbrake;
            }
        }

        static float Strongest(float a, float b) => Mathf.Abs(a) >= Mathf.Abs(b) ? a : b;

        static float ReadAxis(InputAction action) => action.controls.Count > 0 ? action.ReadValue<float>() : 0f;

        float ApplyPedalDeadzone(float v) => v < calibration.pedalDeadzone ? 0f : Mathf.Clamp01(v);

        /// <summary>Значение 0..1 из плагина; как и у Input System, учитывается только после первого движения.</summary>
        float ReadPluginPedal(float raw, ref PedalWatch watch) => watch.Update(raw) ? ApplyPedalDeadzone(raw) : 0f;

        float ReadPedal(InputAction action, PedalRange range, ref PedalWatch watch)
        {
            if (action.controls.Count == 0) return 0f;
            float raw = action.ReadValue<float>();
            if (!watch.Update(raw)) return 0f;
            float v = Mathf.Clamp01(InputCalibration.MapPedal(raw, range));
            return v < calibration.pedalDeadzone ? 0f : v;
        }
    }
}
