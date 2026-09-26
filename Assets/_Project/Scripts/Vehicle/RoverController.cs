using UnityEngine;

namespace Mehatron.Vehicle
{
    /// <summary>
    /// Аркадный лунный ровер на лучевой подвеске (надёжнее WheelCollider при низкой гравитации и прыжках).
    /// Автомат: газ толкает вперёд, тормоз при остановке включает задний ход. Ручник уменьшает сцепление задних колёс.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RoverController : MonoBehaviour
    {
        [System.Serializable]
        public class Wheel
        {
            public Transform anchor;
            public Transform visual;
            [Tooltip("Пустышка в центре колеса без руления и вращения (к ней крепятся кулак и шарниры рычагов).")]
            public Transform carrier;
            [Tooltip("Кулак с крылом: следует за колесом, поворачивается рулём, не вращается.")]
            public Transform knuckle;
            [Tooltip("Смещение оси поворота (шкворня, через шаровые опоры) от центра колеса вдоль оси X ровера.")]
            public float kingpinOffset;
            public bool steer;
            public bool rear;
            [HideInInspector] public bool grounded;
            [HideInInspector] public float compression;
            [HideInInspector] public float spin;
        }

        [SerializeField] RoverInput input;
        [SerializeField] Wheel[] wheels = new Wheel[0];
        [Tooltip("Центр масс в локальных координатах — ниже реального, чтобы ровер не опрокидывался.")]
        [SerializeField] Vector3 centerOfMass = new Vector3(0f, 0.35f, 0f);

        [Header("Подвеска")]
        [SerializeField] float restLength = 0.45f;
        [SerializeField] float wheelRadius = 0.42f;
        [SerializeField] float springStrength = 9000f;
        [SerializeField] float springDamping = 900f;
        [Tooltip("Максимальный ход колеса вверх от свободного положения. Дальше — жёсткий отбойник, колесо не уходит в крыло/корпус.")]
        [SerializeField] float maxCompression = 0.3f;
        [SerializeField] float bumpStopStrength = 80000f;
        [SerializeField] LayerMask groundMask = ~0;

        [Header("Двигатель (автомат)")]
        [SerializeField] float motorForce = 4200f;
        [SerializeField] float maxSpeedKmh = 45f;
        [SerializeField] float reverseMaxSpeedKmh = 12f;
        [SerializeField] float brakeForce = 5000f;
        [SerializeField] float rollingResistance = 0.06f;

        [Header("Рулевое")]
        [SerializeField] float maxSteerAngle = 28f;
        [Tooltip("Уменьшение угла поворота на скорости (0 — нет).")]
        [SerializeField] float highSpeedSteerReduction = 0.5f;
        [SerializeField] float steerSpeed = 6f;

        [Header("Сцепление")]
        [SerializeField] float grip = 0.9f;
        [SerializeField] float handbrakeRearGrip = 0.25f;

        [Header("Стабилизация")]
        [Tooltip("Лёгкое выравнивание кузова в полёте, чтобы ровер не кувыркался.")]
        [SerializeField] float airLevelTorque = 1.5f;
        [SerializeField] float flipResetDelay = 2.5f;

        [Header("Приземления")]
        [SerializeField] float minAirTimeForLanding = 0.35f;
        [Tooltip("Вертикальная скорость удара (м/с), соответствующая силе 1.")]
        [SerializeField] float landingFullScaleSpeed = 9f;

        [Header("Удары о препятствия")]
        [SerializeField] float minImpactSpeed = 2f;
        [SerializeField] float impactFullScaleSpeed = 10f;

        public Rigidbody Body { get; private set; }
        public bool IsGrounded { get; private set; }
        public float SpeedMs => Body != null ? Body.linearVelocity.magnitude : 0f;
        public float ForwardSpeedMs { get; private set; }
        /// <summary>Шероховатость грунта 0..1 (задают зоны <see cref="Gameplay.RoughZone"/>).</summary>
        public float Roughness { get; set; }
        public bool ControlsEnabled { get; set; } = true;
        public RoverInput Input => input;
        public Wheel[] Wheels => wheels;

        float currentSteer;
        float airTime;
        float minAirVerticalSpeed;
        float upsideDownTime;
        int roverLayerMask;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.centerOfMass = centerOfMass;
            roverLayerMask = groundMask & ~(1 << gameObject.layer);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float throttle = 0f, brake = 0f, steer = 0f;
            bool handbrake = false;
            if (ControlsEnabled && input != null)
            {
                throttle = input.Throttle;
                brake = input.Brake;
                steer = input.Steer;
                handbrake = input.Handbrake;
            }

            ForwardSpeedMs = Vector3.Dot(Body.linearVelocity, transform.forward);
            float speedKmh = Mathf.Abs(ForwardSpeedMs) * 3.6f;
            float steerLimit = maxSteerAngle * Mathf.Lerp(1f, 1f - highSpeedSteerReduction, speedKmh / maxSpeedKmh);
            currentSteer = Mathf.MoveTowards(currentSteer, steer * steerLimit, steerSpeed * maxSteerAngle * dt);

            // Автомат: тормоз на почти нулевой скорости = задний ход.
            float drive = throttle;
            float brakeAmount = brake;
            if (brake > 0.1f && throttle < 0.1f && ForwardSpeedMs < 0.8f)
            {
                drive = -brake;
                brakeAmount = 0f;
            }

            int groundedCount = 0;
            float massPerWheel = Body.mass / Mathf.Max(1, wheels.Length);
            foreach (var w in wheels)
            {
                if (w.anchor == null) continue;
                if (w.steer) w.anchor.localRotation = Quaternion.Euler(0f, currentSteer, 0f);

                Vector3 origin = w.anchor.position;
                Vector3 up = transform.up;
                w.grounded = Physics.Raycast(origin, -up, out RaycastHit hit, restLength + wheelRadius, roverLayerMask, QueryTriggerInteraction.Ignore);
                if (!w.grounded)
                {
                    w.compression = 0f;
                    continue;
                }
                groundedCount++;

                Vector3 pointVel = Body.GetPointVelocity(origin);
                w.compression = restLength + wheelRadius - hit.distance;
                float springForce = w.compression * springStrength - Vector3.Dot(pointVel, up) * springDamping;
                if (w.compression > maxCompression)
                    springForce += (w.compression - maxCompression) * bumpStopStrength;
                Body.AddForceAtPosition(up * Mathf.Max(0f, springForce), origin);

                Vector3 fwd = Vector3.ProjectOnPlane(w.anchor.forward, hit.normal).normalized;
                Vector3 side = Vector3.Cross(hit.normal, fwd);
                float lateralVel = Vector3.Dot(pointVel, side);
                float wheelGrip = w.rear && handbrake ? handbrakeRearGrip : grip;
                Vector3 lateralForce = -side * lateralVel * wheelGrip * massPerWheel / dt;
                // Ограничение силы трения, чтобы на скорости можно было сорваться в занос.
                float maxLateral = massPerWheel * Mathf.Abs(Physics.gravity.y) * 2.5f;
                Body.AddForceAtPosition(Vector3.ClampMagnitude(lateralForce, maxLateral), hit.point + up * 0.2f);

                float wheelFwdVel = Vector3.Dot(pointVel, fwd);
                float driveForce = 0f;
                if (drive > 0f && wheelFwdVel * 3.6f < maxSpeedKmh)
                    driveForce = drive * motorForce * (1f - Mathf.Clamp01(wheelFwdVel * 3.6f / maxSpeedKmh) * 0.6f);
                else if (drive < 0f && -wheelFwdVel * 3.6f < reverseMaxSpeedKmh)
                    driveForce = drive * motorForce * 0.5f;

                float resist = -wheelFwdVel * rollingResistance * massPerWheel;
                float brakeF = -Mathf.Sign(wheelFwdVel) * Mathf.Min(Mathf.Abs(wheelFwdVel) * massPerWheel / dt, brakeAmount * brakeForce / wheels.Length);
                if (handbrake && w.rear) brakeF += -Mathf.Sign(wheelFwdVel) * Mathf.Min(Mathf.Abs(wheelFwdVel) * massPerWheel / dt, brakeForce * 0.5f / wheels.Length);

                Body.AddForceAtPosition(fwd * (driveForce / wheels.Length + resist + brakeF), hit.point + up * 0.2f);
            }

            UpdateAirborne(groundedCount, dt);
            UpdateFlipRecovery(dt);
        }

