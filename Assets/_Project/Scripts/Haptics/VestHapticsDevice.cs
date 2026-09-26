using System.Collections;
using UnityEngine;

namespace Mehatron.Haptics
{
    /// <summary>
    /// Жилет на сетке моторов: все паттерны реализованы здесь один раз, наследник только отправляет кадры
    /// моторов в своё железо (<see cref="OutputFrame"/>). Для bHaptics: наследник вызывает PlayMotors.
    /// </summary>
    public abstract class VestHapticsDevice : HapticsDeviceBehaviour
    {
        [SerializeField] int columns = 4;
        [SerializeField] int rows = 5;
        [Tooltip("Частота отправки кадров моторов, Гц.")]
        [SerializeField] float frameRate = 20f;
        [Tooltip("Скорость затухания импульсов (единиц интенсивности в секунду).")]
        [SerializeField] float decayPerSecond = 5f;
        [Range(0f, 1f)] [SerializeField] float masterIntensity = 1f;

        protected VestMotorModel Model { get; private set; }
        public float MasterIntensity { get => masterIntensity; set => masterIntensity = Mathf.Clamp01(value); }
        /// <summary>Итоговые значения моторов (импульсы + фоновая вибрация), 0..1.</summary>
        protected float[] OutFront, OutBack;

        float rumble;
        float frameTimer;
        Coroutine heartbeat;
        Coroutine hullAlarm;

        protected virtual void Awake()
        {
            Model = new VestMotorModel(columns, rows);
            OutFront = new float[Model.MotorsPerSide];
            OutBack = new float[Model.MotorsPerSide];
        }

        /// <summary>Отправить кадр в железо. Значения 0..1, индекс = row * cols + col.</summary>
        protected abstract void OutputFrame(float[] front, float[] back);

        public override void PlayImpact(float angleDeg, float height, float intensity) =>
            Model.Impact(angleDeg, height, intensity * masterIntensity);

        public override void SetRumble(float intensity) => rumble = Mathf.Clamp01(intensity) * masterIntensity;

        public override void StopAll()
        {
            StopAllCoroutines();
            heartbeat = null;
            hullAlarm = null;
            rumble = 0f;
            Model?.Clear();
        }

        public override void PlayPattern(HapticPattern pattern, float intensity)
        {
            intensity *= masterIntensity;
            switch (pattern)
            {
                case HapticPattern.Landing: StartCoroutine(Landing(intensity)); break;
                case HapticPattern.SampleCollected: StartCoroutine(Collect(intensity)); break;
                case HapticPattern.MeteorWarning: StartCoroutine(Warning(intensity)); break;
                case HapticPattern.MissionComplete: StartCoroutine(Celebrate(intensity)); break;
                case HapticPattern.MissionFailed: StartCoroutine(Failed(intensity)); break;
                case HapticPattern.SystemsCheck: StartCoroutine(SystemsCheck(intensity)); break;
                case HapticPattern.Notify: StartCoroutine(Notify(intensity)); break;
                case HapticPattern.ObjectiveComplete: StartCoroutine(Ring(intensity)); break;
                case HapticPattern.ScanPulse: ScanPulse(intensity); break;
                case HapticPattern.Quake: StartCoroutine(Quake(intensity)); break;
                case HapticPattern.StaticShock: StartCoroutine(Sparks(intensity)); break;
                case HapticPattern.OxygenRefill: StartCoroutine(Refill(intensity)); break;
                case HapticPattern.HullAlarm:
                    if (hullAlarm != null) StopCoroutine(hullAlarm);
                    hullAlarm = intensity > 0f ? StartCoroutine(HullAlarm(intensity)) : null;
                    break;
                case HapticPattern.Heartbeat:
                    // intensity 0 выключает сердцебиение
                    if (heartbeat != null) StopCoroutine(heartbeat);
                    heartbeat = intensity > 0f ? StartCoroutine(Heartbeat(intensity)) : null;
                    break;
            }
        }

        // Удар снизу: волна от поясницы к плечам.
        IEnumerator Landing(float k)
        {
            Model.PulseAll(k * 0.6f);
            for (int r = Model.Rows - 1; r >= 0; r--)
            {
                Model.PulseRow(r, k);
                yield return Wait(0.05f);
            }
        }

        // «Вспышка» из центра груди.
        IEnumerator Collect(float k)
        {
            int cx0 = Model.Cols / 2 - 1, cx1 = Model.Cols / 2, cy = Model.Rows / 2;
            Model.Pulse(true, cx0, cy, k); Model.Pulse(true, cx1, cy, k);
            yield return Wait(0.08f);
            for (int c = 0; c < Model.Cols; c++)
                for (int r = cy - 1; r <= cy + 1; r++) Model.Pulse(true, c, r, k * 0.7f);
            yield return Wait(0.08f);
            Model.PulseAll(k * 0.35f, true, false);
        }

        // Три коротких толчка по плечам сзади.
        IEnumerator Warning(float k)
        {
            for (int i = 0; i < 3; i++)
            {
                Model.PulseRow(0, k, true, true);
                yield return Wait(0.18f);
            }
        }

        IEnumerator Celebrate(float k)
        {
            for (int n = 0; n < 3; n++)
                for (int r = Model.Rows - 1; r >= 0; r--)
                {
                    Model.PulseRow(r, k * 0.8f);
                    yield return Wait(0.06f);
                }
        }

