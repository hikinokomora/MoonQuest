using System;
using mozaAPI;

namespace MOZA
{
    /// <summary>
    /// Полное состояние руля MOZA: руль, педали, ручник, рычаг КПП, все кнопки
    /// обода, крестовины, энкодеры, многопозиционные селекторы и лепестки.
    ///
    /// Данные заполняет <see cref="MozaWheelManager"/> раз в кадр, раньше всех
    /// остальных скриптов (DefaultExecutionOrder = -1000), поэтому читать
    /// отсюда можно из обычного Update. Менеджер создаётся сам при запуске
    /// сцены — добавлять его на объект не нужно.
    ///
    /// Пример:
    /// <code>
    /// float steer = MozaWheelInput.Steering;                         // -1..1
    /// float gas = MozaWheelInput.Throttle;                           // 0..1
    /// if (MozaWheelInput.GetButtonDown(MozaButton.RightPaddle)) GearUp();
    /// if (MozaWheelInput.GetButton(47)) DoSomething();               // любая кнопка по номеру
    /// </code>
    /// </summary>
    public static class MozaWheelInput
    {
        /// <summary>Размер таблицы кнопок. HIDData.buttons в SDK — 128 элементов, номера 1..127.</summary>
        public const int MaxButtons = 128;

        /// <summary>Количество многопозиционных селекторов в HIDData.</summary>
        public const int SelectorCount = 5;

        private static readonly bool[] _level = new bool[MaxButtons];
        private static readonly bool[] _down = new bool[MaxButtons];
        private static readonly bool[] _up = new bool[MaxButtons];
        private static readonly int[] _pressCount = new int[MaxButtons];
        private static readonly int[] _selectors = new int[SelectorCount];

        // ---------- подключение ----------

        /// <summary>Руль подключён и данные в этом кадре валидны.</summary>
        public static bool IsConnected { get; private set; }

        /// <summary>Руль подключился (или SDK впервые получил от него данные).</summary>
        public static event Action OnConnected;

        /// <summary>Руль отключился или перестал отвечать.</summary>
        public static event Action OnDisconnected;

        /// <summary>
        /// Код последнего опроса SDK. NORMAL — всё в порядке; PITHOUSENOTREADY —
        /// не запущен MOZA Pit House; NODEVICES — руль не найден;
        /// COLLECTIONCYCLEDATALOSS — кадр был слишком долгим и часть щелчков
        /// потерялась (руль при этом остаётся подключённым).
        /// </summary>
        public static ERRORCODE LastError { get; private set; } = ERRORCODE.NOINSTALLSDK;

        /// <summary>
        /// Необработанные данные SDK за последний опрос. Нужны, только если
        /// требуется что-то, чего нет в свойствах этого класса.
        /// </summary>
        public static HIDData RawData { get; private set; }

        // ---------- руль ----------

        /// <summary>Поворот руля, -1..1: -1 — упор влево, 1 — упор вправо.</summary>
        public static float Steering { get; private set; }

        /// <summary>Угол поворота руля от центра, градусы. Знак совпадает с <see cref="Steering"/>.</summary>
        public static float SteeringAngleDegrees { get; private set; }

        /// <summary>Полный диапазон руля «упор-упор», градусы, — как настроено в Pit House.</summary>
        public static float SteeringRangeDegrees { get; private set; } = 900f;

        /// <summary>Скорость вращения руля, град/с (прошивка базы 1.2.4.x и новее, иначе 0).</summary>
        public static float SteeringVelocity { get; private set; }

        /// <summary>Ускорение вращения руля, град/с² (прошивка базы 1.2.4.x и новее, иначе 0).</summary>
        public static float SteeringAcceleration { get; private set; }

        /// <summary>Ось руля как её отдаёт база, -32767..32767.</summary>
        public static short SteeringAxisRaw { get; private set; }

        // ---------- педали, ручник, КПП ----------

        /// <summary>Педаль газа, 0..1.</summary>
        public static float Throttle { get; private set; }

        /// <summary>Педаль тормоза, 0..1.</summary>
        public static float Brake { get; private set; }

        /// <summary>Педаль сцепления, 0..1.</summary>
        public static float Clutch { get; private set; }

