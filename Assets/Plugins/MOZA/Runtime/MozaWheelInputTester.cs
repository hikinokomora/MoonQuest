using System.Collections.Generic;
using System.Text;
using mozaAPI;
using UnityEngine;

namespace MOZA
{
    /// <summary>
    /// Проверка руля MOZA: живое состояние руля, педалей, всех кнопок,
    /// крестовин, энкодеров и селекторов поверх экрана.
    ///
    /// Как пользоваться: добавьте компонент на любой объект сцены, запустите
    /// сцену и по очереди нажмите каждую кнопку обода. Оверлей покажет её номер
    /// и имя. Внизу копится список кнопок, сработавших за сессию: если какой-то
    /// кнопки в нём нет, она не читается.
    /// </summary>
    [AddComponentMenu("MOZA/MOZA Wheel Input Tester")]
    public sealed class MozaWheelInputTester : MonoBehaviour
    {
        [Tooltip("Показывать оверлей поверх игры.")]
        [SerializeField] private bool showOverlay = true;

        [Tooltip("Писать каждое нажатие и отпускание в консоль.")]
        [SerializeField] private bool logToConsole = true;

        private readonly SortedSet<int> _seen = new SortedSet<int>();
        private readonly StringBuilder _sb = new StringBuilder();
        private GUIStyle _style;

        private void OnEnable()
        {
            MozaWheelInput.OnButtonDown += HandleDown;
            MozaWheelInput.OnButtonUp += HandleUp;
        }

        private void OnDisable()
        {
            MozaWheelInput.OnButtonDown -= HandleDown;
            MozaWheelInput.OnButtonUp -= HandleUp;
        }

        private void HandleDown(int number)
        {
            _seen.Add(number);

            if (logToConsole)
            {
                Debug.Log($"[MOZA] нажата кнопка {number} ({Name(number)})");
            }
        }

        private void HandleUp(int number)
        {
            if (logToConsole)
            {
                Debug.Log($"[MOZA] отпущена кнопка {number} ({Name(number)})");
            }
        }

        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, wordWrap = true };

            _sb.Clear();
            _sb.Append(MozaWheelInput.IsConnected ? "<b>MOZA: подключён</b>" : "<b>MOZA: НЕ подключён</b>");
            _sb.Append("   код SDK: ").Append(MozaWheelInput.LastError).Append(ErrorHint(MozaWheelInput.LastError));
            _sb.AppendLine();

            _sb.Append("Руль ").Append(MozaWheelInput.Steering.ToString("+0.00;-0.00"))
               .Append(" (").Append(MozaWheelInput.SteeringAngleDegrees.ToString("0")).Append("° из ±")
               .Append((MozaWheelInput.SteeringRangeDegrees * 0.5f).ToString("0")).Append("°)")
               .Append("   скорость ").Append(MozaWheelInput.SteeringVelocity.ToString("0")).Append("°/с")
               .AppendLine();

            _sb.Append("Газ ").Append(MozaWheelInput.Throttle.ToString("0.00"))
               .Append("   Тормоз ").Append(MozaWheelInput.Brake.ToString("0.00"))
               .Append("   Сцепление ").Append(MozaWheelInput.Clutch.ToString("0.00"))
               .Append("   Ручник ").Append(MozaWheelInput.HandbrakeAxis.ToString("0.00"))
               .Append(MozaWheelInput.Handbrake ? " (затянут)" : string.Empty)
               .Append("   Передача ").Append(GearName(MozaWheelInput.Gear))
               .AppendLine();

            _sb.Append("Лепестки сцепления ").Append(MozaWheelInput.ClutchPaddle.ToString("0.00"))
               .Append(" (L ").Append(MozaWheelInput.ClutchPaddleLeft.ToString("0.00"))
               .Append(" / R ").Append(MozaWheelInput.ClutchPaddleRight.ToString("0.00")).Append(')')
               .AppendLine();

            _sb.Append("Крестовины: левая ").Append(MozaWheelInput.LeftRocker)
               .Append(", правая ").Append(MozaWheelInput.RightRocker)
               .Append("   Энкодеры: L ").Append(MozaWheelInput.LeftKnobPosition)
               .Append(", R ").Append(MozaWheelInput.RightKnobPosition)
               .Append("   Селекторы:");

            for (int i = 0; i < MozaWheelInput.SelectorCount; i++)
            {
                _sb.Append(' ').Append(MozaWheelInput.GetSelector(i));
            }

            _sb.AppendLine().AppendLine().Append("<b>Зажаты сейчас:</b> ");

            bool any = false;

            for (int i = 1; i < MozaWheelInput.MaxButtons; i++)
            {
                if (!MozaWheelInput.GetButton(i))
                {
                    continue;
                }

                _sb.Append(i).Append(" (").Append(Name(i)).Append(")   ");
                any = true;
            }

            _sb.AppendLine(any ? string.Empty : "—");
            _sb.AppendLine().Append("<b>Сработали за сессию (").Append(_seen.Count).Append("):</b> ");

            foreach (int number in _seen)
            {
                _sb.Append(number).Append(' ');
            }

            GUI.Label(new Rect(12f, 12f, Screen.width - 24f, Screen.height - 24f), _sb.ToString(), _style);
        }

        private static string ErrorHint(ERRORCODE error)
        {
            switch (error)
            {
                case ERRORCODE.PITHOUSENOTREADY: return " — запустите MOZA Pit House";
                case ERRORCODE.NODEVICES: return " — руль не найден, проверьте подключение базы";
                case ERRORCODE.NOINSTALLSDK: return " — SDK не запущен, см. ошибки в консоли";
                case ERRORCODE.FIRMWARETOOOLD: return " — обновите прошивку в Pit House";
                default: return string.Empty;
            }
        }

        private static string GearName(int gear) => gear < 0 ? "R" : gear == 0 ? "N" : gear.ToString();

        private static string Name(int number) =>
            System.Enum.IsDefined(typeof(MozaButton), number) ? ((MozaButton)number).ToString() : "без имени";
    }
}
