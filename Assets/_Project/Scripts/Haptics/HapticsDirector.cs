using System.Collections.Generic;
using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.Haptics
{
    /// <summary>Переводит игровые события в команды тактильных устройств.</summary>
    public class HapticsDirector : MonoBehaviour
    {
        [SerializeField] RoverController rover;
        [SerializeField] List<HapticsDeviceBehaviour> devices = new List<HapticsDeviceBehaviour>();

        [Header("Интенсивности")]
        [SerializeField] float landingGain = 1f;
        [SerializeField] float impactGain = 1f;
        [SerializeField] float sampleIntensity = 0.8f;
        [SerializeField] float heartbeatIntensity = 0.7f;
        [SerializeField] float engineIdleRumble = 0.03f;
        [SerializeField] float roughRumble = 0.45f;
        [SerializeField] float stormRumble = 0.12f;
        [SerializeField] float quakeRumble = 0.5f;
        [SerializeField] float alarmIntensity = 0.6f;

        /// <summary>Сколько раз за миссию вызывался каждый тактильный эффект (для панели оператора).</summary>
        public static readonly Dictionary<string, int> Calls = new Dictionary<string, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Calls.Clear();

        static void Count(string kind) => Calls[kind] = Calls.TryGetValue(kind, out int n) ? n + 1 : 1;

        /// <summary>Множитель фоновой вибрации (настройки).</summary>
        public float RumbleScale { get; set; } = 1f;

        bool playing;
        bool storm;
        bool lowOxygen, hullCritical;
        float quakeLeft, quakeDuration, quakeStrength;

        void OnEnable()
        {
            GameEvents.Landed += OnLanded;
            GameEvents.Impact += OnImpact;
            GameEvents.SampleCollected += OnSample;
            GameEvents.LowOxygenChanged += OnLowOxygen;
            GameEvents.MeteorShowerStarted += OnMeteors;
            GameEvents.StateChanged += OnState;
            GameEvents.MissionFinished += OnFinished;
            GameEvents.SystemsCheck += OnSystemsCheck;
            GameEvents.ObjectiveChanged += OnObjective;
            GameEvents.TaskDone += OnTaskDone;
            GameEvents.ScanPulse += OnScan;
            GameEvents.QuakeStarted += OnQuake;
            GameEvents.StormChanged += OnStorm;
            GameEvents.StaticDischarge += OnStatic;
            GameEvents.OxygenRefilled += OnRefill;
            GameEvents.HullCriticalChanged += OnHullCritical;
        }

        void OnDisable()
        {
            GameEvents.Landed -= OnLanded;
            GameEvents.Impact -= OnImpact;
            GameEvents.SampleCollected -= OnSample;
            GameEvents.LowOxygenChanged -= OnLowOxygen;
            GameEvents.MeteorShowerStarted -= OnMeteors;
            GameEvents.StateChanged -= OnState;
            GameEvents.MissionFinished -= OnFinished;
            GameEvents.SystemsCheck -= OnSystemsCheck;
            GameEvents.ObjectiveChanged -= OnObjective;
            GameEvents.TaskDone -= OnTaskDone;
            GameEvents.ScanPulse -= OnScan;
            GameEvents.QuakeStarted -= OnQuake;
            GameEvents.StormChanged -= OnStorm;
            GameEvents.StaticDischarge -= OnStatic;
            GameEvents.OxygenRefilled -= OnRefill;
            GameEvents.HullCriticalChanged -= OnHullCritical;
            ForEach(d => d.StopAll());
        }

        void Update()
        {
            float rumble = 0f;
            if (playing && rover != null)
            {
                float speed = Mathf.Clamp01(rover.SpeedMs / 10f);
                rumble = engineIdleRumble + (rover.IsGrounded ? rover.Roughness * speed * roughRumble : 0f);
                if (storm) rumble += stormRumble;
                if (quakeLeft > 0f)
                {
                    quakeLeft -= Time.deltaTime;
                    rumble += quakeRumble * quakeStrength * Mathf.Clamp01(quakeLeft / quakeDuration);
                }
            }
            rumble *= RumbleScale;
            ForEach(d => d.SetRumble(rumble));
        }

        void OnLanded(float s) => Play(HapticPattern.Landing, Mathf.Clamp01(s * landingGain));

        void OnImpact(Vector3 point, float s)
        {
            if (rover == null) return;
            Vector3 local = rover.transform.InverseTransformPoint(point);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg; // 0 — спереди, 90 — справа
            float height = Mathf.Clamp01(0.5f + local.y * 0.1f);
            Count("Impact");
            ForEach(d => d.PlayImpact(angle, height, Mathf.Clamp01(s * impactGain)));
        }

        void OnSample(int c, int t) => Play(HapticPattern.SampleCollected, sampleIntensity);
        void OnLowOxygen(bool low)
        {
            lowOxygen = low;
            Play(HapticPattern.Heartbeat, low ? heartbeatIntensity : 0f);
        }
        void OnMeteors() => Play(HapticPattern.MeteorWarning, 0.8f);
        void OnFinished(bool ok) => Play(ok ? HapticPattern.MissionComplete : HapticPattern.MissionFailed, 0.9f);

        void OnSystemsCheck()
        {
            Calls.Clear();
            Play(HapticPattern.SystemsCheck, 0.8f);
        }
        // Этап 0 начинается вместе с тестом систем — отдельное уведомление не нужно.
        void OnObjective(int index, string title) { if (index > 0) Play(HapticPattern.Notify, 0.6f); }
        void OnTaskDone(string text) => Play(HapticPattern.ObjectiveComplete, 0.8f);
        void OnScan(float progress) => Play(HapticPattern.ScanPulse, Mathf.Lerp(0.3f, 0.9f, progress));

        void OnQuake(float duration, float strength)
        {
            quakeDuration = quakeLeft = Mathf.Max(0.1f, duration);
            quakeStrength = strength;
            Play(HapticPattern.Quake, strength);
        }

        void OnStorm(bool on) => storm = on;

        void OnStatic(Vector3 point, float s)
        {
            Play(HapticPattern.StaticShock, s);
            OnImpact(point, s * 0.6f); // разряд ощущается со стороны вспышки
        }

        void OnRefill(float seconds) => Play(HapticPattern.OxygenRefill, 0.8f);
        void OnHullCritical(bool critical)
        {
            hullCritical = critical;
            Play(HapticPattern.HullAlarm, critical ? alarmIntensity : 0f);
        }

        void OnState(GameState s)
        {
            playing = s == GameState.Playing;
            if (s != GameState.Playing && s != GameState.Paused) { storm = false; quakeLeft = 0f; }
            if (s == GameState.Menu) ForEach(d => d.StopAll());
            if (s == GameState.Playing)
            {
                // После паузы возобновляем постоянные сигналы.
                if (lowOxygen) ForEach(d => d.PlayPattern(HapticPattern.Heartbeat, heartbeatIntensity));
                if (hullCritical) ForEach(d => d.PlayPattern(HapticPattern.HullAlarm, alarmIntensity));
            }
            if (s == GameState.Paused || s == GameState.Result)
            {
                ForEach(d => d.PlayPattern(HapticPattern.Heartbeat, 0f));
                ForEach(d => d.PlayPattern(HapticPattern.HullAlarm, 0f));
            }
        }

        /// <summary>Эффект на все устройства; включения (intensity > 0) учитываются в <see cref="Calls"/>.</summary>
        void Play(HapticPattern p, float intensity)
        {
            if (intensity > 0f) Count(p.ToString());
            ForEach(d => d.PlayPattern(p, intensity));
        }

        void ForEach(System.Action<HapticsDeviceBehaviour> a)
        {
            foreach (var d in devices)
                if (d != null && d.isActiveAndEnabled) a(d);
        }
    }
}
