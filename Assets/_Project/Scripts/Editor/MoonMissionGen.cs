using System;
using System.Collections.Generic;
using Mehatron.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Mehatron.EditorTools
{
    /// <summary>Точки экспедиции: ретрансляторы, площадка сброса контейнера (за бороздой), керн в Большом кратере.</summary>
    public static partial class MoonSceneBuilder
    {
        // Маршрут: база → образцы → трамплины → R1 (запад) → R2 (северо-запад) → R3 (восток) → контейнер (юг, за бороздой)
        // → керн (Большой кратер, северо-восток) → база. ~2.5 км езды + задания ≈ 10–15 минут.
        static readonly Vector2[] RelaySpots = { new Vector2(-185f, -10f), new Vector2(-110f, 260f), new Vector2(240f, 120f) };
        static readonly Vector2 PodSpot = new Vector2(110f, -228f);
        static readonly Vector2 CoreSpot = new Vector2(160f, 212f);

        /// <summary>Точки, вокруг которых не ставим камни и мелкие кратеры.</summary>
        static bool NearMissionSpot(Vector2 p, float extra = 0f)
        {
            foreach (var r in RelaySpots) if (Vector2.Distance(p, r) < 14f + extra) return true;
            return Vector2.Distance(p, PodSpot) < 14f + extra || Vector2.Distance(p, CoreSpot) < 12f + extra;
        }

        static GameObject ZoneMarker(Transform parent, float radius, Material beacon, Material disc, Color light, float beaconHeight = 40f)
        {
            var marker = new GameObject("Marker");
            marker.transform.SetParent(parent, false);
            Prim(PrimitiveType.Cylinder, "Beacon", marker.transform, Vector3.up * beaconHeight, new Vector3(0.9f, beaconHeight, 0.9f), beacon, false)
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Prim(PrimitiveType.Cylinder, "Zone", marker.transform, Vector3.up * 0.15f, new Vector3(radius * 2f, 0.02f, radius * 2f), disc, false)
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var l = new GameObject("Glow").AddComponent<Light>();
            l.transform.SetParent(marker.transform, false);
            l.transform.localPosition = Vector3.up * 3f;
            l.type = LightType.Point;
            l.color = light;
            l.range = radius * 2.2f;
            l.intensity = 3f;
            return marker;
        }

        /// <summary>Анимируемые объекты (падают, вырастают) не должны попадать в static batching.</summary>
        static GameObject Dynamic(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            return go;
        }

        static MissionPoint NewPoint(string name, Transform root, Vector3 pos, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = radius;
            return go.AddComponent<MissionPoint>();
        }

        static void BuildMissionPoints(Dictionary<string, Material> mats, Func<float, float, float> h,
                                       out List<MissionPoint> relays, out MissionPoint pod, out MissionPoint core)
        {
            var root = new GameObject("Mission").transform;
            Vector3 P(Vector2 v) => new Vector3(v.x, h(v.x, v.y), v.y);
            var green = new Color(0.3f, 1f, 0.45f);

            relays = new List<MissionPoint>();
            for (int i = 0; i < RelaySpots.Length; i++)
            {
                var pt = NewPoint($"Relay_{i + 1}", root, P(RelaySpots[i]), 9f);
                var marker = ZoneMarker(pt.transform, 9f, mats["BeaconRelay"], mats["ZoneRelay"], green);
                // Ретранслятор без коллайдера: появляется прямо рядом со стоящим ровером.
                var model = Dynamic(PlaceModel("Relay.fbx", "RelayModel", pt.transform, pt.transform.position + new Vector3(4f, 0f, 3f), i * 70f + 20f, 1.4f, false));
                var signal = new GameObject("SignalLight").AddComponent<Light>();
                signal.transform.SetParent(model.transform, false);
                signal.transform.localPosition = Vector3.up * 5.6f;
                signal.type = LightType.Point;
                signal.color = green;
                signal.range = 18f;
                signal.intensity = 4f;
                Set(pt, "marker", marker);
                Set(pt, "reveal", model.transform);
                relays.Add(pt);
            }

            // Контейнер: груз падает с орбиты в отдельном дочернем объекте, триггер остаётся на грунте.
            pod = NewPoint("SupplyPod", root, P(PodSpot), 6f);
            var podMarker = ZoneMarker(pod.transform, 6f, mats["BeaconPod"], mats["ZonePod"], new Color(1f, 0.6f, 0.2f), 60f);
            var body = new GameObject("Body").transform;
            body.SetParent(pod.transform, false);
            Dynamic(PlaceModel("OxygenCache.fbx", "OxygenCache", body, pod.transform.position, 25f, 1.3f, false));
            var trail = body.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.8f;
            trail.startWidth = 3f;
            trail.endWidth = 0f;
            trail.sharedMaterial = mats["Trail"];
            var podLight = new GameObject("Glow").AddComponent<Light>();
            podLight.transform.SetParent(body, false);
            podLight.transform.localPosition = Vector3.up * 2.5f;
            podLight.type = LightType.Point;
            podLight.color = new Color(0.3f, 0.85f, 1f);
            podLight.range = 12f;
            podLight.intensity = 3f;
            Set(pod, "marker", podMarker);
            Set(pod, "hideOnComplete", body.gameObject);
            Set(pod, "dropBody", body);

            // Керн: крупный кристалл в Большом кратере; после бурения на его месте встаёт флаг экспедиции.
            core = NewPoint("CoreSample", root, P(CoreSpot), 8f);
            var coreMarker = ZoneMarker(core.transform, 8f, mats["BeaconCore"], mats["ZoneCore"], new Color(0.75f, 0.45f, 1f), 70f);
            var crystal = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(CrystalModelPath));
            crystal.name = "CoreCrystal";
            crystal.transform.SetParent(core.transform, false);
            crystal.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            crystal.transform.localScale = Vector3.one * 4f;
            var flag = Dynamic(PlaceModel("Flag.fbx", "ExpeditionFlag", core.transform, core.transform.position, 140f, 1.6f, false));
            Set(core, "marker", coreMarker);
            Set(core, "hideOnComplete", crystal);
            Set(core, "reveal", flag.transform);
        }
    }
}
