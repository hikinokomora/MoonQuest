using UnityEngine;

namespace Mehatron.Motion
{
    [CreateAssetMenu(menuName = "Mehatron/Motion Settings", fileName = "MotionSettings")]
    public class MotionSettings : ScriptableObject
    {
        [Header("Безопасность")]
        [Tooltip("Общий множитель амплитуд. Для первых тестов на стенде ставьте 0.3.")]
        [Range(0f, 1f)] public float masterIntensity = 0.3f;
        [Tooltip("Максимальная скорость изменения каждой оси (нормализованных единиц в секунду).")]
        public float maxRatePerSecond = 3f;
        [Tooltip("Время плавного выхода в ноль / входа из нуля при смене состояния игры, секунды.")]
        public float fadeTime = 2f;
        [Tooltip("Частота отправки позы на платформу, Гц.")]
        public float sendRateHz = 100f;

        [Header("Крен/тангаж от наклона кузова")]
        [Tooltip("Наклон кузова (градусы), который соответствует полному отклонению платформы.")]
        public float bodyTiltFullScaleDeg = 25f;
        [Range(0f, 1f)] public float bodyTiltGain = 0.8f;

        [Header("Tilt coordination (ускорения → наклон)")]
        [Tooltip("Продольное ускорение (м/с²), дающее полный тангаж.")]
        public float longAccelFullScale = 8f;
        [Range(0f, 1f)] public float longAccelTiltGain = 0.35f;
        [Tooltip("Боковое ускорение (м/с²), дающее полный крен.")]
        public float latAccelFullScale = 8f;
        [Range(0f, 1f)] public float latAccelTiltGain = 0.35f;
        [Tooltip("Сглаживание ускорений, секунды.")]
        public float accelSmoothing = 0.12f;

        [Header("Washout (высокочастотные оси)")]
        [Tooltip("Постоянная времени washout-фильтра, секунды. Больше — дольше держится ощущение.")]
        public float washoutTime = 0.8f;
        public float surgeGain = 0.6f;
        public float swayGain = 0.6f;
        public float heaveGain = 0.5f;
        [Tooltip("Вертикальное ускорение (м/с²), дающее полный heave.")]
        public float heaveAccelFullScale = 10f;
        [Tooltip("Скорость рыскания (град/с), дающая полный yaw.")]
        public float yawRateFullScale = 60f;
        public float yawGain = 0.4f;

        [Header("Импульсы (приземления, удары)")]
        public float landingHeaveKick = 1.0f;
        public float landingPitchKick = 0.35f;
        public float impactKick = 0.5f;
        [Tooltip("Жёсткость пружины импульсов. Выше — короче и резче.")]
        public float kickStiffness = 180f;
        [Tooltip("Демпфирование пружины импульсов.")]
        public float kickDamping = 14f;

        [Header("Тряска по камням")]
        public float roughnessHeave = 0.25f;
        public float roughnessTilt = 0.08f;
        public float roughnessFrequency = 14f;

        [Header("Оси платформы (выключите отсутствующие)")]
        public bool usePitch = true;
        public bool useRoll = true;
        public bool useHeave = true;
        public bool useYaw = true;
        public bool useSurge = false;
        public bool useSway = false;
    }
}
