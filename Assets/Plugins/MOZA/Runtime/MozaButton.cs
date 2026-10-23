namespace MOZA
{
    /// <summary>
    /// Номера кнопок обода MOZA — те же, что подписаны на официальной схеме
    /// руля и показаны в MOZA Pit House. Значение enum ЕСТЬ номер кнопки,
    /// поэтому <c>(int)MozaButton.Start == 36</c>.
    ///
    /// Нумерация начинается с 1, как в MOZA SDK: официальный пример перебирает
    /// <c>for (int i = 1; i &lt; 113; i++) d-&gt;buttons[i]</c>, а имена полей
    /// HIDData прямо кодируют номера (leftRocker5_8, knobL45_46 и т.д.).
    ///
    /// Имена даны по схеме обода ES. У других ободов часть номеров подписана
    /// иначе или отсутствует, но читаются ВСЕ кнопки без исключения — по номеру:
    /// <c>MozaWheelInput.GetButton(47)</c>. Какой номер у конкретной кнопки,
    /// показывает <see cref="MozaWheelInputTester"/>.
    /// </summary>
    public enum MozaButton
    {
        None = 0,

        // Правый ромб.
        A = 1,  // низ
        B = 2,  // право
        X = 3,  // лево
        Y = 4,  // верх

        // Левая крестовина. Та же четвёрка приходит ещё и как направление —
        // см. MozaWheelInput.LeftRocker.
        DpadUp = 5,
        DpadRight = 6,
        DpadDown = 7,
        DpadLeft = 8,

        // Правая крестовина (есть не на всех ободах).
        RightDpadUp = 9,
        RightDpadRight = 10,
        RightDpadDown = 11,
        RightDpadLeft = 12,

        // Подрулевые лепестки переключения передач.
        LeftPaddle = 13,
        RightPaddle = 14,

        // Левый верхний блок.
        N = 19,
        Wip = 20,
        FL = 21,

        // Левый нижний блок.
        Cam = 22,
        Radio = 23,
        S1 = 24,
        Home = 25,

        // Правый верхний блок.
        P = 32,
        Box = 33,
        PL = 34,

        // Правый нижний блок.
        R = 35,
        Start = 36,
        S2 = 37,
        Menu = 38,
    }

    /// <summary>Направление крестовины.</summary>
    public enum MozaDirection
    {
        None = 0,
        Up,
        UpRight,
        Right,
        DownRight,
        Down,
        DownLeft,
        Left,
        UpLeft,
    }
}
