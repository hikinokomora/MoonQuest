using System;
using mozaAPI;
using UnityEngine;

namespace MOZA
{
    /// <summary>
    /// Опрашивает руль MOZA через MOZA SDK и раскладывает данные в
    /// <see cref="MozaWheelInput"/>.
    ///
    /// Создаётся автоматически перед загрузкой первой сцены и живёт до выхода
    /// из игры, поэтому добавлять его на объект не нужно.
    ///
    /// Для работы на компьютере должен быть установлен и запущен MOZA Pit House:
    /// SDK получает данные руля через него.
    ///
    /// DefaultExecutionOrder = -1000: менеджер отрабатывает раньше всех
    /// скриптов, читающих MozaWheelInput, иначе GetButtonDown у них опаздывал
    /// бы на кадр.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("")]
    public sealed class MozaWheelManager : MonoBehaviour
    {
        /// <summary>
        /// Создавать менеджер автоматически. Если руль MOZA в проекте нужен
        /// не всегда, выключите флаг из статического конструктора своего кода
        /// и добавьте компонент на объект вручную.
        /// </summary>
        public static bool AutoCreate = true;

        private static MozaWheelManager _instance;

        private bool _sdkInstalled;
        private float _rangeDegrees = 900f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateOnLoad()
        {
            if (!AutoCreate || _instance != null)
            {
                return;
            }

            var go = new GameObject("MOZA Wheel Manager");
            go.AddComponent<MozaWheelManager>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            try
            {
                mozaAPI.mozaAPI.installMozaSDK();
                _sdkInstalled = true;
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogError("[MOZA] Не найдены библиотеки MOZA SDK (MOZA_API_C.dll, MOZA_SDK.dll): " + ex.Message);
                return;
            }
            catch (Exception ex)
            {
                Debug.LogError("[MOZA] Не удалось запустить MOZA SDK: " + ex.Message);
                return;
            }

            ReadSteeringRange();
        }

        private void ReadSteeringRange()
        {
            var error = ERRORCODE.NORMAL;
            var limit = mozaAPI.mozaAPI.getMotorLimitAngle(ref error);

            if (error == ERRORCODE.NORMAL && limit != null && limit.Item2 != 0)
            {
                _rangeDegrees = Math.Abs(limit.Item2);
            }
        }

        private void Update()
        {
            if (!_sdkInstalled)
            {
                return;
            }

            HIDData data;
            var error = ERRORCODE.NORMAL;

            try
            {
                data = mozaAPI.mozaAPI.getHIDData(ref error);
            }
            catch (Exception)
            {
                MozaWheelInput.SetError(ERRORCODE.NODEVICES);
                MozaWheelInput.SetConnected(false);
                return;
            }

            MozaWheelInput.SetError(error);

            // COLLECTIONCYCLEDATALOSS — кадр был слишком долгим (загрузка сцены,
            // пауза отладчика), и кольцевой буфер SDK переполнился. Часть щелчков
            // потеряна, но оси и состояние кнопок валидны: руль НЕ отключён.
            if (error != ERRORCODE.NORMAL && error != ERRORCODE.COLLECTIONCYCLEDATALOSS)
            {
                MozaWheelInput.SetConnected(false);
                return;
            }

            if (!MozaWheelInput.IsConnected)
            {
                // Диапазон руля мог поменяться в Pit House, пока руль был отключён.
                ReadSteeringRange();
            }

            MozaWheelInput.BeginFrame(data);

            ReadAxes(data);
            ReadButtons(data);
            ReadKnobs(data);

            // Событие подключения — после разбора, чтобы подписчики сразу
            // видели настоящие значения, а не нули.
            MozaWheelInput.SetConnected(true);
        }

        private void ReadAxes(HIDData data)
        {
            float angle = float.IsNaN(data.fSteeringWheelAngle) ? 0f : data.fSteeringWheelAngle;
            float steering = float.IsNaN(data.fSteeringWheelAngle)
                ? data.steeringWheelAxle / 32767f
                : angle / (_rangeDegrees * 0.5f);

            MozaWheelInput.SetSteering(
                Mathf.Clamp(steering, -1f, 1f),
                angle,
                _rangeDegrees,
                float.IsNaN(data.fSteeringWheelVelocity) ? 0f : data.fSteeringWheelVelocity,
                float.IsNaN(data.fSteeringWheelAcceleration) ? 0f : data.fSteeringWheelAcceleration,
                data.steeringWheelAxle);

            float handbrakeAxis = Axis01(data.handbrake);

            MozaWheelInput.SetPedals(
                Axis01(data.throttle),
                Axis01(data.brake),
                Axis01(data.clutch),
                handbrakeAxis,
                data.buttonHandbrake || handbrakeAxis > 0.5f,
                ToGear(data.shift));

            MozaWheelInput.SetClutchPaddles(
                Axis01(data.clutchSynthesisShaft),
                Axis01(data.clutchIndependentShaftL),
                Axis01(data.clutchIndependentShaftR));
        }

