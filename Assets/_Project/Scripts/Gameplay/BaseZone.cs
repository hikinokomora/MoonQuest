using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.Gameplay
{
    static class ZoneUtil
    {
        public static RoverController Rover(Collider c) =>
            c.attachedRigidbody != null ? c.attachedRigidbody.GetComponent<RoverController>() : null;
    }

    /// <summary>Зона базы: сюда нужно вернуться со всеми образцами.</summary>
    [RequireComponent(typeof(Collider))]
    public class BaseZone : MonoBehaviour
    {
        public bool RoverInside { get; private set; }

        void Awake() => GetComponent<Collider>().isTrigger = true;
        void OnTriggerEnter(Collider other) { if (ZoneUtil.Rover(other) != null) RoverInside = true; }
        void OnTriggerExit(Collider other) { if (ZoneUtil.Rover(other) != null) RoverInside = false; }
    }
}
