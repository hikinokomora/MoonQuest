using Mehatron.Gameplay;
using Mehatron.Haptics;
using Mehatron.Motion;
using Mehatron.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mehatron.UI
{
    /// <summary>
    /// Панель оператора в десктопном окне (в шлеме её не видно): состояние игры, горячие клавиши, аварийная остановка.
    /// Отладочные платформа и жилет рисуют свои блоки сами, когда <see cref="Visible"/> включён.
    /// </summary>
    public class OperatorPanel : MonoBehaviour
    {
        public static bool Visible = true;
        /// <summary>Рисовать ли панель сейчас: пока открыто игровое меню, панель прячется (горячие клавиши работают).</summary>
        public static bool Draw => Visible && !Menu.GameMenu.IsOpen;

        [SerializeField] GameFlow gameFlow;
        [SerializeField] MotionCueing motion;
        [SerializeField] RoverController rover;
        [SerializeField] VRSeatRig seatRig;
        [SerializeField] RoverAutopilot autopilot;

        static GUIStyle richLabel;
        public static GUIStyle RichLabel => richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Visible = true;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) Visible = !Visible;
            if (kb.f12Key.wasPressedThisFrame && motion != null) motion.ToggleEmergencyStop();
            if (kb.f5Key.wasPressedThisFrame && seatRig != null) seatRig.Recenter();
            if (kb.pKey.wasPressedThisFrame && gameFlow != null) gameFlow.TogglePause();
            if (kb.f9Key.wasPressedThisFrame && gameFlow != null) gameFlow.GoToMenu();
            if (kb.f8Key.wasPressedThisFrame && autopilot != null) autopilot.Toggle();
            if (kb.f7Key.wasPressedThisFrame && gameFlow != null && gameFlow.Mission != null) gameFlow.Mission.DebugSkipStage();
            if (motion != null && motion.Settings != null)
            {
                if (kb.pageUpKey.wasPressedThisFrame) motion.Settings.masterIntensity = Mathf.Clamp01(motion.Settings.masterIntensity + 0.1f);
                if (kb.pageDownKey.wasPressedThisFrame) motion.Settings.masterIntensity = Mathf.Clamp01(motion.Settings.masterIntensity - 0.1f);
            }
        }

        void OnGUI()
        {
            if (!Draw) return;
            var area = new Rect(10f, 10f, 330f, 236f);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 4f, area.width - 16f, area.height - 8f));
            string state = gameFlow != null ? gameFlow.State.ToString() : "-";
            GUILayout.Label($"<b>ЛУННЫЙ РОВЕР</b>   {state}   {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):0} fps", RichLabel);
            if (rover != null)
                GUILayout.Label($"Скорость {rover.SpeedMs * 3.6f:0} км/ч   {(rover.IsGrounded ? "на грунте" : "<color=#7cf>В ПОЛЁТЕ</color>")}", RichLabel);
            if (motion != null && motion.Settings != null)
            {
                string estop = motion.IsEmergencyStopped ? "   <color=#f55><b>E-STOP</b></color>" : "";
                string ap = autopilot != null && autopilot.Active ? "  <color=#fc6>АВТО</color>" : "";
                GUILayout.Label($"Интенсивность {motion.Settings.masterIntensity:0.0}   фейд {motion.Fade:0.00}{estop}{ap}", RichLabel);
            }
            var mission = gameFlow != null ? gameFlow.Mission : null;
            if (mission != null && gameFlow.State != GameState.Menu)
            {
                int t = Mathf.FloorToInt(gameFlow.MissionTime);
                string hull = gameFlow.Hull != null ? $"   корпус {gameFlow.Hull.Normalized * 100f:0}%" : "";
                GUILayout.Label($"Этап {mission.StageIndex + 1}/{mission.StageCount} <b>{mission.Title}</b>   {t / 60}:{t % 60:00}{hull}", RichLabel);
            }
            int calls = 0;
            foreach (var kv in HapticsDirector.Calls) calls += kv.Value;
            GUILayout.Label($"Тактильные вызовы: {calls}  (разных эффектов: {HapticsDirector.Calls.Count})", RichLabel);
            if (rover != null && rover.Input != null)
            {
                var input = rover.Input;
                string wheel = input.WheelDevices.Count > 0 ? string.Join(", ", input.WheelDevices) : "<color=#888>нет</color>";
                string stick = input.FlightStickDevices.Count > 0 ? string.Join(", ", input.FlightStickDevices) : "<color=#888>нет</color>";
                GUILayout.Label($"<size=10>Руль: {wheel}\nДжойстик: {stick}</size>", RichLabel);
            }
            GUILayout.Label("<size=10>F1 панель  F5 центр VR  F8 автопилот  F7 пропуск этапа\nF9 меню  P пауза  PgUp/PgDn интенсивность   <color=#f88>F12 АВАРИЙНЫЙ СТОП</color></size>", RichLabel);
            GUILayout.EndArea();
        }

        static Texture2D white;

        public static void DrawRect(Rect r, Color c)
        {
            if (white == null) white = Texture2D.whiteTexture;
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = prev;
        }
    }
}
