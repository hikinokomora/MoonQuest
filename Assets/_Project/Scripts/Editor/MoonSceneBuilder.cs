using System.Collections.Generic;
using System.IO;
using Mehatron.Gameplay;
using Mehatron.Haptics;
using Mehatron.Motion;
using Mehatron.UI;
using Mehatron.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Mehatron.EditorTools
{
    /// <summary>
    /// Собирает сцену Moon.unity с нуля: террейн с кратерами, небо, база, ровер с VR-ригом и HUD,
    /// образцы, трамплины, камни, менеджеры. Повторный запуск пересоздаёт сцену (ассеты настроек сохраняются).
    /// </summary>
    public static partial class MoonSceneBuilder
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/Moon.unity";
        const string RoverModelPath = Root + "/Models/Rover.fbx";
        const string CrystalModelPath = Root + "/Models/SampleCrystal.fbx";
        const int RoverLayer = 8;
        const float ConsoleTilt = 42f;  // наклон экрана пульта в модели ровера (Art/tools/rover.py: DA)
        const float WheelRadius = 0.5f; // радиус колеса в модели (Art/tools/rover.py: WR)

        const float TerrainSize = 800f;
        const float TerrainHeight = 80f;
        const float TerrainBase = 32f; // высота уровня базы над дном террейна

        struct Crater
        {
            public Vector2 c; public float r, depth, rim;
            public Crater(float x, float z, float r, float depth, float rim) { c = new Vector2(x, z); this.r = r; this.depth = depth; this.rim = rim; }
        }

        // Кратеры с образцами (первые пять) + декоративные.
        static readonly Crater[] SampleCraters =
        {
            new Crater(0f, 75f, 14f, 4f, 2f),
            new Crater(-75f, 135f, 20f, 6f, 3f),
            new Crater(65f, 175f, 16f, 5f, 2.5f),
            new Crater(125f, 80f, 22f, 7f, 3f),
            new Crater(-115f, 40f, 18f, 5f, 2.5f),
        };

        static readonly Crater[] DecoCraters =
        {
            new Crater(40f, -90f, 30f, 8f, 3f),
            new Crater(-160f, -70f, 25f, 6f, 2f),
            new Crater(180f, 210f, 35f, 10f, 4f),
            new Crater(-40f, 240f, 28f, 8f, 3f),
            new Crater(210f, -20f, 18f, 5f, 2f),
            new Crater(-210f, 170f, 40f, 12f, 4f),
            new Crater(120f, 280f, 22f, 6f, 3f),
        };

        [MenuItem("Mehatron/Build Moon Scene")]
        public static void Build()
        {
            EnsureFolders();
            EnsureRoverLayer();
            PrepareModelMaterials();

            LoadOrCreate<MotionSettings>(Root + "/Settings/MotionSettings.asset");
            LoadOrCreate<InputCalibration>(Root + "/Settings/InputCalibration.asset");
            AssetDatabase.SaveAssets();

            var mats = CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Random.InitState(20260922);
            SetupLighting(mats);
            var terrain = BuildTerrain(mats);
            float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;

            var baseZone = BuildBase(mats, H);
            BuildLandmarks(mats, H);
            var spawn = new GameObject("SpawnPoint").transform;
            spawn.position = new Vector3(0f, 0.1f, 4f);

            var samples = BuildSamples(mats, H);
            var ramps = BuildRamps(mats, H);
            BuildMissionPoints(mats, H, out var relays, out var pod, out var core);
            BuildRocks(mats, H);

            // Перезагружаем по пути: реимпорт текстур выше мог пересоздать объекты в памяти.
            var motionSettings = AssetDatabase.LoadAssetAtPath<MotionSettings>(Root + "/Settings/MotionSettings.asset");
            var inputCalibration = AssetDatabase.LoadAssetAtPath<InputCalibration>(Root + "/Settings/InputCalibration.asset");
            var rover = BuildRover(spawn, inputCalibration, mats, out var seatRig, out var hudRoot);

            // Менеджеры
            var game = new GameObject("Game");
            var oxygen = game.AddComponent<OxygenTimer>();
            var meteors = game.AddComponent<MeteorShower>();
            Set(meteors, "rover", rover);
            Set(meteors, "meteorMaterial", mats["Meteor"]);
            Set(meteors, "meteorMesh", ModelMesh("Meteor.fbx", "Meteor"));
            Set(meteors, "trailMaterial", mats["Trail"]);
            Set(meteors, "dustMaterial", mats["Dust"]);
            var hull = game.AddComponent<HullIntegrity>();
            var storm = game.AddComponent<DustStorm>();
            Set(storm, "rover", rover);
            Set(storm, "dustMaterial", mats["Dust"]);
            Set(storm, "sparkMaterial", mats["Spark"]);
            var flow = game.AddComponent<GameFlow>();
            Set(flow, "rover", rover);
            Set(flow, "spawnPoint", spawn);
            Set(flow, "baseZone", baseZone);
            Set(flow, "oxygen", oxygen);
            Set(flow, "meteorShower", meteors);
            Set(flow, "seatRig", seatRig);
            SetList(flow, "samples", samples.ConvertAll(s => (Object)s));
            Set(flow, "hull", hull);
            var mission = game.AddComponent<ExpeditionMission>();
            Set(mission, "flow", flow);
            Set(mission, "rover", rover);
            Set(mission, "oxygen", oxygen);
            Set(mission, "hull", hull);
            Set(mission, "meteors", meteors);
            Set(mission, "storm", storm);
            Set(mission, "baseZone", baseZone);
            SetList(mission, "ramps", ramps.ConvertAll(r => (Object)r));
            SetList(mission, "relays", relays.ConvertAll(r => (Object)r));
            Set(mission, "supplyPod", pod);
            Set(mission, "coreSample", core);
            Set(flow, "mission", mission);

            var motionGo = new GameObject("Motion");
            var debugPlatform = motionGo.AddComponent<DebugMotionPlatform>();
            var cueing = motionGo.AddComponent<MotionCueing>();
            Set(cueing, "settings", motionSettings);
            Set(cueing, "rover", rover);
            SetList(cueing, "outputs", new List<Object> { debugPlatform });

            var hapticsGo = new GameObject("Haptics");
            var debugVest = hapticsGo.AddComponent<DebugHapticsDevice>();
            // bHaptics TactSuit X16: сетка 4×4 на сторону, как в SDK2 (PlayMotors, 32 значения).
            var bhaptics = hapticsGo.AddComponent<BhapticsVestDevice>();
            foreach (var vest in new VestHapticsDevice[] { debugVest, bhaptics })
            {
                SetInt(vest, "columns", 4);
                SetInt(vest, "rows", 4);
            }
            var director = hapticsGo.AddComponent<HapticsDirector>();
            Set(director, "rover", rover);
            SetList(director, "devices", new List<Object> { debugVest, bhaptics });

            var autopilot = game.AddComponent<RoverAutopilot>();
            Set(autopilot, "flow", flow);

            var op = new GameObject("OperatorPanel").AddComponent<OperatorPanel>();
            Set(op, "autopilot", autopilot);
            Set(op, "gameFlow", flow);
            Set(op, "motion", cueing);
            Set(op, "rover", rover);
            Set(op, "seatRig", seatRig);

            BuildHud(hudRoot, flow);
            BuildMenu(rover, seatRig, flow, cueing, director, new List<Object> { debugVest, bhaptics }, oxygen, hull);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[MoonSceneBuilder] Сцена собрана: " + ScenePath);
        }

        // ---------------------------------------------------------------- подготовка

        static void EnsureFolders()
        {
            foreach (var f in new[] { "Scenes", "Settings", "Materials", "Textures", "Terrain", "Meshes" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{f}")) AssetDatabase.CreateFolder(Root, f);
        }

        static void EnsureRoverLayer()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            var el = layers.GetArrayElementAtIndex(RoverLayer);
            if (el.stringValue != "Rover")
            {
                el.stringValue = "Rover";
                tagManager.ApplyModifiedProperties();
            }
        }

        static void ExtractMaterials(string modelPath, string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            bool any = false;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (o is not Material m) continue;
                string dst = $"{folder}/{m.name}.mat";
                if (File.Exists(dst)) continue;
                string err = AssetDatabase.ExtractAsset(m, dst);
                if (!string.IsNullOrEmpty(err)) Debug.LogWarning(err);
                any = true;
            }
            if (!any) return;
            AssetDatabase.WriteImportSettingsIfDirty(modelPath);
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
        }

        static void SetEmission(string matPath, Color c)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) return;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m);
        }

        static PhysicsMaterial LoadOrCreatePhysicsMaterial(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m == null)
            {
                m = new PhysicsMaterial("RoverBody");
                AssetDatabase.CreateAsset(m, path);
            }
            m.dynamicFriction = 0.05f;
            m.staticFriction = 0.05f;
            m.frictionCombine = PhysicsMaterialCombine.Minimum;
            m.bounciness = 0f;
            EditorUtility.SetDirty(m);
            return m;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        // ---------------------------------------------------------------- материалы и текстуры

        static Dictionary<string, Material> CreateMaterials()
        {
            var d = new Dictionary<string, Material>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var sprite = Shader.Find("Sprites/Default");

            Material Lit(string name, Color c, float metal = 0f, float smooth = 0.2f, Color? emit = null)
            {
                var m = LoadOrNewMat(name, lit);
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Metallic", metal);
                m.SetFloat("_Smoothness", smooth);
                if (emit.HasValue)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emit.Value);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                d[name] = m;
                return m;
            }

            Material Glow(string name, Color c, Texture tex = null)
            {
                var m = LoadOrNewMat(name, sprite);
                m.color = c;
                if (tex != null) m.mainTexture = tex;
                d[name] = m;
                return m;
            }

            var soft = SoftCircleTexture();
            Lit("Rock", new Color(0.36f, 0.35f, 0.34f), 0f, 0.08f);
            Lit("BaseWhite", new Color(0.86f, 0.87f, 0.88f), 0.1f, 0.45f);
            Lit("BaseDark", new Color(0.18f, 0.19f, 0.21f), 0.5f, 0.4f);
            Lit("Solar", new Color(0.05f, 0.08f, 0.25f), 0.8f, 0.85f);
            Lit("Ramp", new Color(0.42f, 0.42f, 0.44f), 0.6f, 0.35f);
            Lit("RampStripe", new Color(0.95f, 0.45f, 0.08f), 0f, 0.3f, new Color(0.4f, 0.15f, 0.02f));
            Lit("Earth", new Color(0.2f, 0.4f, 0.85f), 0f, 0.6f, new Color(0.18f, 0.35f, 0.8f) * 1.3f);
            Lit("Meteor", new Color(0.3f, 0.2f, 0.15f), 0f, 0.2f, new Color(1f, 0.45f, 0.1f) * 4f);
            Glow("Trail", new Color(1f, 0.6f, 0.2f, 0.8f), soft);
            Glow("Dust", new Color(0.6f, 0.58f, 0.55f, 0.6f), soft);
            Glow("BeaconSample", new Color(0.3f, 0.85f, 1f, 0.22f));
            Glow("BeaconBase", new Color(0.3f, 1f, 0.4f, 0.18f));
            Glow("PadMarking", new Color(1f, 0.6f, 0.1f, 0.9f));
            Glow("BeaconRelay", new Color(0.3f, 1f, 0.45f, 0.22f));
            Glow("ZoneRelay", new Color(0.3f, 1f, 0.45f, 0.12f));
            Glow("BeaconPod", new Color(1f, 0.6f, 0.2f, 0.25f));
            Glow("ZonePod", new Color(1f, 0.6f, 0.2f, 0.14f));
            Glow("BeaconCore", new Color(0.75f, 0.45f, 1f, 0.25f));
            Glow("ZoneCore", new Color(0.75f, 0.45f, 1f, 0.14f));
            Glow("Spark", new Color(0.75f, 0.88f, 1f, 1f), soft);

            // Небо: панорама со звёздами
            var sky = LoadOrNewMat("StarSky", Shader.Find("Skybox/Panoramic"));
            sky.SetTexture("_MainTex", StarTexture());
            sky.SetFloat("_Exposure", 1.2f);
            d["StarSky"] = sky;
            return d;
        }

        static Material LoadOrNewMat(string name, Shader shader)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D SaveTexture(Texture2D tex, string name, TextureWrapMode wrap, bool mips = true)
        {
            string path = $"{Root}/Textures/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = wrap;
            imp.mipmapEnabled = mips;
            imp.alphaIsTransparency = true;
            imp.maxTextureSize = 4096;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D StarTexture()
        {
            const int w = 4096, h = 2048;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var px = new Color32[w * h];
            var rnd = new System.Random(42);
            for (int i = 0; i < 9000; i++)
            {
                int x = rnd.Next(w), y = rnd.Next(h);
                // равномерно по сфере: плотнее к экватору панорамы не делаем
                if (rnd.NextDouble() > Mathf.Cos((y / (float)h - 0.5f) * Mathf.PI)) continue;
                float b = Mathf.Pow((float)rnd.NextDouble(), 3f);
                byte v = (byte)(60 + b * 195);
                float tint = (float)rnd.NextDouble();
                var c = tint < 0.15f ? new Color32(v, (byte)(v * 0.85f), (byte)(v * 0.7f), 255)
                      : tint < 0.3f ? new Color32((byte)(v * 0.8f), (byte)(v * 0.9f), v, 255)
                      : new Color32(v, v, v, 255);
                px[y * w + x] = c;
                if (b > 0.6f && x + 1 < w && y + 1 < h)
                {
                    px[y * w + x + 1] = c; px[(y + 1) * w + x] = c; px[(y + 1) * w + x + 1] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexture(tex, "StarSky", TextureWrapMode.Clamp, false);
        }

        static Texture2D SoftCircleTexture()
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(s / 2f - 0.5f, s / 2f - 0.5f)) / (s / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            tex.Apply();
            return SaveTexture(tex, "SoftCircle", TextureWrapMode.Clamp);
        }

        static float TileNoise(int x, int y, int size, float freq)
        {
            float u = x / (float)size, v = y / (float)size;
            float a = Mathf.PerlinNoise(u * freq, v * freq);
            float b = Mathf.PerlinNoise((u - 1f) * freq, v * freq);
            float c = Mathf.PerlinNoise(u * freq, (v - 1f) * freq);
            float d = Mathf.PerlinNoise((u - 1f) * freq, (v - 1f) * freq);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        // ---------------------------------------------------------------- окружение

        static void SetupLighting(Dictionary<string, Material> mats)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(16f, 60f, 0f);

            RenderSettings.skybox = mats["StarSky"];
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.11f, 0.11f, 0.13f);
            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0.3f;

            var earth = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            earth.name = "Earth";
            Object.DestroyImmediate(earth.GetComponent<Collider>());
            earth.transform.position = Quaternion.Euler(-20f, 15f, 0f) * Vector3.forward * 1800f;
            earth.transform.localScale = Vector3.one * 130f;
            earth.GetComponent<Renderer>().sharedMaterial = mats["Earth"];
            earth.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = true, Vector3? euler = null)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
            if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        static List<SamplePickup> BuildSamples(Dictionary<string, Material> mats, System.Func<float, float, float> h)
        {
            var list = new List<SamplePickup>();
            var root = new GameObject("Samples").transform;
            var crystal = AssetDatabase.LoadAssetAtPath<GameObject>(CrystalModelPath);
            for (int i = 0; i < SampleCraters.Length; i++)
            {
                var c = SampleCraters[i].c;
                var go = new GameObject($"Sample_{i + 1}");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(c.x, h(c.x, c.y) + 0.2f, c.y);
                var trigger = go.AddComponent<SphereCollider>();
                trigger.radius = 6f;
                trigger.isTrigger = true;

                var visual = new GameObject("Visual").transform;
                visual.SetParent(go.transform, false);
                visual.localPosition = Vector3.up * 0.4f;
                var model = (GameObject)PrefabUtility.InstantiatePrefab(crystal);
                model.transform.SetParent(visual, false);
                model.transform.localScale = Vector3.one * 1.6f;

                var light = new GameObject("Glow").AddComponent<Light>();
                light.transform.SetParent(go.transform, false);
                light.transform.localPosition = Vector3.up * 1.5f;
                light.type = LightType.Point;
                light.color = new Color(0.3f, 0.85f, 1f);
                light.range = 14f;
                light.intensity = 3f;

                Prim(PrimitiveType.Cylinder, "Beacon", go.transform, Vector3.up * 40f, new Vector3(0.8f, 40f, 0.8f), mats["BeaconSample"], false)
                    .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

                var pickup = go.AddComponent<SamplePickup>();
                Set(pickup, "visual", visual);
                list.Add(pickup);
            }
            return list;
        }

        static Mesh[] RockMeshes()
        {
            var result = new Mesh[6];
            for (int k = 0; k < result.Length; k++)
            {
                string path = $"{Root}/Meshes/Rock_{k}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null) { result[k] = existing; continue; }

                Icosphere(2, out var verts, out var tris);
                float seed = k * 17.3f;
                for (int i = 0; i < verts.Count; i++)
                {
                    var v = verts[i];
                    float n = Mathf.PerlinNoise(v.x * 1.7f + seed, v.y * 1.7f + v.z * 0.9f) * 0.45f
                            + Mathf.PerlinNoise(v.z * 3.1f + seed, v.x * 3.1f) * 0.15f;
                    verts[i] = v * (0.75f + n);
                    if (verts[i].y < -0.2f) verts[i] = new Vector3(verts[i].x, -0.2f - (verts[i].y + 0.2f) * 0.3f, verts[i].z);
                }
                var fv = new Vector3[tris.Count];
                var fi = new int[tris.Count];
                for (int i = 0; i < tris.Count; i++) { fv[i] = verts[tris[i]]; fi[i] = i; }
                var mesh = new Mesh { name = $"Rock_{k}", vertices = fv, triangles = fi };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, path);
                result[k] = mesh;
            }
            return result;
        }

        static void Icosphere(int subdivisions, out List<Vector3> verts, out List<int> tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            tris = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var vs = verts;
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int idx)) return idx;
                    vs.Add(((vs[a] + vs[b]) * 0.5f).normalized);
                    cache[key] = vs.Count - 1;
                    return vs.Count - 1;
                }
                var next = new List<int>();
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = next;
            }
        }

        // ---------------------------------------------------------------- ровер

        static RoverController BuildRover(Transform spawn, InputCalibration calibration, Dictionary<string, Material> mats,
                                          out VRSeatRig seatRig, out Transform hudAnchor)
        {
            var root = new GameObject("Rover");
            root.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoverModelPath));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            // Распаковываем экземпляр модели, чтобы собрать иерархию подвески (в FBX все детали плоские).
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Transform Find(string n) => FindDeep(model.transform, n);
            foreach (var tag in new[] { "FL", "FR", "RL", "RR" })
            {
                var carrier = Find("Carrier_" + tag);
                foreach (var child in new[] { "Knuckle_" + tag, "Joint_U_" + tag, "Joint_L_" + tag })
                    Find(child)?.SetParent(carrier, true);
                Find("DamperTarget_" + tag)?.SetParent(Find("Arm_L_" + tag), true);
            }

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 900f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.6f;

            // Корпус: нижний «корыто» + задний блок оборудования + дуга (защищает при опрокидывании)
            var body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.78f, 0f);
            body.size = new Vector3(1.46f, 0.52f, 3.6f);
            var rear = root.AddComponent<BoxCollider>();
            rear.center = new Vector3(0f, 1.25f, -1.33f);
            rear.size = new Vector3(1.36f, 0.48f, 0.82f);
            var hoop = root.AddComponent<BoxCollider>();
            hoop.center = new Vector3(0f, 1.75f, -0.85f);
            hoop.size = new Vector3(1.3f, 0.9f, 0.12f);
            // Корпус скользкий: сев «на брюхо» на гребень кратера, ровер сползает, а не висит на трении.
            var slick = LoadOrCreatePhysicsMaterial(Root + "/Settings/RoverBody.physicMaterial");
            body.sharedMaterial = rear.sharedMaterial = hoop.sharedMaterial = slick;

            var input = root.AddComponent<RoverInput>();
            Set(input, "calibration", calibration);
            var rover = root.AddComponent<RoverController>();
            Set(rover, "input", input);

            // Якоря подвески над центрами колёс
            const float restLength = 0.45f;
            var names = new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            var so = new SerializedObject(rover);
            var wheels = so.FindProperty("wheels");
            wheels.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                var visual = Find(names[i]);
                var anchor = new GameObject("Anchor_" + names[i].Substring(6)).transform;
                anchor.SetParent(root.transform, false);
                anchor.localPosition = visual.localPosition + Vector3.up * restLength;
                var el = wheels.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("anchor").objectReferenceValue = anchor;
                el.FindPropertyRelative("visual").objectReferenceValue = visual;
                el.FindPropertyRelative("steer").boolValue = i < 2;
                el.FindPropertyRelative("rear").boolValue = i >= 2;
                string tag = names[i].Substring(6);
                el.FindPropertyRelative("carrier").objectReferenceValue = Find("Carrier_" + tag);
                el.FindPropertyRelative("knuckle").objectReferenceValue = Find("Knuckle_" + tag);
                var joint = Find("Joint_U_" + tag);
                if (joint != null)
                    el.FindPropertyRelative("kingpinOffset").floatValue = root.transform.InverseTransformPoint(joint.position).x - root.transform.InverseTransformPoint(visual.position).x;
            }
            so.FindProperty("restLength").floatValue = restLength;
            so.FindProperty("wheelRadius").floatValue = WheelRadius;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Видимая подвеска: рычаги к шаровым опорам, амортизаторы к нижним рычагам (порядок важен)
            var rig = root.AddComponent<SuspensionRig>();
            var rso = new SerializedObject(rig);
            var links = rso.FindProperty("links");
            var linkDefs = new List<(string part, string target, bool stretch)>();
            foreach (var n in names)
            {
                string tag = n.Substring(6);
                // Рычаги тоже тянутся: кулак ходит вертикально, и без растяжения конец рычага расходился бы с опорой.
                linkDefs.Add(("Arm_U_" + tag, "Joint_U_" + tag, true));
                linkDefs.Add(("Arm_L_" + tag, "Joint_L_" + tag, true));
            }
            foreach (var n in names)
            {
                string tag = n.Substring(6);
                linkDefs.Add(("Damper_" + tag, "DamperTarget_" + tag, true));
            }
            links.arraySize = linkDefs.Count;
            for (int i = 0; i < linkDefs.Count; i++)
            {
                var el = links.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("part").objectReferenceValue = Find(linkDefs[i].part);
                el.FindPropertyRelative("target").objectReferenceValue = Find(linkDefs[i].target);
                el.FindPropertyRelative("stretch").boolValue = linkDefs[i].stretch;
                if (Find(linkDefs[i].part) == null || Find(linkDefs[i].target) == null)
                    Debug.LogError($"[MoonSceneBuilder] Нет детали подвески {linkDefs[i].part} / {linkDefs[i].target}");
            }
            rso.ApplyModifiedPropertiesWithoutUndo();

            // Руль модели следует за реальным
            var steer = Find("SteeringWheel").gameObject.AddComponent<SteeringWheelVisual>();
            Set(steer, "input", input);
            Set(steer, "axisPoint", Find("SteeringWheel_Axis"));

            // Фары
            foreach (var n in new[] { "Headlight_L", "Headlight_R", "RoofLight_L", "RoofLight_R" })
            {
                var l = new GameObject(n + "_Spot").AddComponent<Light>();
                l.transform.SetParent(root.transform, false);
                l.transform.localPosition = Find(n).localPosition;
                l.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
                l.type = LightType.Spot;
                l.range = 45f;
                l.spotAngle = 55f;
                l.intensity = 4f;
                l.color = new Color(1f, 0.96f, 0.88f);
            }

            // VR-риг в кресле водителя
            var seat = new GameObject("SeatRig").transform;
            seat.SetParent(root.transform, false);
            seat.localPosition = Find("EyePoint").localPosition;
            var offset = new GameObject("CameraOffset").transform;
            offset.SetParent(seat, false);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(offset, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 3000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction("Head Position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction("Head Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            tpd.trackingStateInput = new InputActionProperty(new InputAction("Tracking State", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));

            seatRig = seat.gameObject.AddComponent<VRSeatRig>();
            Set(seatRig, "cameraOffset", offset);
            Set(seatRig, "headCamera", camGo.transform);
            Set(seatRig, "trackedPoseDriver", tpd);
            Set(seatRig, "input", input);

            SetLayerRecursive(root, RoverLayer);
            hudAnchor = Find("HUD_Anchor");
            return rover;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        // ---------------------------------------------------------------- HUD

        static void BuildHud(Transform anchor, GameFlow flow)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rover = anchor.root;

            var canvasGo = new GameObject("CockpitHUD", typeof(RectTransform));
            canvasGo.layer = RoverLayer;
            canvasGo.transform.SetParent(rover, false);
            // Экран панели наклонён к водителю; канвас читается вдоль своей +Z.
            canvasGo.transform.localPosition = anchor.localPosition;
            canvasGo.transform.localRotation = Quaternion.Euler(ConsoleTilt, 0f, 0f);
            canvasGo.transform.localPosition += canvasGo.transform.localRotation * Vector3.back * 0.004f;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(480f, 240f);
            rt.localScale = Vector3.one * 0.001f;

            var cyan = new Color(0.45f, 0.88f, 1f);

            var mission = UiRect("Mission", rt, Vector2.zero, Vector2.one).gameObject;
            var samples = UiText("Samples", mission.transform, new Vector2(0.03f, 0.8f), new Vector2(0.62f, 0.98f), font, 30, TextAnchor.MiddleLeft, cyan);
            var speed = UiText("Speed", mission.transform, new Vector2(0.62f, 0.8f), new Vector2(0.97f, 0.98f), font, 30, TextAnchor.MiddleRight, Color.white);
            var oxygenText = UiText("OxygenText", mission.transform, new Vector2(0.03f, 0.63f), new Vector2(0.6f, 0.8f), font, 26, TextAnchor.MiddleLeft, Color.white);
            var barBg = UiRect("OxygenBar", mission.transform, new Vector2(0.03f, 0.53f), new Vector2(0.72f, 0.63f));
            barBg.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            var fill = UiRect("Fill", barBg, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            fill.color = cyan;

            var hullText = UiText("HullText", mission.transform, new Vector2(0.03f, 0.42f), new Vector2(0.72f, 0.52f), font, 22, TextAnchor.MiddleLeft, Color.white);

            var arrow = UiText("CompassArrow", mission.transform, new Vector2(0.87f, 0.6f), new Vector2(0.87f, 0.6f), font, 64, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.15f));
            arrow.text = "▲";
            arrow.rectTransform.sizeDelta = new Vector2(80f, 80f);
            var targetLabel = UiText("TargetLabel", mission.transform, new Vector2(0.76f, 0.38f), new Vector2(0.98f, 0.46f), font, 16, TextAnchor.MiddleCenter, Color.white);
            targetLabel.text = "ЦЕЛЬ";

            var message = UiText("Message", mission.transform, new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.4f), font, 28, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
            message.resizeTextForBestFit = true;
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            message.verticalOverflow = VerticalWrapMode.Truncate;
            message.resizeTextMinSize = 12;
            message.resizeTextMaxSize = 28;

            var big = UiText("BigMessage", rt, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f), font, 32, TextAnchor.MiddleCenter, Color.white);
            big.resizeTextForBestFit = true;
            big.horizontalOverflow = HorizontalWrapMode.Wrap;
            big.verticalOverflow = VerticalWrapMode.Truncate;
            big.resizeTextMinSize = 12;
            big.resizeTextMaxSize = 32;

            var hud = canvasGo.AddComponent<CockpitHUD>();
            Set(hud, "flow", flow);
            Set(hud, "samplesText", samples);
            Set(hud, "oxygenText", oxygenText);
            Set(hud, "hullText", hullText);
            Set(hud, "speedText", speed);
            Set(hud, "messageText", message);
            Set(hud, "bigMessageText", big);
            Set(hud, "oxygenFill", fill);
            Set(hud, "compassArrow", arrow.rectTransform);
            Set(hud, "missionGroup", mission);
            SetLayerRecursive(canvasGo, RoverLayer);
        }

        static RectTransform UiRect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Text UiText(string name, Transform parent, Vector2 min, Vector2 max, Font font, int size, TextAnchor align, Color color)
        {
            var t = UiRect(name, parent, min, max).gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // ---------------------------------------------------------------- сериализация

        static void Set(Object target, string prop, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogError($"[MoonSceneBuilder] Нет поля {prop} у {target.GetType().Name}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInt(Object target, string prop, int value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(prop).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetList(Object target, string prop, IList<Object> items)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(prop);
            p.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
