#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
#define MOZA_AVAILABLE
#endif
using ThrustmasterT16000M;
using UnityEngine;
#if MOZA_AVAILABLE
using MOZA;
#endif

namespace Mehatron.Vehicle
{
    /// <summary>
    /// Мост к плагинам стенда: руль и педали MOZA (MOZA SDK) и джойстик Thrustmaster T.16000M.
    /// Менеджеры плагинов создаются сами и обновляют данные раньше всех скриптов, поэтому читать можно из обычного Update.
    /// Если устройство подключено, <see cref="RoverInput"/> берёт данные отсюда, иначе — через Input System по путям калибровки.
    /// Плагин MOZA работает только в Windows; на других платформах руль всегда «не подключён».
    /// </summary>
    public static class PluginInput
    {
        // ---------------------------------------------------------------- руль и педали MOZA

        public const string WheelName = "MOZA (SDK)";

#if MOZA_AVAILABLE
        public static bool WheelConnected => MozaWheelInput.IsConnected;
        /// <summary>Поворот руля −1..1 (−1 — упор влево); полный ход — половина диапазона из Pit House.</summary>
        public static float WheelSteer => MozaWheelInput.Steering;
        public static float WheelThrottle => MozaWheelInput.Throttle;
        public static float WheelBrake => MozaWheelInput.Brake;
        /// <summary>Ручной тормоз MOZA (кнопка или аналоговый рычаг больше половины хода).</summary>
        public static bool WheelHandbrake => MozaWheelInput.Handbrake;
        public static bool WheelButton(int number) => number > 0 && MozaWheelInput.GetButton(number);
        public static bool WheelButtonDown(int number) => number > 0 && MozaWheelInput.GetButtonDown(number);

        /// <summary>Левая крестовина обода: для навигации по меню.</summary>
        static Vector2Int WheelDpad => MozaWheelInput.LeftRocker switch
        {
            MozaDirection.Up => new Vector2Int(0, 1),
            MozaDirection.UpRight => new Vector2Int(1, 1),
            MozaDirection.Right => new Vector2Int(1, 0),
            MozaDirection.DownRight => new Vector2Int(1, -1),
            MozaDirection.Down => new Vector2Int(0, -1),
            MozaDirection.DownLeft => new Vector2Int(-1, -1),
            MozaDirection.Left => new Vector2Int(-1, 0),
            MozaDirection.UpLeft => new Vector2Int(-1, 1),
            _ => Vector2Int.zero,
        };
#else
        public static bool WheelConnected => false;
        public static float WheelSteer => 0f;
        public static float WheelThrottle => 0f;
        public static float WheelBrake => 0f;
        public static bool WheelHandbrake => false;
        public static bool WheelButton(int number) => false;
        public static bool WheelButtonDown(int number) => false;
        static Vector2Int WheelDpad => Vector2Int.zero;
#endif

        // ---------------------------------------------------------------- джойстик T.16000M

        public static bool StickConnected => T16000MInput.IsConnected;
        public static string StickName => T16000MInput.DeviceName;
        /// <summary>Наклон ручки влево/вправо, −1..1 (вправо — плюс).</summary>
        public static float StickRoll => T16000MInput.StickX;
        /// <summary>Наклон ручки от себя/на себя, −1..1 (от себя — плюс).</summary>
        public static float StickPitch => T16000MInput.StickY;
        /// <summary>Поворот ручки вокруг оси, −1..1 (вправо — плюс).</summary>
        public static float StickTwist => T16000MInput.Twist;
        /// <summary>Ползунок тяги, 0..1 (от себя — 1).</summary>
        public static float StickLever => T16000MInput.Throttle;
        public static bool StickButton(int number) => T16000MInput.GetButton(number);
        public static bool StickButtonDown(int number) => T16000MInput.GetButtonDown(number);

        /// <summary>Мёртвую зону и кривую ручки задаёт калибровка игры, поэтому собственные зоны плагина выключены.</summary>
        public static void ConfigureStick()
        {
            T16000MInput.StickDeadzone = 0f;
            T16000MInput.TwistDeadzone = 0f;
        }

        // ---------------------------------------------------------------- навигация по меню

        /// <summary>Крестовина обода MOZA или хатка джойстика: x — вправо, y — вверх; нули, если ничего не нажато.</summary>
        public static Vector2Int Dpad
        {
            get
            {
                var hat = T16000MInput.IsConnected ? Vector2Int.RoundToInt(T16000MInput.HatVector) : Vector2Int.zero;
                return hat != Vector2Int.zero ? hat : WheelConnected ? WheelDpad : Vector2Int.zero;
            }
        }
    }
}
