using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mehatron.Vehicle
{
    public enum PedalRange
    {
        [Tooltip("Отпущена = 0, нажата = 1")] ZeroToOne,
        [Tooltip("Отпущена = -1, нажата = 1")] MinusOneToOne,
        [Tooltip("Отпущена = 1, нажата = -1")] OneToMinusOne,
        [Tooltip("Отпущена = 1, нажата = 0")] OneToZero,
    }

    public enum StickSteerAxis
    {
        [Tooltip("Наклон ручки влево/вправо")] Roll,
        [Tooltip("Поворот ручки вокруг оси")] Twist,
        [Tooltip("Что отклонено сильнее: наклон или поворот")] RollOrTwist,
    }

    public enum JoystickRole { Wheel, FlightStick }

    /// <summary>
    /// Привязки руля, педалей и самолётного джойстика. Пути смотрите в Window → Analysis → Input Debugger.
    /// Руль и самолётный джойстик для Input System — оба <c>Joystick</c>, поэтому устройство относится к джойстику
    /// по названию (<see cref="flightStickNames"/>), остальные считаются рулём. Клавиатура и геймпад работают всегда.
    /// </summary>
    [CreateAssetMenu(menuName = "Mehatron/Input Calibration", fileName = "InputCalibration")]
    public class InputCalibration : ScriptableObject
    {
        [Header("Руль")]
        public string steerPath = "<Joystick>/stick/x";
        public bool invertSteer;
        [Tooltip("Множитель руля: руль на 900° удобнее проходить не до упора. 2 = полный поворот колёс на половине хода.")]
        public float steerSensitivity = 2f;
        [Range(0f, 0.2f)] public float steerDeadzone = 0.01f;

        [Header("Педали")]
        public string throttlePath = "<Joystick>/z";
        public PedalRange throttleRange = PedalRange.MinusOneToOne;
        public string brakePath = "<Joystick>/rz";
        public PedalRange brakeRange = PedalRange.MinusOneToOne;
        [Range(0f, 0.2f)] public float pedalDeadzone = 0.05f;

        [Header("Кнопки руля")]
        public string startButtonPath = "<Joystick>/trigger";
        public string handbrakeButtonPath = "<Joystick>/button2";
        public string recenterButtonPath = "<Joystick>/button3";
        public string resetRoverButtonPath = "<Joystick>/button4";

        [Header("Руль MOZA (плагин MOZA SDK; номера кнопок как в MozaButton: 1 A, 2 B, 3 X, 4 Y; 0 — не использовать)")]
        public int mozaStartButton = 1;
        public int mozaHandbrakeButton = 2;
        public int mozaRecenterButton = 3;
        public int mozaResetRoverButton = 4;

        [Header("Самолётный джойстик")]
        [Tooltip("Части названия устройства (без учёта регистра), по которым джойстик считается самолётным, а не рулём.")]
        public string[] flightStickNames =
        {
            "T.16000", "T.Flight", "TWCS", "Warthog", "HOTAS", "Extreme 3D", "X52", "X56", "X-56",
            "VKB", "VIRPIL", "Gladiator", "Flight",
        };
        [Tooltip("Кем считать джойстик, название которого не подошло ни под один шаблон.")]
        public JoystickRole unknownJoystickRole = JoystickRole.Wheel;
        public StickSteerAxis stickSteerAxis = StickSteerAxis.Roll;
        public string stickRollPath = "<Joystick>/stick/x";
        public string stickTwistPath = "<Joystick>/{Twist}";
        [Tooltip("Ручка от себя — газ, на себя — тормоз (на месте — задний ход).")]
        public string stickPitchPath = "<Joystick>/stick/y";
        public bool invertStickPitch;
        [Range(0f, 0.3f)] public float stickDeadzone = 0.08f;
        [Tooltip("0 — линейно, 1 — кубическая кривая: точнее около центра.")]
        [Range(0f, 1f)] public float stickExpo = 0.35f;
        public float stickSteerSensitivity = 1f;
        [Tooltip("РУД: если сдвинуть, работает как постоянный газ. У разных моделей это slider или z.")]
        public bool useThrottleLever = true;
        public string throttleLeverPath = "<Joystick>/slider";
        public PedalRange throttleLeverRange = PedalRange.OneToMinusOne;
        [Tooltip("Инвертировать ползунок тяги T.16000M, если «от себя» должно быть нулём.")]
        public bool invertThrottleLever;
        [Tooltip("Кнопки T.16000M (плагин Thrustmaster; номера 1..16, 1 — курок; 0 — не использовать).")]
        public int stickStartButton = 1;
        public int stickHandbrakeButton = 2;
        public int stickRecenterButton = 3;
        public int stickResetRoverButton = 4;
        public string stickStartButtonPath = "<Joystick>/trigger";
        public string stickHandbrakeButtonPath = "<Joystick>/button2";
        public string stickRecenterButtonPath = "<Joystick>/button3";
        public string stickResetRoverButtonPath = "<Joystick>/button4";

        public bool IsFlightStick(InputDevice device)
        {
            if (device is not Joystick) return false;
            var d = device.description;
            string text = $"{d.product} {d.manufacturer} {device.displayName}";
            foreach (var n in flightStickNames)
                if (!string.IsNullOrWhiteSpace(n) && text.IndexOf(n.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return unknownJoystickRole == JoystickRole.FlightStick;
        }

        /// <summary>Мёртвая зона с перемасштабированием и экспо-кривая для оси ручки.</summary>
        public float ShapeStickAxis(float v)
        {
            float a = Mathf.Abs(v);
            if (a <= stickDeadzone) return 0f;
            a = Mathf.Clamp01((a - stickDeadzone) / (1f - stickDeadzone));
            a = (1f - stickExpo) * a + stickExpo * a * a * a;
            return Mathf.Sign(v) * a;
        }

        public static float MapPedal(float raw, PedalRange range) => range switch
        {
            PedalRange.ZeroToOne => raw,
            PedalRange.MinusOneToOne => (raw + 1f) * 0.5f,
            PedalRange.OneToMinusOne => (1f - raw) * 0.5f,
            PedalRange.OneToZero => 1f - raw,
            _ => raw,
        };
    }
}
