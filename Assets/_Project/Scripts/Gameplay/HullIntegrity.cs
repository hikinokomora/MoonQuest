using UnityEngine;

namespace Mehatron.Gameplay
{
    /// <summary>Прочность корпуса ровера: сильные удары, близкие метеориты и разряды статики её снижают, контейнер снабжения чинит.</summary>
    public class HullIntegrity : MonoBehaviour
    {
        [SerializeField] float maxHull = 100f;
        [Tooltip("Удары слабее порога (0..1) не повреждают корпус.")]
        [SerializeField] float impactThreshold = 0.6f;
        [Tooltip("Урон от удара силой 1. Урон растёт квадратично: лёгкие касания почти не вредят.")]
        [SerializeField] float impactDamage = 10f;
        [Tooltip("Взрыв метеорита вредит корпусу только почти прямым попаданием (сила по расстоянию выше порога).")]
        [SerializeField] float meteorThreshold = 0.85f;
        [SerializeField] float meteorDamage = 8f;
        [SerializeField] float hardLandingThreshold = 0.75f;
        [SerializeField] float hardLandingDamage = 6f;
        [SerializeField] float staticDamage = 1f;
        [Range(0f, 1f)] [SerializeField] float criticalThreshold = 0.3f;
        [Tooltip("После урона корпус неуязвим это время: взрыв и отскок ровера считаются одним ударом.")]
        [SerializeField] float damageCooldown = 1.5f;

        /// <summary>Множитель урона от сложности.</summary>
        public float DamageScale { get; set; } = 1f;

        public float Value { get; private set; }
        public float Normalized => maxHull > 0f ? Value / maxHull : 0f;
        public bool IsCritical { get; private set; }
        public bool Broken => Value <= 0f;
        /// <summary>Урон принимается только во время миссии.</summary>
        public bool Active { get; set; }

        float lastDamageTime = -100f;

        void Awake() => ResetHull();

        void OnEnable()
        {
            GameEvents.Collided += OnImpact;
            GameEvents.MeteorExploded += OnMeteor;
            GameEvents.Landed += OnLanded;
            GameEvents.StaticDischarge += OnStatic;
        }

        void OnDisable()
        {
            GameEvents.Collided -= OnImpact;
            GameEvents.MeteorExploded -= OnMeteor;
            GameEvents.Landed -= OnLanded;
            GameEvents.StaticDischarge -= OnStatic;
        }

        public void ResetHull()
        {
            Value = maxHull;
            Active = false;
            SetCritical(false);
        }

        public void Repair(float amount)
        {
            Value = Mathf.Min(maxHull, Value + amount);
            SetCritical(Normalized <= criticalThreshold);
        }

        void OnImpact(Vector3 point, float s)
        {
            if (s >= impactThreshold) Damage(s * s * impactDamage);
        }

        void OnMeteor(Vector3 point, float s)
        {
            if (s >= meteorThreshold) Damage(meteorDamage * s);
        }

        void OnLanded(float s)
        {
            if (s >= hardLandingThreshold) Damage(hardLandingDamage * s);
        }

        void OnStatic(Vector3 point, float s) => Damage(staticDamage * s);

        void Damage(float amount)
        {
            amount *= DamageScale;
            if (!Active || Broken || amount <= 0f || Time.time - lastDamageTime < damageCooldown) return;
            lastDamageTime = Time.time;
            Value = Mathf.Max(0f, Value - amount);
            GameEvents.RaiseHullDamaged(Normalized);
            SetCritical(Normalized <= criticalThreshold);
        }

        void SetCritical(bool c)
        {
            if (c == IsCritical) return;
            IsCritical = c;
            GameEvents.RaiseHullCriticalChanged(c);
        }
    }
}
