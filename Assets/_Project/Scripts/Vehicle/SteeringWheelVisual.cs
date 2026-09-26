using UnityEngine;

namespace Mehatron.Vehicle
{
    /// <summary>Поворачивает руль модели вслед за реальным рулём стенда.</summary>
    public class SteeringWheelVisual : MonoBehaviour
    {
        [SerializeField] RoverInput input;
        [Tooltip("Пустышка на оси колонки: ось вращения = от руля к ней.")]
        [SerializeField] Transform axisPoint;
        [Tooltip("Угол поворота руля модели при полном отклонении ввода.")]
        [SerializeField] float maxAngle = 135f;

        Quaternion baseLocal;
        Vector3 localAxis;

        void Start()
        {
            baseLocal = transform.localRotation;
            Vector3 worldAxis = axisPoint != null ? (axisPoint.position - transform.position).normalized : transform.forward;
            localAxis = transform.parent != null ? transform.parent.InverseTransformDirection(worldAxis) : worldAxis;
        }

        void LateUpdate()
        {
            if (input == null) return;
            // Руль вправо — по часовой стрелке для водителя (ось смотрит на водителя).
            transform.localRotation = Quaternion.AngleAxis(-input.Steer * maxAngle, localAxis) * baseLocal;
        }
    }
}
