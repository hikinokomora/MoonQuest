using UnityEngine;

namespace Mehatron.Haptics
{
    /// <summary>
    /// Базовый адаптер тактильного устройства. Игровой код общается с ним только через <see cref="HapticsDirector"/>.
    /// </summary>
    public abstract class HapticsDeviceBehaviour : MonoBehaviour
    {
        public abstract bool IsConnected { get; }

        /// <summary>Направленный удар. angleDeg: 0 — спереди, 90 — справа, 180 — сзади, -90 — слева. height: 0 — низ, 1 — верх.</summary>
        public abstract void PlayImpact(float angleDeg, float height, float intensity);

        public abstract void PlayPattern(HapticPattern pattern, float intensity);

        /// <summary>Постоянная фоновая вибрация (двигатель, камни), 0..1.</summary>
        public abstract void SetRumble(float intensity);

        public abstract void StopAll();
    }
}
