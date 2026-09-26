using UnityEngine;

namespace Mehatron.Motion
{
    /// <summary>
    /// Поза платформы в нормализованных единицах -1..1 по каждой оси.
    /// Адаптер конкретной платформы сам переводит их в градусы/мм и игнорирует оси, которых у неё нет.
    /// Знаки: pitch &gt; 0 — нос вверх, roll &gt; 0 — крен вправо, heave &gt; 0 — вверх,
    /// yaw &gt; 0 — поворот вправо, surge &gt; 0 — вперёд, sway &gt; 0 — вправо.
    /// </summary>
    [System.Serializable]
    public struct MotionPose
    {
        public float pitch;
        public float roll;
        public float heave;
        public float yaw;
        public float surge;
        public float sway;

        public static MotionPose Zero => default;

        public static MotionPose Lerp(MotionPose a, MotionPose b, float t) => new MotionPose
        {
            pitch = Mathf.Lerp(a.pitch, b.pitch, t),
            roll = Mathf.Lerp(a.roll, b.roll, t),
            heave = Mathf.Lerp(a.heave, b.heave, t),
            yaw = Mathf.Lerp(a.yaw, b.yaw, t),
            surge = Mathf.Lerp(a.surge, b.surge, t),
            sway = Mathf.Lerp(a.sway, b.sway, t),
        };

        public static MotionPose operator *(MotionPose p, float k) => new MotionPose
        {
            pitch = p.pitch * k, roll = p.roll * k, heave = p.heave * k,
            yaw = p.yaw * k, surge = p.surge * k, sway = p.sway * k,
        };

        public override string ToString() =>
            $"P {pitch:+0.00;-0.00} R {roll:+0.00;-0.00} H {heave:+0.00;-0.00} Y {yaw:+0.00;-0.00} Su {surge:+0.00;-0.00} Sw {sway:+0.00;-0.00}";
    }
}