        /// <summary>Аналоговый ручной тормоз MOZA, 0..1.</summary>
        public static float HandbrakeAxis { get; private set; }

        /// <summary>Ручной тормоз затянут: кнопочный режим ручника или ось больше половины хода.</summary>
        public static bool Handbrake { get; private set; }

        /// <summary>Передача рычага КПП MOZA: -1 = R, 0 = нейтраль, 1..7.</summary>
        public static int Gear { get; private set; }

        /// <summary>
        /// Аналоговые лепестки сцепления на ободе, совмещённая ось, 0..1.
        /// Есть не у всех ободов, у остальных всегда 0. Это НЕ лепестки
        /// переключения передач — те являются кнопками 13 и 14.
        /// </summary>
        public static float ClutchPaddle { get; private set; }

        /// <summary>Левый лепесток сцепления в раздельном режиме, 0..1.</summary>
        public static float ClutchPaddleLeft { get; private set; }

        /// <summary>Правый лепесток сцепления в раздельном режиме, 0..1.</summary>
        public static float ClutchPaddleRight { get; private set; }

        // ---------- крестовины и энкодеры ----------

        /// <summary>
        /// Левая крестовина (кнопки 5-8) как направление. Кнопки 5-8 работают
        /// в любом режиме крестовины, выбранном в Pit House.
        /// </summary>
        public static MozaDirection LeftRocker { get; private set; }

        /// <summary>Правая крестовина (кнопки 9-12) как направление.</summary>
        public static MozaDirection RightRocker { get; private set; }

        /// <summary>Левый энкодер (кнопки 45-46): щелчков за кадр, знак — сторона.</summary>
        public static int LeftKnobDelta { get; private set; }

        /// <summary>Правый энкодер (кнопки 47-48): щелчков за кадр, знак — сторона.</summary>
        public static int RightKnobDelta { get; private set; }

        /// <summary>Левый энкодер: сумма щелчков с момента запуска.</summary>
        public static int LeftKnobPosition { get; private set; }

        /// <summary>Правый энкодер: сумма щелчков с момента запуска.</summary>
        public static int RightKnobPosition { get; private set; }

        /// <summary>
        /// Последняя выбранная позиция многопозиционного селектора.
        /// Индекс 0..4 — в порядке полей HIDData: 26_27or53_64, 28_29or65_76,
        /// 30_31or77_88, 39_40or89_100, 43_44or101_112.
        /// </summary>
        public static int GetSelector(int index) =>
            index >= 0 && index < SelectorCount ? _selectors[index] : 0;

        // ---------- кнопки ----------

        /// <summary>Нажатие любой кнопки в этом кадре; аргумент — номер кнопки.</summary>
        public static event Action<int> OnButtonDown;

        /// <summary>Отпускание любой кнопки в этом кадре; аргумент — номер кнопки.</summary>
        public static event Action<int> OnButtonUp;

        /// <summary>Кнопка удерживается прямо сейчас.</summary>
        public static bool GetButton(int number) => IsValid(number) && _level[number];

        /// <summary>Кнопку нажали в этом кадре.</summary>
        public static bool GetButtonDown(int number) => IsValid(number) && _down[number];

        /// <summary>Кнопку отпустили в этом кадре.</summary>
        public static bool GetButtonUp(int number) => IsValid(number) && _up[number];

        /// <summary>
        /// Сколько раз кнопку нажали с прошлого кадра. Обычно 0 или 1, но
        /// учитывает и очень быстрые нажатия, уложившиеся между двумя кадрами.
        /// </summary>
        public static int GetPressCount(int number) => IsValid(number) ? _pressCount[number] : 0;

        public static bool GetButton(MozaButton button) => GetButton((int)button);
        public static bool GetButtonDown(MozaButton button) => GetButtonDown((int)button);
        public static bool GetButtonUp(MozaButton button) => GetButtonUp((int)button);
        public static int GetPressCount(MozaButton button) => GetPressCount((int)button);

        /// <summary>Номер любой кнопки, нажатой в этом кадре, иначе 0. Удобно для экрана переназначения.</summary>
        public static int GetAnyButtonDown()
        {
            for (int i = 1; i < MaxButtons; i++)
            {
                if (_down[i])
                {
                    return i;
                }
            }

            return 0;
        }

