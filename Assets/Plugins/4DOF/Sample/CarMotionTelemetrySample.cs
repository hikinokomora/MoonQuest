using _4DOF;
using UnityEngine;

public class CarMotionTelemetrySample : MonoBehaviour
{
    [SerializeField] private Rigidbody body;

    // Постоянная времени сглаживания, с.
    [SerializeField] private float smoothing = 0.05f;

    // Сколько секунд держать пик удара.
    [SerializeField] private float impactHold = 0.12f;

    private readonly MotionTelemetrySender _sender =
        new MotionTelemetrySender();
    private Vector3 _lastVelocity;
    private Vector3 _accel;
    private float _yawRate;
    private float _heave;
    private float _holdTimer;

    private void OnEnable()
    {
        _lastVelocity = body.linearVelocity;
        _sender.Start();
    }

    private void OnDisable() => _sender.Stop();

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        float k = 1f - Mathf.Exp(-dt / smoothing);

        // 1. Ускорение в осях машины.
        Vector3 velocity = body.linearVelocity;
        Vector3 accel = transform
            .InverseTransformDirection(
                (velocity - _lastVelocity) / dt);
        _lastVelocity = velocity;
        _accel = Vector3.Lerp(_accel, accel, k);

        // 2. Вертикаль: пик удара держим
        //    impactHold секунд.
        float vertical =
            Mathf.Clamp(accel.y, -20f, 20f);
        if (Mathf.Abs(vertical) >= Mathf.Abs(_heave))
        {
            _heave = vertical;
            _holdTimer = impactHold;
        }
        else if (_holdTimer > 0f)
        {
            _holdTimer -= dt;
        }
        else
        {
            _heave = Mathf.Lerp(_heave, vertical, k);
        }

        // 3. Скорость рыскания, град/с.
        Vector3 angular = transform
            .InverseTransformDirection(
                body.angularVelocity);
        _yawRate = Mathf.Lerp(
            _yawRate, angular.y * Mathf.Rad2Deg, k);

        // 4. Углы кузова: -180..180 градусов.
        Vector3 euler = transform.eulerAngles;
        float pitch = Mathf.DeltaAngle(0f, euler.x);
        float roll = Mathf.DeltaAngle(0f, euler.z);

        // Продольная ось в Unity — Z, боковая — X.
        _sender.Data.Set(pitch, roll, _yawRate,
            _accel.z, _accel.x, _heave);
    }
}
