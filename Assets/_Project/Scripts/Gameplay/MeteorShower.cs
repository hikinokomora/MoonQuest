using System.Collections.Generic;
using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.Gameplay
{
    /// <summary>
    /// Метеоритный дождь посреди миссии: метеоры падают вокруг ровера (чаще впереди, чтобы их было видно),
    /// взрывы дают направленные удары в жилет и толчки платформы через <see cref="GameEvents.Impact"/>.
    /// </summary>
    public class MeteorShower : MonoBehaviour
    {
        [SerializeField] RoverController rover;
        [SerializeField] Material meteorMaterial;
        [Tooltip("Меш метеорита (если не задан — сфера).")]
        [SerializeField] Mesh meteorMesh;
        [SerializeField] Material trailMaterial;
        [SerializeField] Material dustMaterial;

        [Header("Когда")]
        [SerializeField] float startDelay = 50f;
        [Tooltip("Или раньше — после N-го собранного образца.")]
        [SerializeField] int startAfterSamples = 2;
        [SerializeField] float duration = 22f;
        [SerializeField] Vector2 spawnInterval = new Vector2(1.6f, 3.2f);

        [Header("Где")]
        [SerializeField] Vector2 distanceRange = new Vector2(18f, 65f);
        [SerializeField] float forwardConeAngle = 75f;
        [SerializeField] float spawnHeight = 140f;
        [SerializeField] float fallTime = 2.2f;
        [SerializeField] LayerMask groundMask = 1;

        [Header("Сила")]
        [Tooltip("Дистанция, на которой удар ощущается в полную силу.")]
        [SerializeField] float fullImpactDistance = 12f;
        [SerializeField] float maxImpactDistance = 60f;
        [SerializeField] float explosionForce = 2500f;

        public bool IsActive { get; private set; }

        bool armed;
        float elapsed, activeTime, nextSpawn;
        readonly List<GameObject> live = new List<GameObject>();

        void OnEnable() => GameEvents.SampleCollected += OnSample;
        void OnDisable() => GameEvents.SampleCollected -= OnSample;

        public void Arm()
        {
            StopShower();
            armed = true;
            elapsed = 0f;
        }

        public void StopShower()
        {
            armed = false;
            IsActive = false;
            foreach (var go in live)
                if (go != null) Destroy(go);
            live.Clear();
        }

        void OnSample(int collected, int total)
        {
            if (armed && !IsActive && collected >= startAfterSamples) Begin();
        }

        /// <summary>Запустить дождь сразу (по сценарию миссии) на заданное время.</summary>
        public void Begin(float seconds)
        {
            duration = seconds;
            Begin();
        }

        /// <summary>Перестать запускать новые метеоры; уже летящие долетят.</summary>
        public void StopSpawning()
        {
            armed = false;
            IsActive = false;
        }

        void Begin()
        {
            armed = false;
            IsActive = true;
            activeTime = 0f;
            nextSpawn = 1.5f;
            GameEvents.RaiseMeteorShowerStarted();
        }

        void Update()
        {
            if (armed)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= startDelay) Begin();
                return;
            }
            if (!IsActive || rover == null) return;

            activeTime += Time.deltaTime;
            if (activeTime >= duration)
            {
                IsActive = false;
                return;
            }
            nextSpawn -= Time.deltaTime;
            if (nextSpawn <= 0f)
            {
                nextSpawn = Random.Range(spawnInterval.x, spawnInterval.y);
                Spawn();
            }
            live.RemoveAll(g => g == null);
        }

        void Spawn()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(rover.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            // Треть метеоров — с любой стороны, чтобы жилет «бил» и сзади.
            float cone = Random.value < 0.33f ? 180f : forwardConeAngle;
            Vector3 dir = Quaternion.Euler(0f, Random.Range(-cone, cone), 0f) * fwd;
            Vector3 target = rover.transform.position + dir * Random.Range(distanceRange.x, distanceRange.y);
            if (Physics.Raycast(target + Vector3.up * 300f, Vector3.down, out RaycastHit hit, 600f, groundMask, QueryTriggerInteraction.Ignore))
                target = hit.point;

            Vector3 start = target + Vector3.up * spawnHeight + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * spawnHeight * 0.6f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Meteor";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * Random.Range(0.8f, 1.6f);
            go.transform.rotation = Random.rotation;
            if (meteorMesh != null) go.GetComponent<MeshFilter>().sharedMesh = meteorMesh;
            if (meteorMaterial != null) go.GetComponent<Renderer>().sharedMaterial = meteorMaterial;
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.6f;
            trail.startWidth = go.transform.localScale.x * 1.2f;
            trail.endWidth = 0f;
            if (trailMaterial != null) trail.sharedMaterial = trailMaterial;
            var m = go.AddComponent<Meteor>();
            m.Launch(start, target, fallTime, Explode);
            live.Add(go);
        }

        void Explode(Vector3 point)
        {
            SpawnDust(point);
            SpawnFlash(point);
            if (rover == null) return;
            float d = Vector3.Distance(point, rover.transform.position);
            if (d > maxImpactDistance) return;
            float strength = 1f - Mathf.InverseLerp(fullImpactDistance, maxImpactDistance, d);
            GameEvents.RaiseImpact(point, strength);
            GameEvents.RaiseMeteorExploded(point, strength);
            if (d < fullImpactDistance * 1.5f)
                rover.Body.AddExplosionForce(explosionForce * rover.Body.mass * 0.01f, point, fullImpactDistance * 1.5f, 0.5f, ForceMode.Impulse);
        }

        void SpawnDust(Vector3 point)
        {
            var go = new GameObject("MeteorDust");
            go.SetActive(false);
            go.transform.position = point;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = true;
            main.duration = 1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f);
            main.startSize = new ParticleSystem.MinMaxCurve(1f, 3.5f);
            main.startColor = new Color(0.55f, 0.53f, 0.5f, 0.9f);
            main.gravityModifier = 0.25f;
            main.maxParticles = 200;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (dustMaterial != null) r.sharedMaterial = dustMaterial;
            go.SetActive(true);
        }

        static void SpawnFlash(Vector3 point)
        {
            var go = new GameObject("MeteorFlash");
            go.transform.position = point + Vector3.up * 2f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.3f);
            light.range = 40f;
            light.intensity = 30f;
            go.AddComponent<FlashFade>();
        }

        class FlashFade : MonoBehaviour
        {
            Light l;
            void Awake() => l = GetComponent<Light>();
            void Update()
            {
                l.intensity = Mathf.MoveTowards(l.intensity, 0f, 60f * Time.deltaTime);
                if (l.intensity <= 0f) Destroy(gameObject);
            }
        }
    }
}
