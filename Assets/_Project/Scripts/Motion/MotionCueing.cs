using System.Collections.Generic;
using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.Motion
{
    /// <summary>
    /// Превращает физику ровера и игровые события в позу платформы:
    /// наклон кузова + tilt coordination от ускорений + washout-оси + пружинные импульсы (приземления, удары) + тряска.
    /// Отвечает за безопасность: клэмп, ограничение скорости, плавный вход/выход, аварийная остановка.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class MotionCueing : MonoBehaviour
    {
        [SerializeField] MotionSettings settings;
        [SerializeField] RoverController rover;
        [Tooltip("Все адаптеры, которым отправляется поза: реальная платформа, отладочный визуализатор и т.д.")]
        [SerializeField] List<MotionPlatformBehaviour> outputs = new List<MotionPlatformBehaviour>();

        public MotionSettings Settings => settings;

        /// <summary>Подменить настройки рабочей копией (меню правит копию, ассет на диске не меняется).</summary>
        public MotionSettings UseRuntimeCopy()
        {
            if (settings != null && !settingsCopied)
            {
                settings = Instantiate(settings);
                settingsCopied = true;
            }
            return settings;
        }

        bool settingsCopied;
        /// <summary>Поза, отправленная на платформу последней (после фейда, клэмпа и ограничения скорости).</summary>
        public MotionPose OutputPose { get; private set; }
        public float Fade => fade;
        public bool IsEmergencyStopped { get; private set; }

        float fade;
        float fadeTarget;
        float sendTimer;

        Vector3 prevLocalVelocity;
        Vector3 smoothedAccel;
        float prevYaw;
        bool hasPrevSample;

        readonly HighPass surgeHP = new HighPass();
        readonly HighPass swayHP = new HighPass();
        readonly HighPass heaveHP = new HighPass();
        readonly HighPass yawHP = new HighPass();

        Spring kickPitch, kickRoll, kickHeave;
        float airSink;

        // Сценарные эффекты миссии
        float checkTime = -1f;
        float quakeLeft, quakeDuration, quakeStrength;
        bool storm;
        float stormLevel;

        void OnEnable()
        {
            GameEvents.Landed += OnLanded;
            GameEvents.Impact += OnImpact;
            GameEvents.StateChanged += OnStateChanged;
            GameEvents.SystemsCheck += OnSystemsCheck;
            GameEvents.QuakeStarted += OnQuake;
            GameEvents.StormChanged += OnStorm;
            GameEvents.Gust += OnGust;
            GameEvents.StaticDischarge += OnStatic;
            foreach (var o in outputs)
                if (o != null) o.Connect();
        }

        void OnDisable()
        {
            GameEvents.Landed -= OnLanded;
            GameEvents.Impact -= OnImpact;
            GameEvents.StateChanged -= OnStateChanged;
            GameEvents.SystemsCheck -= OnSystemsCheck;
            GameEvents.QuakeStarted -= OnQuake;
            GameEvents.StormChanged -= OnStorm;
            GameEvents.Gust -= OnGust;
            GameEvents.StaticDischarge -= OnStatic;
            ParkAll();
        }

        void OnApplicationQuit() => ParkAll();

        void OnStateChanged(GameState state)
        {
            fadeTarget = state == GameState.Playing ? 1f : 0f;
            if (state == GameState.Menu || state == GameState.Result)
            {
                checkTime = -1f;
                quakeLeft = 0f;
                storm = false;
            }
        }

        void OnSystemsCheck() => checkTime = 0f;

        void OnQuake(float duration, float strength)
        {
            quakeDuration = quakeLeft = Mathf.Max(0.1f, duration);
            quakeStrength = strength;
            AddKick(0f, 0f, -settings.impactKick * strength * 8f); // первый толчок снизу
        }

        void OnStorm(bool on) => storm = on;

        // Ветер слева (side < 0) кренит кузов вправо, и наоборот.
        void OnGust(float side) => AddKick(0f, -side * settings.impactKick * 5f, 0f);

        void OnStatic(Vector3 point, float strength) => AddKick(0f, 0f, settings.impactKick * strength * 3f);

        public void ToggleEmergencyStop()
        {
            if (IsEmergencyStopped)
            {
                // Возобновляем строго из нуля, чтобы платформа не дёрнулась.
                IsEmergencyStopped = false;
                fade = 0f;
                OutputPose = MotionPose.Zero;
                foreach (var o in outputs)
                    if (o != null) o.Connect();
                Debug.Log("[Motion] Аварийная остановка снята.");
            }
            else
            {
                IsEmergencyStopped = true;
                foreach (var o in outputs)
                    if (o != null) o.EmergencyStop();
                Debug.LogWarning("[Motion] АВАРИЙНАЯ ОСТАНОВКА.");
            }
        }

        public void AddKick(float pitch, float roll, float heave)
        {
            kickPitch.velocity += pitch;
            kickRoll.velocity += roll;
            kickHeave.velocity += heave;
        }

        void OnLanded(float strength)
        {
            // Кузов «проседает» (heave вниз) и клюёт носом, затем пружина возвращает в ноль.
            AddKick(-settings.landingPitchKick * strength * 10f, 0f, -settings.landingHeaveKick * strength * 14f);
        }

        void OnImpact(Vector3 point, float strength)
        {
            if (rover == null) return;
            Vector3 local = rover.transform.InverseTransformPoint(point);
            local.y = 0f;
            Vector3 dir = local.sqrMagnitude > 0.01f ? local.normalized : Vector3.forward;
            float k = settings.impactKick * strength * 10f;
            // Удар справа толкает кузов влево, спереди — назад.
            AddKick(dir.z * k * 0.6f, -dir.x * k, k * 0.5f);
        }

        void FixedUpdate()
        {
            if (rover == null || rover.Body == null) return;
            float dt = Time.fixedDeltaTime;

            Vector3 localVel = rover.transform.InverseTransformDirection(rover.Body.linearVelocity);
            float yaw = rover.transform.eulerAngles.y;
            if (!hasPrevSample)
            {
                prevLocalVelocity = localVel;
                prevYaw = yaw;
                hasPrevSample = true;
                return;
            }

            Vector3 accel = (localVel - prevLocalVelocity) / dt;
            prevLocalVelocity = localVel;
            float k = settings.accelSmoothing > 0f ? 1f - Mathf.Exp(-dt / settings.accelSmoothing) : 1f;
            smoothedAccel = Vector3.Lerp(smoothedAccel, accel, k);

            float yawRate = Mathf.DeltaAngle(prevYaw, yaw) / dt;
            prevYaw = yaw;

            surgeHP.Step(smoothedAccel.z / settings.longAccelFullScale, settings.washoutTime, dt);
            swayHP.Step(smoothedAccel.x / settings.latAccelFullScale, settings.washoutTime, dt);
            heaveHP.Step(smoothedAccel.y / settings.heaveAccelFullScale, settings.washoutTime * 0.5f, dt);
            yawHP.Step(yawRate / settings.yawRateFullScale, settings.washoutTime, dt);

            kickPitch.Step(settings.kickStiffness, settings.kickDamping, dt);
            kickRoll.Step(settings.kickStiffness, settings.kickDamping, dt);
            kickHeave.Step(settings.kickStiffness, settings.kickDamping, dt);

            // В полёте платформа плавно опускается — ощущение невесомости перед приземлением.
            float sinkTarget = rover.IsGrounded ? 0f : -0.35f;
            airSink = Mathf.MoveTowards(airSink, sinkTarget, dt * (rover.IsGrounded ? 2f : 0.6f));

            if (checkTime >= 0f) checkTime += dt;
            if (quakeLeft > 0f) quakeLeft -= dt;
            stormLevel = Mathf.MoveTowards(stormLevel, storm ? 1f : 0f, dt / 4f);
        }

        /// <summary>
        /// Тест платформы на старте (после фейда 2 c): плавно тангаж, крен, подъём — игрок и оператор видят, что все оси живы.
        /// </summary>
        MotionPose SystemsCheckPose()
        {
            if (checkTime < 4f || checkTime > 8.5f) return MotionPose.Zero;
            float Bump(float t0, float len) => checkTime > t0 && checkTime < t0 + len ? Mathf.Sin((checkTime - t0) / len * Mathf.PI * 2f) : 0f;
            return new MotionPose { pitch = 0.3f * Bump(4f, 1.5f), roll = 0.3f * Bump(5.5f, 1.5f), heave = 0.35f * Bump(7f, 1.5f) };
        }

        void Update()
        {
            if (settings == null) return;
            float dt = Time.unscaledDeltaTime;

            fade = Mathf.MoveTowards(fade, IsEmergencyStopped ? 0f : fadeTarget, dt / Mathf.Max(0.01f, settings.fadeTime));

            MotionPose target = ComputeTargetPose();
            target = MaskAxes(target) * (fade * settings.masterIntensity);
            OutputPose = RateLimit(OutputPose, Clamp(target), settings.maxRatePerSecond * dt);

            if (IsEmergencyStopped) return;
            sendTimer += dt;
            float period = 1f / Mathf.Max(1f, settings.sendRateHz);
            if (sendTimer < period) return;
            sendTimer = 0f;
            var pose = OutputPose;
            foreach (var o in outputs)
                if (o != null && o.IsConnected) o.SendPose(pose);
        }

        MotionPose ComputeTargetPose()
        {
            if (rover == null) return MotionPose.Zero;
            var s = settings;
            Transform t = rover.transform;

            float bodyPitch = Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float bodyRoll = Mathf.Asin(Mathf.Clamp(-t.right.y, -1f, 1f)) * Mathf.Rad2Deg;

            float longAcc = Mathf.Clamp(smoothedAccel.z / s.longAccelFullScale, -1f, 1f);
            float latAcc = Mathf.Clamp(smoothedAccel.x / s.latAccelFullScale, -1f, 1f);

            float speedFactor = Mathf.Clamp01(rover.SpeedMs / 8f);
            float rough = rover.IsGrounded ? rover.Roughness * speedFactor : 0f;
            float time = Time.time * s.roughnessFrequency;
            float n1 = Mathf.PerlinNoise(time, 0.3f) * 2f - 1f;
            float n2 = Mathf.PerlinNoise(0.7f, time) * 2f - 1f;
            float n3 = Mathf.PerlinNoise(time, time * 0.5f + 11f) * 2f - 1f;

            // Лунотрясение: низкочастотная качка, затухает к концу.
            float quake = quakeLeft > 0f ? quakeStrength * Mathf.Clamp01(quakeLeft / quakeDuration) : 0f;
            float qt = Time.time * 7f;
            float q1 = Mathf.PerlinNoise(qt, 5.1f) * 2f - 1f;
            float q2 = Mathf.PerlinNoise(9.3f, qt) * 2f - 1f;
            float q3 = Mathf.Sin(Time.time * 11f) * (Mathf.PerlinNoise(qt, qt) * 0.5f + 0.5f);
            // Буря: мелкая постоянная турбулентность.
            float st = stormLevel * 0.06f;
            float sn = Mathf.PerlinNoise(Time.time * 3f, 21f) * 2f - 1f;
            var check = SystemsCheckPose();

            return new MotionPose
            {
                // Разгон прижимает к спинке → нос вверх; поворот вправо прижимает влево → крен влево.
                pitch = bodyPitch / s.bodyTiltFullScaleDeg * s.bodyTiltGain
                        + longAcc * s.longAccelTiltGain
                        + kickPitch.value
                        + n1 * rough * s.roughnessTilt
                        + q1 * quake * 0.3f
                        + check.pitch,
                roll = bodyRoll / s.bodyTiltFullScaleDeg * s.bodyTiltGain
                       - latAcc * s.latAccelTiltGain
                       + kickRoll.value
                       + n2 * rough * s.roughnessTilt
                       + q2 * quake * 0.3f
                       + sn * st
                       + check.roll,
                heave = heaveHP.value * s.heaveGain
                        + kickHeave.value
                        + airSink
                        + n3 * rough * s.roughnessHeave
                        + q3 * quake * 0.45f
                        + check.heave,
                yaw = yawHP.value * s.yawGain,
                surge = surgeHP.value * s.surgeGain,
                sway = swayHP.value * s.swayGain,
            };
        }

        MotionPose MaskAxes(MotionPose p)
        {
            if (!settings.usePitch) p.pitch = 0f;
            if (!settings.useRoll) p.roll = 0f;
            if (!settings.useHeave) p.heave = 0f;
            if (!settings.useYaw) p.yaw = 0f;
            if (!settings.useSurge) p.surge = 0f;
            if (!settings.useSway) p.sway = 0f;
            return p;
        }

        static MotionPose Clamp(MotionPose p) => new MotionPose
        {
            pitch = Mathf.Clamp(p.pitch, -1f, 1f),
            roll = Mathf.Clamp(p.roll, -1f, 1f),
            heave = Mathf.Clamp(p.heave, -1f, 1f),
            yaw = Mathf.Clamp(p.yaw, -1f, 1f),
            surge = Mathf.Clamp(p.surge, -1f, 1f),
            sway = Mathf.Clamp(p.sway, -1f, 1f),
        };

        static MotionPose RateLimit(MotionPose from, MotionPose to, float maxDelta) => new MotionPose
        {
            pitch = Mathf.MoveTowards(from.pitch, to.pitch, maxDelta),
            roll = Mathf.MoveTowards(from.roll, to.roll, maxDelta),
            heave = Mathf.MoveTowards(from.heave, to.heave, maxDelta),
            yaw = Mathf.MoveTowards(from.yaw, to.yaw, maxDelta),
            surge = Mathf.MoveTowards(from.surge, to.surge, maxDelta),
            sway = Mathf.MoveTowards(from.sway, to.sway, maxDelta),
        };

        void ParkAll()
        {
            foreach (var o in outputs)
                if (o != null) o.Park();
        }

        /// <summary>Фильтр высоких частот первого порядка (washout): держит ощущение и плавно возвращает ось в ноль.</summary>
        class HighPass
        {
            public float value;
            float prevInput;

            public void Step(float input, float tau, float dt)
            {
                float a = tau / (tau + dt);
                value = a * (value + input - prevInput);
                prevInput = input;
            }
        }

        struct Spring
        {
            public float value;
            public float velocity;

            public void Step(float stiffness, float damping, float dt)
            {
                velocity += (-stiffness * value - damping * velocity) * dt;
                value += velocity * dt;
            }
        }
    }
}
