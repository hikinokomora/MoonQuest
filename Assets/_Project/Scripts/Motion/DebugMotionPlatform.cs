using Mehatron.UI;
using UnityEngine;

namespace Mehatron.Motion
{
    /// <summary>
    /// Виртуальная платформа: ничего не двигает, а показывает позу на экране оператора (десктопное окно).
    /// Опционально наклоняет модель-«макет» платформы в сцене.
    /// </summary>
    public class DebugMotionPlatform : MotionPlatformBehaviour
    {
        [Tooltip("Необязательный объект, который наклоняется вместе с позой (макет платформы).")]
        [SerializeField] Transform previewModel;
        [SerializeField] float previewMaxAngle = 20f;
        [SerializeField] float previewMaxHeave = 0.15f;
        [SerializeField] bool logCalls = true;

        public MotionPose LastPose { get; private set; }
        public string Status { get; private set; } = "не подключена";

        bool connected;
        Vector3 previewBasePos;

        public override bool IsConnected => connected;

        void Awake()
        {
            if (previewModel != null) previewBasePos = previewModel.localPosition;
        }

        public override void Connect()
        {
            connected = true;
            Status = "подключена";
            if (logCalls) Debug.Log("[DebugPlatform] Connect");
        }

        public override void SendPose(in MotionPose pose)
        {
            LastPose = pose;
            if (previewModel == null) return;
            previewModel.localRotation = Quaternion.Euler(-pose.pitch * previewMaxAngle, pose.yaw * previewMaxAngle, -pose.roll * previewMaxAngle);
            previewModel.localPosition = previewBasePos + Vector3.up * pose.heave * previewMaxHeave;
        }

        public override void Park()
        {
            LastPose = MotionPose.Zero;
            Status = "припаркована";
            if (logCalls && connected) Debug.Log("[DebugPlatform] Park");
            connected = false;
        }

        public override void EmergencyStop()
        {
            LastPose = MotionPose.Zero;
            Status = "АВАРИЙНАЯ ОСТАНОВКА";
            connected = false;
            if (logCalls) Debug.LogWarning("[DebugPlatform] EmergencyStop");
        }

        void OnGUI()
        {
            if (!OperatorPanel.Draw) return;
            const float w = 260f, rowH = 18f;
            var area = new Rect(10f, 252f, w, 8 * rowH + 10f);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 4f, w - 16f, area.height - 8f));
            GUILayout.Label($"<b>Платформа</b> — {Status}", OperatorPanel.RichLabel);
            var p = LastPose;
            Bar("Pitch", p.pitch);
            Bar("Roll", p.roll);
            Bar("Heave", p.heave);
            Bar("Yaw", p.yaw);
            Bar("Surge", p.surge);
            Bar("Sway", p.sway);
            GUILayout.EndArea();
        }

        static void Bar(string label, float value)
        {
            var r = GUILayoutUtility.GetRect(200f, 16f);
            GUI.Label(new Rect(r.x, r.y, 50f, r.height), label);
            var track = new Rect(r.x + 50f, r.y + 4f, r.width - 100f, 8f);
            OperatorPanel.DrawRect(track, new Color(1f, 1f, 1f, 0.15f));
            float half = track.width * 0.5f;
            float v = Mathf.Clamp(value, -1f, 1f) * half;
            var fill = v >= 0f
                ? new Rect(track.x + half, track.y, v, track.height)
                : new Rect(track.x + half + v, track.y, -v, track.height);
            OperatorPanel.DrawRect(fill, Mathf.Abs(value) > 0.9f ? new Color(1f, 0.35f, 0.3f) : new Color(0.3f, 0.8f, 1f));
            OperatorPanel.DrawRect(new Rect(track.x + half - 0.5f, track.y - 2f, 1f, track.height + 4f), Color.white);
            GUI.Label(new Rect(track.xMax + 6f, r.y, 44f, r.height), value.ToString("+0.00;-0.00"));
        }
    }
}
