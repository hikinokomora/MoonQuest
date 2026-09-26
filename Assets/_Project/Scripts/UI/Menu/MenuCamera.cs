using UnityEngine;

namespace Mehatron.UI.Menu
{
    /// <summary>
    /// Кинематографичная камера фона меню (только без шлема): медленный облёт базы по нескольким «планам»
    /// с плавными переходами. В VR не используется — там меню висит перед кабиной.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MenuCamera : MonoBehaviour
    {
        [SerializeField] Transform focus;
        [SerializeField] float shotDuration = 14f;

        struct Shot
        {
            public float radius, height, startAngle, sweep, fov; public Vector3 lookOffset;
            public Shot(float r, float h, float a, float s, float fov, Vector3 look) { radius = r; height = h; startAngle = a; sweep = s; this.fov = fov; lookOffset = look; }
        }

        // Планы: общий над базой, низкий у колёс ровера, высокий с Землёй, проход вдоль модулей.
        static readonly Shot[] Shots =
        {
            new Shot(24f, 7f, 200f, 30f, 50f, new Vector3(0f, 1.2f, 0f)),
            new Shot(9f, 1.4f, 120f, 25f, 42f, new Vector3(0f, 1.1f, 0f)),
            new Shot(38f, 16f, 20f, 25f, 55f, new Vector3(0f, 6f, 10f)),
            new Shot(16f, 3.5f, 300f, 35f, 46f, new Vector3(-4f, 1.5f, -6f)),
        };

        Camera cam;
        float t;
        int shot;

        public Camera Camera => cam != null ? cam : cam = GetComponent<Camera>();
        public Transform Focus { get => focus; set => focus = value; }

        void OnEnable()
        {
            t = 0f;
            Place(1f);
        }

        void LateUpdate()
        {
            t += Time.unscaledDeltaTime;
            if (t > shotDuration)
            {
                t = 0f;
                shot = (shot + 1) % Shots.Length;
            }
            Place(UIKit.Smooth(0f, 1f, 3f));
        }

        void Place(float blend)
        {
            if (focus == null) return;
            var s = Shots[shot];
            float k = t / shotDuration;
            float ease = k * k * (3f - 2f * k);
            float a = (s.startAngle + s.sweep * ease) * Mathf.Deg2Rad;
            Vector3 c = focus.position;
            Vector3 target = c + new Vector3(Mathf.Sin(a) * s.radius, s.height + Mathf.Sin(Time.unscaledTime * 0.3f) * 0.3f, Mathf.Cos(a) * s.radius);
            // Не уходим под грунт.
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                float g = terrain.SampleHeight(target) + terrain.transform.position.y + 0.8f;
                if (target.y < g) target.y = g;
            }
            Quaternion look = Quaternion.LookRotation(c + s.lookOffset - target);
            // Плавно в начале плана — «затемнение» делает меню, тут только мягкий подвод.
            bool cut = t < 0.05f;
            transform.position = cut ? target : Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            transform.rotation = cut ? look : Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            Camera.fieldOfView = Mathf.Lerp(Camera.fieldOfView, s.fov, cut ? 1f : 0.05f);
        }

        /// <summary>Время до смены плана (меню делает затемнение-переход).</summary>
        public float TimeToCut => shotDuration - t;
    }
}
