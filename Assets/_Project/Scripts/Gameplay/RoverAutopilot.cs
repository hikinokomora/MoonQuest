using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.Gameplay
{
    /// <summary>
    /// Автопилот для демонстрации и тестов: едет к текущей цели (образец или база).
    /// Включается с панели оператора (F8). Реальный ввод при этом игнорируется.
    /// </summary>
    public class RoverAutopilot : MonoBehaviour
    {
        [SerializeField] GameFlow flow;
        [SerializeField] float cruiseThrottle = 0.85f;
        [SerializeField] float steerGain = 2.5f;
        [Tooltip("На таком угле до цели сбрасываем газ, чтобы развернуться.")]
        [SerializeField] float slowTurnAngle = 50f;

        [Tooltip("Если на газу скорость ниже порога дольше stuckTime — сдаём назад.")]
        [SerializeField] float stuckSpeed = 1.2f;
        [SerializeField] float stuckTime = 2f;
        [SerializeField] float reverseTime = 1.6f;
        [SerializeField] float avoidDistance = 12f;

        public bool Active { get; private set; }

        float stuckTimer;
        float reverseTimer;
        float reverseSteer;
        int stuckCount;
        float lastStuckTime = -100f;

        public void SetActive(bool on)
        {
            Active = on;
            var input = flow != null && flow.Rover != null ? flow.Rover.Input : null;
            if (input != null && !on) input.DebugOverride = null;
            Debug.Log($"[Autopilot] {(on ? "ВКЛ" : "ВЫКЛ")}");
        }

        public void Toggle() => SetActive(!Active);

        /// <summary>Три луча вперёд: объезжаем валуны и постройки (грунт не считаем препятствием).</summary>
        float Avoidance(RoverController rover)
        {
            int mask = ~(1 << rover.gameObject.layer);
            Vector3 origin = rover.transform.position + rover.transform.up * 1.0f;
            float Probe(float yaw)
            {
                Vector3 dir = Quaternion.AngleAxis(yaw, rover.transform.up) * rover.transform.forward;
                if (!Physics.SphereCast(origin, 0.8f, dir, out RaycastHit hit, avoidDistance, mask, QueryTriggerInteraction.Ignore))
                    return 0f;
                // Грунт и пологие поверхности (настил трамплина) — не препятствие, по ним едем.
                if (hit.collider is TerrainCollider || hit.normal.y > 0.7f) return 0f;
                return 1f - hit.distance / avoidDistance;
            }
            float left = Probe(-25f), center = Probe(0f), right = Probe(25f);
            if (left + center + right <= 0f) return 0f;
            float side = right > left ? -1f : 1f;
            return side * Mathf.Max(center, Mathf.Max(left, right)) * 2f;
        }

        void Update()
        {
            if (!Active || flow == null || flow.Rover == null) return;
            var rover = flow.Rover;
            var input = rover.Input;
            if (flow.State != GameState.Playing)
            {
                input.DebugOverride = new RoverInput.Override();
                return;
            }

            var target = flow.CurrentTarget();
            // В зоне развёртывания/бурения стоим, пока этап не засчитает удержание.
            if (!target.HasValue || (flow.Mission != null && flow.Mission.StopHere))
            {
                // Педаль тормоза на месте включает задний ход — гасим скорость тормозом, а стоим на ручнике.
                input.DebugOverride = new RoverInput.Override { brake = rover.ForwardSpeedMs > 0.5f ? 1f : 0f, handbrake = true };
                return;
            }

            Vector3 local = rover.transform.InverseTransformPoint(target.Value);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(angle / 45f * steerGain + Avoidance(rover), -1f, 1f);

            // Застряли (упёрлись в камень или трамплин) — сдаём назад с обратным рулём.
            if (reverseTimer > 0f)
            {
                reverseTimer -= Time.deltaTime;
                input.DebugOverride = new RoverInput.Override { steer = reverseSteer, brake = 1f };
                return;
            }
            stuckTimer = rover.SpeedMs < stuckSpeed ? stuckTimer + Time.deltaTime : 0f;
            if (stuckTimer > stuckTime)
            {
                stuckTimer = 0f;
                // Откат не помогает (сел на брюхо на гребне) — как игрок, жмём «поставить на колёса».
                stuckCount = Time.time - lastStuckTime < 20f ? stuckCount + 1 : 1;
                lastStuckTime = Time.time;
                if (stuckCount >= 3)
                {
                    stuckCount = 0;
                    rover.ResetUpright();
                    Debug.Log("[Autopilot] Застрял — сброс на колёса.");
                    return;
                }
                reverseTimer = reverseTime;
                reverseSteer = -Mathf.Sign(steer == 0f ? 1f : steer);
                return;
            }
            bool sharp = Mathf.Abs(angle) > slowTurnAngle;
            // Перед целью сбрасываем скорость, иначе на скорости не хватает радиуса поворота и ровер кружит.
            float dist = new Vector2(local.x, local.z).magnitude;
            float approach = Mathf.Clamp(dist / 35f, 0.3f, 1f);
            float speedKmh = rover.SpeedMs * 3.6f;
            bool tooFast = sharp && speedKmh > 15f;
            input.DebugOverride = new RoverInput.Override
            {
                steer = steer,
                throttle = tooFast ? 0f : (sharp ? 0.35f : cruiseThrottle * approach),
                brake = tooFast ? 0.6f : 0f,
            };
        }
    }
}