        private static bool IsValid(int number) => number > 0 && number < MaxButtons;

        // ---------- заполнение (вызывает MozaWheelManager) ----------

        internal static void BeginFrame(HIDData raw)
        {
            RawData = raw;
            Array.Clear(_down, 0, MaxButtons);
            Array.Clear(_up, 0, MaxButtons);
            Array.Clear(_pressCount, 0, MaxButtons);
            LeftKnobDelta = 0;
            RightKnobDelta = 0;
        }

        internal static void SetButton(int number, bool level, int presses, int releases)
        {
            if (!IsValid(number))
            {
                return;
            }

            bool previous = _level[number];

            // presses/releases ловят нажатие, целиком уместившееся между двумя
            // опросами: уровень при этом мог и не измениться.
            bool down = presses > 0 || (level && !previous);
            bool up = releases > 0 || (previous && !level);

            _level[number] = level;
            _down[number] = down;
            _up[number] = up;
            _pressCount[number] = Math.Max(presses, down ? 1 : 0);

            if (down)
            {
                OnButtonDown?.Invoke(number);
            }

            if (up)
            {
                OnButtonUp?.Invoke(number);
            }
        }

        internal static void SetSteering(float normalized, float angleDegrees, float rangeDegrees,
            float velocity, float acceleration, short raw)
        {
            Steering = normalized;
            SteeringAngleDegrees = angleDegrees;
            SteeringVelocity = velocity;
            SteeringAcceleration = acceleration;
            SteeringAxisRaw = raw;

            if (rangeDegrees > 0f)
            {
                SteeringRangeDegrees = rangeDegrees;
            }
        }

        internal static void SetPedals(float throttle, float brake, float clutch, float handbrakeAxis,
            bool handbrake, int gear)
        {
            Throttle = throttle;
            Brake = brake;
            Clutch = clutch;
            HandbrakeAxis = handbrakeAxis;
            Handbrake = handbrake;
            Gear = gear;
        }

        internal static void SetClutchPaddles(float combined, float left, float right)
        {
            ClutchPaddle = combined;
            ClutchPaddleLeft = left;
            ClutchPaddleRight = right;
        }

        internal static void SetRockers(MozaDirection left, MozaDirection right)
        {
            LeftRocker = left;
            RightRocker = right;
        }

        internal static void SetKnobs(int leftDelta, int rightDelta)
        {
            LeftKnobDelta = leftDelta;
            RightKnobDelta = rightDelta;
            LeftKnobPosition += leftDelta;
            RightKnobPosition += rightDelta;
        }

        internal static void SetSelector(int index, int value)
        {
            if (index >= 0 && index < SelectorCount)
            {
                _selectors[index] = value;
            }
        }

        internal static void SetError(ERRORCODE error) => LastError = error;

        internal static void SetConnected(bool connected)
        {
            if (IsConnected == connected)
            {
                return;
            }

            IsConnected = connected;

            if (connected)
            {
                OnConnected?.Invoke();
                return;
            }

            Reset();
            OnDisconnected?.Invoke();
        }

        /// <summary>Гасит все состояния при отключении руля.</summary>
        private static void Reset()
        {
            for (int i = 0; i < MaxButtons; i++)
            {
                _down[i] = false;
                _pressCount[i] = 0;
                _up[i] = _level[i];

                // Отпускание отправляем честно, иначе подписчики останутся
                // думать, что кнопка всё ещё зажата.
                if (_level[i])
                {
                    _level[i] = false;
                    OnButtonUp?.Invoke(i);
                }
            }

            Array.Clear(_selectors, 0, SelectorCount);
            RawData = default;
            Steering = SteeringAngleDegrees = SteeringVelocity = SteeringAcceleration = 0f;
            SteeringAxisRaw = 0;
            Throttle = Brake = Clutch = HandbrakeAxis = 0f;
            Handbrake = false;
            Gear = 0;
            ClutchPaddle = ClutchPaddleLeft = ClutchPaddleRight = 0f;
            LeftRocker = RightRocker = MozaDirection.None;
            LeftKnobDelta = RightKnobDelta = 0;
        }
    }
}