        void UpdateAirborne(int groundedCount, float dt)
        {
            bool wasGrounded = IsGrounded;
            IsGrounded = groundedCount >= 2;

            if (!IsGrounded)
            {
                airTime += dt;
                minAirVerticalSpeed = Mathf.Min(minAirVerticalSpeed, Body.linearVelocity.y);
                // Мягко выравниваем кузов в полёте.
                Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
                Body.AddTorque(axis * airLevelTorque * Body.mass, ForceMode.Force);
            }
            else
            {
                if (!wasGrounded && airTime >= minAirTimeForLanding)
                    GameEvents.RaiseLanded(-minAirVerticalSpeed / landingFullScaleSpeed);
                airTime = 0f;
                minAirVerticalSpeed = 0f;
            }
        }

        void UpdateFlipRecovery(float dt)
        {
            bool upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0.3f && SpeedMs < 2f;
            upsideDownTime = upsideDown ? upsideDownTime + dt : 0f;
            if (upsideDownTime > flipResetDelay) ResetUpright();
        }

        public void ResetUpright()
        {
            upsideDownTime = 0f;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Teleport(transform.position + Vector3.up * 1.5f, Quaternion.LookRotation(fwd, Vector3.up));
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.position = position;
            Body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            currentSteer = 0f;
            airTime = 0f;
            minAirVerticalSpeed = 0f;
            IsGrounded = true;
        }