        /// <summary>
        /// Кнопки. SDK отдаёт по каждой кнопке состояние на начало цикла опроса
        /// (startValue) и число изменений внутри цикла (changeNum, -1 — ни одного).
        /// Нажатия и отпускания считаем сами: встроенный PressNum() возвращает
        /// половину числа изменений с округлением вверх, то есть 1 и при
        /// отпускании кнопки.
        /// </summary>
        private static void ReadButtons(HIDData data)
        {
            var left = ToDirection(data.leftRocker5_8.LastDir());
            var right = ToDirection(data.rightRocker9_12.LastDir());
            MozaWheelInput.SetRockers(left, right);

            if (data.buttons == null)
            {
                return;
            }

            int last = Math.Min(data.buttons.Length, MozaWheelInput.MaxButtons);

            for (int number = 1; number < last; number++)
            {
                HIDButton button = data.buttons[number];
                bool level = button.LastPressState();

                int changes = Math.Max(button.changeNum, 0);
                int odd = (changes + 1) / 2;
                int even = changes / 2;
                int presses = button.startValue ? even : odd;
                int releases = button.startValue ? odd : even;

                // Если крестовина в Pit House переведена в режим hat switch,
                // кнопки 5-12 в buttons[] молчат, а направление приходит только
                // в leftRocker/rightRocker. Сводим его обратно в кнопки.
                if (number >= 5 && number <= 12)
                {
                    level |= RockerHolds(number, left, right);
                }

                MozaWheelInput.SetButton(number, level, presses, releases);
            }
        }

        private static void ReadKnobs(HIDData data)
        {
            // GetOffset() — сумма щелчков энкодера за цикл опроса со знаком.
            MozaWheelInput.SetKnobs(data.knobL45_46.GetOffset(), data.knobR47_48.GetOffset());

            MozaWheelInput.SetSelector(0, data.multiSegmentKnob26_27or53_64.GetLastKey());
            MozaWheelInput.SetSelector(1, data.multiSegmentKnob28_29or65_76.GetLastKey());
            MozaWheelInput.SetSelector(2, data.multiSegmentKnob30_31or77_88.GetLastKey());
            MozaWheelInput.SetSelector(3, data.multiSegmentKnob39_40or89_100.GetLastKey());
            MozaWheelInput.SetSelector(4, data.multiSegmentKnob43_44or101_112.GetLastKey());
        }

        /// <summary>Ось int16 (-32768..32767) в диапазон 0..1.</summary>
        private static float Axis01(short raw) => Mathf.Clamp01((raw + 32768f) / 65535f);

        private static int ToGear(GEAR gear)
        {
            switch (gear)
            {
                case GEAR.R: return -1;
                case GEAR.GEAR1st: return 1;
                case GEAR.GEAR2nd: return 2;
                case GEAR.GEAR3rd: return 3;
                case GEAR.GEAR4th: return 4;
                case GEAR.GEAR5th: return 5;
                case GEAR.GEAR6th: return 6;
                case GEAR.GEAR7th: return 7;
                default: return 0;
            }
        }

        private static bool RockerHolds(int number, MozaDirection left, MozaDirection right)
        {
            MozaDirection dir = number <= 8 ? left : right;

            switch ((number - 5) % 4) // 0 = верх, 1 = право, 2 = низ, 3 = лево
            {
                case 0: return dir == MozaDirection.Up || dir == MozaDirection.UpLeft || dir == MozaDirection.UpRight;
                case 1: return dir == MozaDirection.Right || dir == MozaDirection.UpRight || dir == MozaDirection.DownRight;
                case 2: return dir == MozaDirection.Down || dir == MozaDirection.DownLeft || dir == MozaDirection.DownRight;
                default: return dir == MozaDirection.Left || dir == MozaDirection.UpLeft || dir == MozaDirection.DownLeft;
            }
        }

        // Имена в SDK с опечатками (DOWM) — так в оригинале.
        private static MozaDirection ToDirection(ROCKEREDIR dir)
        {
            switch (dir)
            {
                case ROCKEREDIR.UP: return MozaDirection.Up;
                case ROCKEREDIR.RIGHTUP: return MozaDirection.UpRight;
                case ROCKEREDIR.RIGHT: return MozaDirection.Right;
                case ROCKEREDIR.RIGHTDOWM: return MozaDirection.DownRight;
                case ROCKEREDIR.DOWM: return MozaDirection.Down;
                case ROCKEREDIR.LEFTDOWM: return MozaDirection.DownLeft;
                case ROCKEREDIR.LEFT: return MozaDirection.Left;
                case ROCKEREDIR.LEFTUP: return MozaDirection.UpLeft;
                default: return MozaDirection.None;
            }
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            _instance = null;
            MozaWheelInput.SetConnected(false);

            if (!_sdkInstalled)
            {
                return;
            }

            try
            {
                mozaAPI.mozaAPI.removeMozaSDK();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MOZA] Ошибка при выгрузке MOZA SDK: " + ex.Message);
            }
        }
    }
}
