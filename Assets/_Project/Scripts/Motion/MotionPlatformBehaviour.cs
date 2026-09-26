using UnityEngine;

namespace Mehatron.Motion
{
    /// <summary>
    /// Базовый класс адаптера платформы. Реальная библиотека подключается наследником,
    /// который переводит нормализованную <see cref="MotionPose"/> в команды железа.
    /// </summary>
    public abstract class MotionPlatformBehaviour : MonoBehaviour
    {
        public abstract bool IsConnected { get; }

        /// <summary>Подключиться к платформе и поднять её в рабочее (нейтральное) положение.</summary>
        public abstract void Connect();

        /// <summary>Отправить позу. Значения уже ограничены -1..1 и по скорости.</summary>
        public abstract void SendPose(in MotionPose pose);

        /// <summary>Штатная парковка (опустить платформу). Вызывается при выходе из игры.</summary>
        public abstract void Park();

        /// <summary>Аварийная остановка: немедленно прекратить движение.</summary>
        public abstract void EmergencyStop();
    }
}
