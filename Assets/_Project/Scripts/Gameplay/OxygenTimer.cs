using UnityEngine;

namespace Mehatron.Gameplay
{
    /// <summary>Запас кислорода на миссию. Тикает только во время игры (пауза = Time.timeScale 0).</summary>
    public class OxygenTimer : MonoBehaviour
    {
        [SerializeField] float duration = 600f;
        [Tooltip("Запас не может превысить duration * maxFill после пополнений.")]
        [SerializeField] float maxFill = 1f;
        [Range(0f, 1f)] [SerializeField] float lowThreshold = 0.2f;

        /// <summary>Множитель запаса от сложности.</summary>
        public float DurationScale { get; set; } = 1f;
        float Duration => duration * DurationScale;

        public float Remaining { get; private set; }
        public float Normalized => Duration > 0f ? Remaining / Duration : 0f;
        public bool IsLow { get; private set; }
        public bool Running { get; set; }
        public bool Depleted => Remaining <= 0f;

        public void ResetTimer()
        {
            Remaining = Duration;
            Running = false;
            SetLow(false);
        }

        /// <summary>Пополнение из контейнера.</summary>
        public void AddTime(float seconds)
        {
            Remaining = Mathf.Min(Duration * maxFill, Remaining + seconds);
            SetLow(Normalized <= lowThreshold);
            GameEvents.RaiseOxygenRefilled(seconds);
        }

        void Awake() => ResetTimer();

        void Update()
        {
            if (!Running || Depleted) return;
            Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);
            SetLow(Normalized <= lowThreshold);
        }

        void SetLow(bool low)
        {
            if (low == IsLow) return;
            IsLow = low;
            GameEvents.RaiseLowOxygenChanged(low);
        }
    }
}
