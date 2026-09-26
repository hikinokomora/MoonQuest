using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Mehatron.Vehicle
{
    /// <summary>
    /// Риг сидячего VR: SeatRig (точка глаз в кресле) → CameraOffset → Camera (TrackedPoseDriver).
    /// Recenter сдвигает CameraOffset так, чтобы голова оказалась в точке глаз и смотрела вперёд по роверу.
    /// Без шлема камера управляется мышью (ПКМ) — удобно для отладки.
    /// </summary>
    public class VRSeatRig : MonoBehaviour
    {
        [SerializeField] Transform cameraOffset;
        [SerializeField] Transform headCamera;
        [Tooltip("TrackedPoseDriver камеры — выключается, когда шлем не активен.")]
        [SerializeField] Behaviour trackedPoseDriver;
        [SerializeField] RoverInput input;
        [SerializeField] float mouseSensitivity = 0.15f;

        float yaw, pitch;

        public float MouseSensitivity { get => mouseSensitivity; set => mouseSensitivity = value; }
        /// <summary>Подъём/опускание точки глаз, м (настройка «Высота сиденья»).</summary>
        public float HeightOffset { get; set; }
        Vector3 seatBase;
        bool seatBaseSaved;

        public static bool XRActive => XRSettings.isDeviceActive;

        IEnumerator Start()
        {
            // Трекинг появляется не в первый кадр — ждём и центрируем.
            yield return new WaitForSeconds(1f);
            Recenter();
        }

        void Update()
        {
            if (!seatBaseSaved) { seatBase = transform.localPosition; seatBaseSaved = true; }
            transform.localPosition = seatBase + Vector3.up * HeightOffset;
            if (input != null && input.RecenterPressed) Recenter();
            bool xr = XRActive;
            if (trackedPoseDriver != null && trackedPoseDriver.enabled != xr) trackedPoseDriver.enabled = xr;
            if (!xr) DesktopLook();
        }

        public void Recenter()
        {
            if (cameraOffset == null || headCamera == null) return;
            if (!XRActive)
            {
                cameraOffset.localPosition = Vector3.zero;
                cameraOffset.localRotation = Quaternion.identity;
                yaw = pitch = 0f;
                return;
            }
            // Поза головы внутри CameraOffset (то, что даёт трекинг).
            Vector3 headLocalPos = headCamera.localPosition;
            float headYaw = headCamera.localEulerAngles.y;

            cameraOffset.localRotation = Quaternion.Euler(0f, -headYaw, 0f);
            cameraOffset.localPosition = -(cameraOffset.localRotation * headLocalPos);
            Debug.Log("[VR] Recenter");
        }

        void DesktopLook()
        {
            var mouse = Mouse.current;
            if (mouse == null || headCamera == null) return;
            if (mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
                yaw = Mathf.Clamp(yaw + d.x, -150f, 150f);
                pitch = Mathf.Clamp(pitch - d.y, -80f, 80f);
            }
            headCamera.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
