using UnityEngine;

namespace Mehatron.Gameplay
{
    /// <summary>Каменистый участок: пока ровер внутри, платформа и жилет трясутся.</summary>
    [RequireComponent(typeof(Collider))]
    public class RoughZone : MonoBehaviour
    {
        [Range(0f, 1f)] [SerializeField] float roughness = 0.8f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var rover = ZoneUtil.Rover(other);
            if (rover != null) rover.Roughness = Mathf.Max(rover.Roughness, roughness);
        }

        void OnTriggerExit(Collider other)
        {
            var rover = ZoneUtil.Rover(other);
            if (rover != null) rover.Roughness = 0f;
        }
    }
}
