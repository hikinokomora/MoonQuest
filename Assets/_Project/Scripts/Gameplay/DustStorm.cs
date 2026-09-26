using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.Gameplay
{
    /// <summary>
    /// Электростатическая пылевая буря: туман, летящая пыль вокруг ровера, порывы (крен платформы, толчок кузова)
    /// и разряды статики рядом с ровером (молния + искры в жилет + небольшой урон корпусу).
    /// </summary>
    public class DustStorm : MonoBehaviour
    {
        [SerializeField] RoverController rover;
        [SerializeField] Material dustMaterial;
        [SerializeField] Material sparkMaterial;

        [Header("Туман")]
        [SerializeField] Color fogColor = new Color(0.46f, 0.42f, 0.37f);
        [SerializeField] float fogDensity = 0.018f;
        [SerializeField] float fadeTime = 6f;

        [Header("События")]
        [SerializeField] Vector2 dischargeInterval = new Vector2(3f, 7f);
        [SerializeField] Vector2 dischargeDistance = new Vector2(4f, 14f);
        [SerializeField] Vector2 gustInterval = new Vector2(2.5f, 5f);
        [SerializeField] float gustForce = 1.6f;

        public bool Active { get; private set; }
        public float Intensity => level;

        float level;
        float nextDischarge, nextGust;
        ParticleSystem dust;
        bool fogWas;
        Color fogColorWas;
        float fogDensityWas;
        FogMode fogModeWas;
        bool fogSaved;

        public void Begin()
        {
            if (Active) return;
            Active = true;
            nextDischarge = Random.Range(dischargeInterval.x, dischargeInterval.y) + 3f;
            nextGust = 2f;
            SaveFog();
            EnsureDust();
            dust.Play();
            GameEvents.RaiseStormChanged(true);
        }

        public void End()
        {
            if (!Active) return;
            Active = false;
            if (dust != null) dust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            GameEvents.RaiseStormChanged(false);
        }

        /// <summary>Мгновенный сброс (рестарт миссии).</summary>
        public void StopImmediate()
        {
            bool was = Active;
            Active = false;
            level = 0f;
            if (dust != null) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            RestoreFog();
            if (was) GameEvents.RaiseStormChanged(false);
        }

        void SaveFog()
        {
            if (fogSaved) return;
            fogSaved = true;
            fogWas = RenderSettings.fog;
            fogColorWas = RenderSettings.fogColor;
            fogDensityWas = RenderSettings.fogDensity;
            fogModeWas = RenderSettings.fogMode;
        }

        void RestoreFog()
        {
            if (!fogSaved) return;
            fogSaved = false;
            RenderSettings.fog = fogWas;
            RenderSettings.fogColor = fogColorWas;
            RenderSettings.fogDensity = fogDensityWas;
            RenderSettings.fogMode = fogModeWas;
        }

        void OnDisable() => StopImmediate();

        void Update()
        {
            level = Mathf.MoveTowards(level, Active ? 1f : 0f, Time.deltaTime / Mathf.Max(0.1f, fadeTime));
            if (fogSaved)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity * level;
                if (!Active && level <= 0f) RestoreFog();
            }
            if (dust != null && rover != null)
                dust.transform.position = rover.transform.position + rover.transform.forward * 6f + Vector3.up * 2f;

            if (!Active || rover == null || level < 0.5f) return;

            nextGust -= Time.deltaTime;
            if (nextGust <= 0f)
            {
                nextGust = Random.Range(gustInterval.x, gustInterval.y);
                float side = Random.value < 0.5f ? -1f : 1f;
                float k = Random.Range(0.5f, 1f);
                // Ветер «откуда» side — толкает кузов в противоположную сторону.
                rover.Body.AddForce(-rover.transform.right * side * gustForce * k * rover.Body.mass, ForceMode.Impulse);
                GameEvents.RaiseGust(side * k);
            }

            nextDischarge -= Time.deltaTime;
            if (nextDischarge <= 0f)
            {
                nextDischarge = Random.Range(dischargeInterval.x, dischargeInterval.y);
                Discharge();
            }
        }

        void Discharge()
        {
            float d = Random.Range(dischargeDistance.x, dischargeDistance.y);
            Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            Vector3 ground = rover.transform.position + dir * d;
            if (Physics.Raycast(ground + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, ~(1 << rover.gameObject.layer), QueryTriggerInteraction.Ignore))
                ground = hit.point;
            float strength = 1f - Mathf.InverseLerp(dischargeDistance.x, dischargeDistance.y, d) * 0.6f;
            SpawnBolt(ground + Vector3.up * Random.Range(12f, 20f), ground);
            GameEvents.RaiseStaticDischarge(ground + Vector3.up * 1.5f, strength);
        }

        void SpawnBolt(Vector3 from, Vector3 to)
        {
            var go = new GameObject("StaticBolt");
            var lr = go.AddComponent<LineRenderer>();
            const int n = 12;
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vector3 jitter = i == 0 || i == n - 1 ? Vector3.zero : Random.insideUnitSphere * 1.2f;
                lr.SetPosition(i, Vector3.Lerp(from, to, t) + jitter);
            }
            lr.widthMultiplier = 0.25f;
            lr.numCapVertices = 2;
            if (sparkMaterial != null) lr.sharedMaterial = sparkMaterial;
            lr.startColor = lr.endColor = new Color(0.7f, 0.85f, 1f);

            var light = go.AddComponent<Light>();
            go.transform.position = to + Vector3.up * 2f;
            light.type = LightType.Point;
            light.color = new Color(0.6f, 0.75f, 1f);
            light.range = 30f;
            light.intensity = 25f;
            go.AddComponent<BoltFade>();
        }

        void EnsureDust()
        {
            if (dust != null) return;
            var go = new GameObject("StormDust");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            dust = go.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 2.2f);
            main.startColor = new Color(0.55f, 0.5f, 0.45f, 0.35f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var emission = dust.emission;
            emission.rateOverTime = 160f;
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 6f, 40f);
            var vel = dust.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(8f, 14f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            vel.z = new ParticleSystem.MinMaxCurve(2f, 5f);
            var col = dust.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (dustMaterial != null) r.sharedMaterial = dustMaterial;
            go.SetActive(true);
        }

        class BoltFade : MonoBehaviour
        {
            Light l;
            LineRenderer lr;
            float t;
            void Awake() { l = GetComponent<Light>(); lr = GetComponent<LineRenderer>(); }
            void Update()
            {
                t += Time.deltaTime;
                // Двойная вспышка, как у настоящего разряда.
                float k = t < 0.08f ? 1f : t < 0.14f ? 0.2f : t < 0.22f ? 0.9f : Mathf.Max(0f, 1f - (t - 0.22f) * 4f);
                l.intensity = 25f * k;
                lr.widthMultiplier = 0.25f * k;
                if (t > 0.5f) Destroy(gameObject);
            }
        }
    }
}
