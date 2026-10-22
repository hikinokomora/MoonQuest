#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
#define FOURDOF_AVAILABLE
#endif
using Mehatron.UI;
using UnityEngine;
#if FOURDOF_AVAILABLE
using _4DOF;
#endif

namespace Mehatron.Motion
{
    /// <summary>
    /// Адаптер реальной платформы 4DOF: переводит нормализованную <see cref="MotionPose"/> в единицы плагина 4DOF
    /// (градусы, град/с, м/с²) и отправляет в приложение X-DOF через общую память Windows.
    /// Плагин работает только в Windows (редактор и standalone) — на других платформах адаптер молчит.
    /// В сцене должен быть ОДИН отправитель: не добавляйте рядом <c>CarMotionTelemetrySample</c> из плагина.
    /// </summary>
    public class FourDofMotionPlatform : MotionPlatformBehaviour
    {
        [Header("Масштабы (что соответствует позе ±1)")]
        [Tooltip("Тангаж и крен капсулы, градусы. Не больше хода вашей платформы; реальный предел ещё раз задаёт X-DOF.")]
        [SerializeField] float pitchRollFullScaleDeg = 15f;
        [Tooltip("Жёсткий предел тангажа и крена, градусы — выше этого значения не уйдёт ничего.")]
        [SerializeField] float pitchRollLimitDeg = 20f;
        [Tooltip("Скорость рыскания, град/с.")]
        [SerializeField] float yawRateFullScale = 60f;
        [Tooltip("Продольное ускорение, м/с².")]
        [SerializeField] float surgeFullScale = 8f;
        [Tooltip("Боковое ускорение, м/с².")]
        [SerializeField] float swayFullScale = 8f;
        [Tooltip("Вертикальное ускорение, м/с².")]
        [SerializeField] float heaveFullScale = 10f;
        [Tooltip("Предел любого ускорения, м/с² (в примере плагина — 20).")]
        [SerializeField] float accelLimit = 20f;

        [Header("Удары по вертикали")]
        [Tooltip("Сколько секунд держать пик вертикального ускорения, чтобы X-DOF успел его отработать.")]
        [SerializeField] float impactHold = 0.12f;
        [Tooltip("Постоянная времени спада после удержания пика, секунды.")]
        [SerializeField] float heaveRelease = 0.05f;

        [Header("Безопасность")]
        [Tooltip("Если поза не приходила дольше этого времени (зависание, остановка игры), платформа уходит в нейтраль.")]
        [SerializeField] float watchdogSeconds = 0.5f;

        [Header("Инверсия осей (калибровка на стенде)")]
        [SerializeField] bool invertPitch;
        [SerializeField] bool invertRoll;
        [SerializeField] bool invertYaw;
        [SerializeField] bool invertSurge;
        [SerializeField] bool invertSway;
        [SerializeField] bool invertHeave;

        public string Status { get; private set; } = "не подключена";

        public override bool IsConnected => connected;

        const string SendingStatus = "отправка в X-DOF";

        static FourDofMotionPlatform active;

        bool connected;
        bool zeroed = true;
        float lastSendTime;
        float heave;
        float heaveHoldUntil;

#if FOURDOF_AVAILABLE
        readonly MotionTelemetrySender sender = new MotionTelemetrySender();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => active = null;

        public override void Connect()
        {
#if FOURDOF_AVAILABLE
            if (active != null && active != this && active.connected)
                Debug.LogWarning("[4DOF] Второй отправитель телеметрии: два отправителя перетирают данные друг друга.");
            active = this;
            ZeroOutput();
            sender.Start();
            connected = true;
            lastSendTime = Time.unscaledTime;
            Status = SendingStatus;
#else
            Status = "недоступна на этой платформе";
#endif
        }

        public override void SendPose(in MotionPose pose)
        {
#if FOURDOF_AVAILABLE
            if (!connected) return;
            float now = Time.unscaledTime;
            float dt = Mathf.Max(0f, now - lastSendTime);
            lastSendTime = now;
            zeroed = false;
            Status = SendingStatus;

            // MotionPose: pitch > 0 — нос вверх, плагин: нос вниз — плюс. Остальные знаки совпадают.
            float pitch = Limit(-Finite(pose.pitch) * pitchRollFullScaleDeg * Sign(invertPitch), pitchRollLimitDeg);
            float roll = Limit(Finite(pose.roll) * pitchRollFullScaleDeg * Sign(invertRoll), pitchRollLimitDeg);
            float yaw = Finite(pose.yaw) * yawRateFullScale * Sign(invertYaw);
            float surge = Limit(Finite(pose.surge) * surgeFullScale * Sign(invertSurge), accelLimit);
            float sway = Limit(Finite(pose.sway) * swayFullScale * Sign(invertSway), accelLimit);

            // Удар по вертикали держим impactHold секунд, потом плавно отпускаем — так сделано в примере плагина.
            float vertical = Limit(Finite(pose.heave) * heaveFullScale * Sign(invertHeave), accelLimit);
            if (Mathf.Abs(vertical) >= Mathf.Abs(heave))
            {
                heave = vertical;
                heaveHoldUntil = now + impactHold;
            }
            else if (now >= heaveHoldUntil)
            {
                float k = heaveRelease > 0f ? 1f - Mathf.Exp(-dt / heaveRelease) : 1f;
                heave = Mathf.Lerp(heave, vertical, k);
            }

            sender.Data.Set(pitch, roll, yaw, surge, sway, heave);
#endif
        }

        public override void Park()
        {
            Shutdown("припаркована");
        }

        public override void EmergencyStop()
        {
            Shutdown("АВАРИЙНАЯ ОСТАНОВКА");
        }

        void Shutdown(string status)
        {
            ZeroOutput();
#if FOURDOF_AVAILABLE
            sender.Stop(); // перед остановкой плагин записывает в память нули
#endif
            if (active == this) active = null;
            connected = false;
            Status = status;
        }

        /// <summary>Обнулить все оси, не останавливая отправку: платформа плавно уходит в нейтраль.</summary>
        void ZeroOutput()
        {
            heave = 0f;
            heaveHoldUntil = 0f;
            zeroed = true;
#if FOURDOF_AVAILABLE
            sender.Data.Reset();
#endif
        }

        void Update()
        {
            // Поток отправки повторяет последнюю позу бесконечно, поэтому при тишине от MotionCueing обнуляем сами.
            if (connected && !zeroed && Time.unscaledTime - lastSendTime > watchdogSeconds)
            {
                ZeroOutput();
                Status = "нет данных от игры — нейтраль";
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && connected) ZeroOutput();
        }

        void OnDisable()
        {
            if (connected) Shutdown("отключена");
        }

        void OnApplicationQuit()
        {
            if (connected) Shutdown("припаркована");
        }

        static float Sign(bool invert) => invert ? -1f : 1f;
        static float Finite(float v) => float.IsFinite(v) ? v : 0f;
        static float Limit(float v, float limit) => Mathf.Clamp(v, -limit, limit);

        void OnGUI()
        {
#if FOURDOF_AVAILABLE
            if (!OperatorPanel.Draw) return;
            var area = new Rect(10f, 412f, 330f, 76f);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 4f, area.width - 16f, area.height - 8f));
            GUILayout.Label($"<b>X-DOF (4DOF)</b> — {Status}", OperatorPanel.RichLabel);
            GUILayout.Label(sender.Data.ToString());
            GUILayout.EndArea();
#endif
        }
    }
}