        void LateUpdate()
        {
            foreach (var w in wheels)
            {
                if (w.anchor == null || w.visual == null) continue;
                // Визуальный ход ограничен тем же пределом, что и физический: колесо не заходит в крыло.
                float travel = w.grounded ? Mathf.Clamp(w.compression, 0f, maxCompression) : 0f;
                Vector3 center = w.anchor.position - transform.up * (restLength - travel);
                // Руление вокруг шкворня (через шаровые опоры), а не вокруг центра колеса — концы рычагов не расходятся с кулаком.
                Quaternion steerRot = w.steer ? Quaternion.AngleAxis(currentSteer, transform.up) : Quaternion.identity;
                Vector3 kp = transform.right * w.kingpinOffset;
                Vector3 steeredCenter = center + kp - steerRot * kp;
                w.visual.position = steeredCenter;
                w.spin += ForwardSpeedMs / wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
                w.visual.rotation = w.anchor.rotation * Quaternion.Euler(w.spin, 0f, 0f);
                if (w.carrier != null)
                    w.carrier.SetPositionAndRotation(center, transform.rotation);
                if (w.knuckle != null)
                    w.knuckle.SetPositionAndRotation(steeredCenter, steerRot * transform.rotation);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0) return;
            // Касания колёсной зоны о грунт не считаем ударами — это делает подвеска.
            ContactPoint c = collision.GetContact(0);
            if (Vector3.Dot(c.normal, transform.up) > 0.7f) return;
            // Сила удара — только составляющая скорости вдоль нормали: скользящее касание вала кратера не удар.
            float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, c.normal));
            if (speed < minImpactSpeed) return;
            float strength = (speed - minImpactSpeed) / (impactFullScaleSpeed - minImpactSpeed);
            GameEvents.RaiseImpact(c.point, strength);
            GameEvents.RaiseCollided(c.point, strength);
        }
    }
}
