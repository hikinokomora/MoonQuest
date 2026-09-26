using System;
using UnityEngine;

namespace Mehatron.Gameplay
{
    /// <summary>
    /// Точка задания миссии (ретранслятор, контейнер, керн): триггер-зона, маяк пока точка активна,
    /// объект, который появляется после выполнения (<see cref="reveal"/>), и объект, который исчезает (<see cref="hideOnComplete"/>).
    /// Умеет «падать с неба» (сброс контейнера с орбиты).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MissionPoint : MonoBehaviour
    {
        [Tooltip("Маяк и подсветка: видны, пока точка — текущая цель.")]
        [SerializeField] GameObject marker;
        [Tooltip("Появляется после выполнения (вырастает из грунта).")]
        [SerializeField] Transform reveal;
        [Tooltip("Исчезает после выполнения (подобранный груз).")]
        [SerializeField] GameObject hideOnComplete;
        [Tooltip("Падающая часть (для Drop). Если не задана — весь объект.")]
        [SerializeField] Transform dropBody;
        [SerializeField] float revealTime = 1.2f;

        public bool RoverInside { get; private set; }
        public bool Done { get; private set; }
        public bool Landed => dropTime <= 0f;
        public Vector3 Position => transform.position;

        Vector3 revealScale = Vector3.one;
        float revealT = -1f;
        Vector3 dropFrom, dropTo;
        float dropTime, dropDuration;
        Action<Vector3> onLand;

        Transform DropBody => dropBody != null ? dropBody : transform;

        void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            if (reveal != null) revealScale = reveal.localScale;
            if (dropBody != null) dropTo = dropBody.localPosition;
            ResetPoint();
        }

        public void ResetPoint()
        {
            Done = false;
            RoverInside = false;
            revealT = -1f;
            dropTime = 0f;
            if (reveal != null) reveal.gameObject.SetActive(false);
            if (hideOnComplete != null) hideOnComplete.SetActive(true);
            if (dropBody != null) dropBody.localPosition = dropTo;
            SetActive(false);
        }

        public void SetActive(bool on)
        {
            if (marker != null) marker.SetActive(on && !Done);
        }

        public void Complete()
        {
            if (Done) return;
            Done = true;
            SetActive(false);
            if (hideOnComplete != null) hideOnComplete.SetActive(false);
            if (reveal != null)
            {
                reveal.gameObject.SetActive(true);
                reveal.localScale = Vector3.zero;
                revealT = 0f;
            }
        }

        /// <summary>Сбросить груз с высоты: падает по дуге и «бьётся» о грунт.</summary>
        public void Drop(float height, float duration, Action<Vector3> landed)
        {
            dropTo = dropBody != null ? dropTo : transform.localPosition;
            dropFrom = dropTo + new Vector3(height * 0.35f, height, height * 0.2f);
            dropDuration = Mathf.Max(0.1f, duration);
            dropTime = dropDuration;
            onLand = landed;
            DropBody.localPosition = dropFrom;
        }

        void Update()
        {
            if (dropTime > 0f)
            {
                dropTime -= Time.deltaTime;
                float t = 1f - Mathf.Clamp01(dropTime / dropDuration);
                DropBody.localPosition = Vector3.Lerp(dropFrom, dropTo, t * t);
                if (dropTime <= 0f)
                {
                    DropBody.localPosition = dropTo;
                    onLand?.Invoke(transform.position);
                }
            }

            if (revealT >= 0f && reveal != null)
            {
                revealT += Time.deltaTime / Mathf.Max(0.01f, revealTime);
                float k = Mathf.Clamp01(revealT);
                // Лёгкий «перелёт» в конце, как у раскладывающейся конструкции.
                float s = 1f - Mathf.Pow(1f - k, 3f) + Mathf.Sin(k * Mathf.PI) * 0.08f;
                reveal.localScale = revealScale * s;
                if (k >= 1f) { reveal.localScale = revealScale; revealT = -1f; }
            }
        }

        void OnTriggerEnter(Collider other) { if (ZoneUtil.Rover(other) != null) RoverInside = true; }
        void OnTriggerExit(Collider other) { if (ZoneUtil.Rover(other) != null) RoverInside = false; }
    }
}