        IEnumerator Failed(float k)
        {
            float t = 0f;
            while (t < 1.5f)
            {
                Model.PulseAll(k * (1f - t / 1.5f));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // «Тук-тук» слева на груди.
        IEnumerator Heartbeat(float k)
        {
            while (true)
            {
                Model.Pulse(true, 0, 1, k); Model.Pulse(true, 1, 1, k); Model.Pulse(true, 0, 2, k * 0.8f);
                yield return Wait(0.22f);
                Model.Pulse(true, 0, 1, k * 0.6f); Model.Pulse(true, 1, 1, k * 0.6f);
                yield return Wait(0.75f);
            }
        }

        // Каждый мотор по очереди: перед сверху вниз, затем спина — проверка, что все моторы живы и стоят на месте.
        IEnumerator SystemsCheck(float k)
        {
            for (int side = 0; side < 2; side++)
                for (int r = 0; r < Model.Rows; r++)
                    for (int c = 0; c < Model.Cols; c++)
                    {
                        Model.Pulse(side == 0, c, r, k * 0.8f);
                        yield return Wait(0.09f);
                    }
            Model.PulseAll(k * 0.6f);
        }

        // Двойной тап по верхнему ряду спереди — «новое сообщение от ЦУП».
        IEnumerator Notify(float k)
        {
            for (int i = 0; i < 2; i++)
            {
                Model.PulseRow(0, k, true, false);
                yield return Wait(0.15f);
            }
        }

        // Кольцо вокруг корпуса: перед слева направо, спина справа налево.
        IEnumerator Ring(float k)
        {
            int cy = Model.Rows / 2;
            for (int c = 0; c < Model.Cols; c++)
            {
                Model.Pulse(true, c, cy, k); Model.Pulse(true, c, cy - 1, k * 0.6f);
                yield return Wait(0.06f);
            }
            for (int c = Model.Cols - 1; c >= 0; c--)
            {
                Model.Pulse(false, c, cy, k); Model.Pulse(false, c, cy - 1, k * 0.6f);
                yield return Wait(0.06f);
            }
            Model.PulseAll(k * 0.4f, true, false);
        }

        // Импульс сканера: точка в центре груди, с ростом силы — целое пятно.
        void ScanPulse(float k)
        {
            int cx0 = Model.Cols / 2 - 1, cx1 = Model.Cols / 2, cy = Model.Rows / 2;
            Model.Pulse(true, cx0, cy, k); Model.Pulse(true, cx1, cy, k);
            if (k > 0.6f)
                for (int c = cx0 - 1; c <= cx1 + 1; c++)
                    for (int r = cy - 1; r <= cy + 1; r++) Model.Pulse(true, c, r, k * 0.5f);
        }

        // Волны снизу вверх по всему корпусу с неровным ритмом.
        IEnumerator Quake(float k)
        {
            for (int w = 0; w < 6; w++)
            {
                float a = k * (1f - w / 7f);
                for (int r = Model.Rows - 1; r >= 0; r--)
                {
                    Model.PulseRow(r, a);
                    yield return Wait(0.04f);
                }
                yield return Wait(Random.Range(0.25f, 0.6f));
            }
        }

        // Россыпь коротких случайных «искр».
        IEnumerator Sparks(float k)
        {
            for (int i = 0; i < 10; i++)
            {
                Model.Pulse(Random.value < 0.5f, Random.Range(0, Model.Cols), Random.Range(0, Model.Rows), k * Random.Range(0.6f, 1f));
                yield return Wait(Random.Range(0.02f, 0.06f));
            }
        }

        // Две волны от пояса к плечам — «вдох».
        IEnumerator Refill(float k)
        {
            for (int n = 0; n < 2; n++)
            {
                for (int r = Model.Rows - 1; r >= 0; r--)
                {
                    Model.PulseRow(r, k * 0.7f);
                    yield return Wait(0.12f);
                }
                yield return Wait(0.2f);
            }
        }

        // Тревога: поочерёдные удары по левому и правому боку.
        IEnumerator HullAlarm(float k)
        {
            while (true)
            {
                for (int r = 0; r < Model.Rows; r++) { Model.Pulse(true, 0, r, k); Model.Pulse(false, 0, r, k); }
                yield return Wait(0.25f);
                for (int r = 0; r < Model.Rows; r++) { Model.Pulse(true, Model.Cols - 1, r, k); Model.Pulse(false, Model.Cols - 1, r, k); }
                yield return Wait(1.4f);
            }
        }

        static WaitForSecondsRealtime Wait(float s) => new WaitForSecondsRealtime(s);

        protected virtual void Update()
        {
            if (Model == null) return; // после перезагрузки скриптов в Play Mode Awake не вызывается
            float dt = Time.unscaledDeltaTime;
            Model.Decay(decayPerSecond * dt);

            float n = rumble > 0f ? 0.6f + 0.4f * Mathf.PerlinNoise(Time.unscaledTime * 25f, 0f) : 0f;
            for (int i = 0; i < OutFront.Length; i++)
            {
                OutFront[i] = Mathf.Clamp01(Mathf.Max(Model.Front[i], rumble * n));
                OutBack[i] = Mathf.Clamp01(Mathf.Max(Model.Back[i], rumble * n));
            }

            frameTimer += dt;
            if (frameTimer < 1f / frameRate) return;
            frameTimer = 0f;
            if (IsConnected) OutputFrame(OutFront, OutBack);
        }

        protected virtual void OnDisable() => StopAll();
    }
}
