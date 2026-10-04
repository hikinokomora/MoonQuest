using System;
using System.Collections.Generic;
using System.IO;
using Mehatron.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;
using Object = UnityEngine.Object;

namespace Mehatron.EditorTools
{
    /// <summary>
    /// Генерация мира: модели из Blender, материалы, процедурные текстуры грунта, рельеф, раскраска, россыпи камней, база.
    /// </summary>
    public static partial class MoonSceneBuilder
    {
        static readonly string[] ModelFiles =
        {
            "Rover.fbx", "SampleCrystal.fbx", "Rocks.fbx", "Meteor.fbx", "Habitat.fbx", "Dome.fbx", "CommsTower.fbx",
            "SolarArray.fbx", "LandingPad.fbx", "Lander.fbx", "Ramp.fbx", "Crate.fbx", "Flag.fbx", "Relay.fbx", "OxygenCache.fbx",
        };

        static readonly Dictionary<string, Color> EmissiveMaterials = new Dictionary<string, Color>
        {
            { "Rover_Light", new Color(1f, 0.95f, 0.85f) * 4f },
            { "Rover_Screen", new Color(0.05f, 0.2f, 0.35f) },
            { "Rover_Gauge", new Color(0.9f, 0.85f, 0.6f) * 1.2f },
            { "Sample_Crystal", new Color(0.2f, 0.8f, 1f) * 3f },
            { "Base_Window", new Color(1f, 0.75f, 0.45f) * 2f },
            { "Base_RedLight", new Color(1f, 0.1f, 0.05f) * 6f },
            { "Base_Lamp", new Color(1f, 0.8f, 0.5f) * 5f },
            { "Base_Glass", new Color(0.15f, 0.35f, 0.55f) * 0.5f },
            { "Mission_Signal", new Color(0.2f, 1f, 0.35f) * 5f },
            { "Mission_O2", new Color(0.2f, 0.8f, 1f) * 4f },
        };

        static readonly Vector2 LanderPos = new Vector2(-42f, 50f);
        static readonly Crater BigCrater = new Crater(175f, 235f, 55f, 16f, 6f);

        static readonly (Vector2 p, float yaw)[] RampSpots =
        {
            (new Vector2(0f, 38f), 0f),
            (new Vector2(-38f, 95f), -35f),
            (new Vector2(38f, 125f), 20f),
            (new Vector2(88f, 58f), 65f),
            (new Vector2(-62f, 22f), -80f),
            (new Vector2(0f, 150f), 10f),
        };

        // ---------------------------------------------------------------- модели и материалы

        static string ModelPath(string file) => $"{Root}/Models/{file}";

        static Mesh ModelMesh(string file, string meshName)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath(file)))
                if (o is Mesh m && m.name == meshName) return m;
            Debug.LogWarning($"[MoonSceneBuilder] Нет меша {meshName} в {file}");
            return null;
        }

        static List<Mesh> ModelMeshes(string file, string prefix)
        {
            var list = new List<Mesh>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath(file)))
                if (o is Mesh m && m.name.StartsWith(prefix)) list.Add(m);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        static GameObject PlaceModel(string file, string name, Transform parent, Vector3 pos, float yaw, float scale = 1f, bool collider = true)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(file));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            if (collider)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            foreach (var t in go.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        /// <summary>Выносим материалы из всех FBX, включаем эмиссию, вешаем текстуры на камни.</summary>
        static void PrepareModelMaterials()
        {
            string folder = Root + "/Materials/Models";
            foreach (var f in ModelFiles)
                if (File.Exists(ModelPath(f))) ExtractMaterials(ModelPath(f), folder);

            foreach (var kv in EmissiveMaterials)
                foreach (var m in FindMaterials(kv.Key))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", kv.Value);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    EditorUtility.SetDirty(m);
                }
            foreach (var m in FindMaterials("Base_Glass"))
            {
                m.SetFloat("_Smoothness", 0.9f);
                EditorUtility.SetDirty(m);
            }

            var (rockAlbedo, rockNormal) = RockTextures();
            void Textured(string name, Color tint, float normalScale)
            {
                foreach (var m in FindMaterials(name))
                {
                    m.SetTexture("_BaseMap", rockAlbedo);
                    m.SetColor("_BaseColor", tint);
                    m.SetTexture("_BumpMap", rockNormal);
                    m.SetFloat("_BumpScale", normalScale);
                    m.EnableKeyword("_NORMALMAP");
                    m.SetFloat("_Smoothness", 0.08f);
                    m.SetFloat("_Metallic", 0f);
                    EditorUtility.SetDirty(m);
                }
            }
            Textured("Rock", new Color(0.95f, 0.93f, 0.9f), 1.2f);
            Textured("Sample_Rock", new Color(0.8f, 0.78f, 0.76f), 1f);
            Textured("MeteorRock", new Color(0.45f, 0.35f, 0.3f), 1.5f);
            AssetDatabase.SaveAssets();
        }

        static List<Material> FindMaterials(string name)
        {
            var list = new List<Material>();
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Material", new[] { Root + "/Materials" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name == name) list.Add(m);
            }
            return list;
        }

        // ---------------------------------------------------------------- процедурные текстуры

        /// <summary>
        /// Альбедо + нормаль из поля высот. Генерируются один раз; чтобы пересоздать — удалить PNG в Textures.
        /// </summary>
        static (Texture2D, Texture2D) SurfaceTextures(string name, int size, Func<int, int, float> height,
                                                     Func<float, int, int, Color> color, float normalStrength)
        {
            string albedoPath = $"{Root}/Textures/{name}.png";
            string normalPath = $"{Root}/Textures/{name}_N.png";
            if (File.Exists(albedoPath) && File.Exists(normalPath))
                return (AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath), AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));

            var h = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    h[y * size + x] = height(x, y);

            var albedo = new Texture2D(size, size, TextureFormat.RGB24, true);
            var normal = new Texture2D(size, size, TextureFormat.RGB24, true);
            var ca = new Color[size * size];
            var cn = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float hl = h[y * size + (x - 1 + size) % size], hr = h[y * size + (x + 1) % size];
                    float hd = h[((y - 1 + size) % size) * size + x], hu = h[((y + 1) % size) * size + x];
                    var n = new Vector3((hl - hr) * normalStrength, (hd - hu) * normalStrength, 1f).normalized;
                    cn[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f);
                    ca[y * size + x] = color(h[y * size + x], x, y);
                }
            albedo.SetPixels(ca);
            normal.SetPixels(cn);
            File.WriteAllBytes(albedoPath, albedo.EncodeToPNG());
            File.WriteAllBytes(normalPath, normal.EncodeToPNG());
            Object.DestroyImmediate(albedo);
            Object.DestroyImmediate(normal);

            AssetDatabase.ImportAsset(albedoPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceUpdate);
            var ia = (TextureImporter)AssetImporter.GetAtPath(albedoPath);
            ia.wrapMode = TextureWrapMode.Repeat;
            ia.anisoLevel = 4;
            ia.SaveAndReimport();
            var inn = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            inn.textureType = TextureImporterType.NormalMap;
            inn.wrapMode = TextureWrapMode.Repeat;
            inn.anisoLevel = 4;
            inn.SaveAndReimport();
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath), AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        }

        static float Grain(int x, int y, int seed)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + seed * 982451653;
                n = (n ^ (n >> 13)) * 1274126177;
                return ((n ^ (n >> 16)) & 0xffff) / 65535f;
            }
        }

        /// <summary>Мелкие кратеры на тайлящейся текстуре (с переносом через края).</summary>
        static float[] Craterlets(int size, int count, float minR, float maxR, int seed)
        {
            var field = new float[size * size];
            var rnd = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float cx = (float)rnd.NextDouble() * size, cy = (float)rnd.NextDouble() * size;
                float r = minR + (float)Math.Pow(rnd.NextDouble(), 3) * (maxR - minR);
                int ir = Mathf.CeilToInt(r * 1.6f);
                for (int dy = -ir; dy <= ir; dy++)
                    for (int dx = -ir; dx <= ir; dx++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                        if (d > 1.6f) continue;
                        float v = d < 1f ? -(1f - d * d) : 0f;
                        float e = (d - 1f) / 0.25f;
                        v += 0.35f * Mathf.Exp(-e * e);
                        int px = ((int)cx + dx + size) % size, py = ((int)cy + dy + size) % size;
                        field[py * size + px] += v * r * 0.06f;
                    }
            }
            return field;
        }

        static (Texture2D, Texture2D) RegolithLayerTex()
        {
            const int s = 512;
            var cr = Craterlets(s, 70, 3f, 18f, 1);
            return SurfaceTextures("T_Regolith", s,
                (x, y) => TileNoise(x, y, s, 6f) * 0.5f + TileNoise(x, y, s, 20f) * 0.3f + TileNoise(x, y, s, 60f) * 0.15f + cr[y * s + x] + Grain(x, y, 1) * 0.08f,
                (h, x, y) => { float v = 0.36f + (h - 0.5f) * 0.18f + Grain(x, y, 2) * 0.06f; return new Color(v * 1.02f, v, v * 0.96f); },
                1.4f);
        }

        static (Texture2D, Texture2D) MareLayerTex()
        {
            const int s = 512;
            var cr = Craterlets(s, 60, 2f, 10f, 3);
            return SurfaceTextures("T_Mare", s,
                (x, y) => TileNoise(x, y, s, 4f) * 0.4f + TileNoise(x, y, s, 30f) * 0.2f + cr[y * s + x] + Grain(x, y, 3) * 0.05f,
                (h, x, y) => { float v = 0.19f + (h - 0.4f) * 0.08f + Grain(x, y, 4) * 0.05f; return new Color(v * 0.98f, v, v * 1.03f); },
                2f);
        }

        static (Texture2D, Texture2D) GravelLayerTex()
        {
            const int s = 512, cells = 22;
            var rnd = new System.Random(5);
            var pts = new Vector2[cells * cells];
            var tone = new float[cells * cells];
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i] = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
                tone[i] = (float)rnd.NextDouble();
            }
            float cellSize = s / (float)cells;
            (float d, int id) Nearest(int x, int y)
            {
                int gx = (int)(x / cellSize), gy = (int)(y / cellSize);
                float best = 1e9f; int id = 0;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int cx = (gx + ox + cells) % cells, cy = (gy + oy + cells) % cells;
                        var p = (new Vector2(gx + ox, gy + oy) + pts[cy * cells + cx]) * cellSize;
                        float d = Vector2.Distance(p, new Vector2(x, y));
                        if (d < best) { best = d; id = cy * cells + cx; }
                    }
                return (best / cellSize, id);
            }
            return SurfaceTextures("T_Gravel", s,
                (x, y) => { var (d, id) = Nearest(x, y); return Mathf.Clamp01(1f - d * 1.5f) * (0.6f + tone[id] * 0.4f) + Grain(x, y, 5) * 0.05f; },
                (h, x, y) => { var (d, id) = Nearest(x, y); float v = h > 0.05f ? 0.26f + tone[id] * 0.2f : 0.22f; v += Grain(x, y, 6) * 0.05f; return new Color(v, v * 0.98f, v * 0.95f); },
                6f);
        }

        static (Texture2D, Texture2D) EjectaLayerTex()
        {
            const int s = 512;
            return SurfaceTextures("T_Ejecta", s,
                (x, y) => TileNoise(x, y, s, 10f) * 0.3f + TileNoise(x, y, s, 50f) * 0.2f + Grain(x, y, 7) * 0.1f,
                (h, x, y) => { float v = 0.52f + (h - 0.3f) * 0.1f + Grain(x, y, 8) * 0.05f; return new Color(v, v * 0.99f, v * 0.97f); },
                1.5f);
        }

        static (Texture2D, Texture2D) RockTextures()
        {
            const int s = 512;
            var cr = Craterlets(s, 80, 1f, 5f, 9);
            return SurfaceTextures("T_Rock", s,
                (x, y) => TileNoise(x, y, s, 5f) * 0.45f + TileNoise(x, y, s, 18f) * 0.3f + TileNoise(x, y, s, 70f) * 0.2f + cr[y * s + x] + Grain(x, y, 9) * 0.08f,
                (h, x, y) => { float v = 0.32f + (h - 0.5f) * 0.3f + Grain(x, y, 10) * 0.07f; return new Color(v * 1.02f, v, v * 0.95f); },
                5f);
        }

        // ---------------------------------------------------------------- рельеф

        static Terrain BuildTerrain(Dictionary<string, Material> mats)
        {
            var data = new TerrainData { heightmapResolution = 513, alphamapResolution = 512 };
            data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
            // Ассет создаём сразу: иначе splat-карты (под-ассеты) теряются и раскраска сбрасывается.
            AssetDatabase.DeleteAsset(Root + "/Terrain/MoonTerrain.asset");
            AssetDatabase.CreateAsset(data, Root + "/Terrain/MoonTerrain.asset");
            int res = data.heightmapResolution;
            var heights = new float[res, res];
            var ejecta = new float[res, res];
            var interior = new float[res, res];

            // Кратеры: игровые, декоративные, ориентир и сотни мелких (степенное распределение размеров)
            var all = new List<Crater>(SampleCraters);
            all.AddRange(DecoCraters);
            all.Add(BigCrater);
            var rnd = new System.Random(3);
            int guard = 0;
            while (all.Count < 380 && guard++ < 5000)
            {
                float x = (float)(rnd.NextDouble() - 0.5) * 720f, z = (float)(rnd.NextDouble() - 0.5) * 720f;
                var p = new Vector2(x, z);
                if (p.magnitude < 45f || Vector2.Distance(p, LanderPos) < 18f) continue;
                bool blocked = false;
                foreach (var c in SampleCraters) if (Vector2.Distance(p, c.c) < c.r + 6f) blocked = true;
                foreach (var r in RampSpots) if (Vector2.Distance(p, r.p) < 12f) blocked = true;
                if (NearMissionSpot(p, 4f)) blocked = true;
                if (blocked) continue;
                bool medium = rnd.NextDouble() < 0.12;
                float rad = medium ? 8f + (float)rnd.NextDouble() * 12f : 1.5f + (float)Math.Pow(rnd.NextDouble(), 2) * 6.5f;
                all.Add(new Crater(x, z, rad, rad * (0.28f + (float)rnd.NextDouble() * 0.12f), rad * 0.1f));
            }

            // Лучи выбросов: для крупных кратеров свои параметры
            var rays = new Dictionary<int, (int n, float phase)>();
            for (int i = 0; i < all.Count; i++)
                if (all[i].r >= 12f) rays[i] = (10 + rnd.Next(10), (float)rnd.NextDouble() * 6.28f);

            // Извилистая борозда (rille) на юге карты
            var rille = new List<Vector2>();
            for (int i = 0; i <= 120; i++)
            {
                float t = i / 120f;
                rille.Add(new Vector2(-360f + 700f * t, -185f + 38f * Mathf.Sin(t * 9f) + 15f * Mathf.Sin(t * 23f)));
            }

            for (int iz = 0; iz < res; iz++)
                for (int ix = 0; ix < res; ix++)
                {
                    float x = ix / (float)(res - 1) * TerrainSize - TerrainSize / 2f;
                    float z = iz / (float)(res - 1) * TerrainSize - TerrainSize / 2f;
                    var p = new Vector2(x, z);
                    float h = (Mathf.PerlinNoise(x * 0.008f + 10f, z * 0.008f + 10f) - 0.5f) * 8f
                            + (Mathf.PerlinNoise(x * 0.03f + 3f, z * 0.03f + 7f) - 0.5f) * 1.6f
                            + (Mathf.PerlinNoise(x * 0.13f, z * 0.13f) - 0.5f) * 0.35f
                            + (Mathf.PerlinNoise(x * 0.45f + 50f, z * 0.45f) - 0.5f) * 0.08f;
                    float ej = 0f, inside = 0f;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var c = all[i];
                        float dx = x - c.c.x, dz = z - c.c.y;
                        float reach = c.r * (rays.ContainsKey(i) ? 3.5f : 2f);
                        if (dx * dx + dz * dz > reach * reach) continue;
                        float r = Mathf.Sqrt(dx * dx + dz * dz) / c.r;
                        if (r < 1f)
                        {
                            h -= c.depth * (1f - r * r);
                            inside = Mathf.Max(inside, 1f - r);
                        }
                        float e = (r - 1f) / 0.28f;
                        h += c.rim * Mathf.Exp(-e * e);
                        if (rays.TryGetValue(i, out var ray))
                        {
                            float halo = Mathf.Exp(-Mathf.Pow((r - 1.15f) / 0.45f, 2f));
                            float ang = Mathf.Atan2(dz, dx);
                            float streak = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * ray.n * 0.5f + ray.phase + Mathf.PerlinNoise(r * 2f, i) * 2f)), 12f);
                            float fade = r > 1f ? Mathf.Pow(Mathf.Clamp01(1f - (r - 1f) / 2.5f), 1.5f) : 0f;
                            ej = Mathf.Max(ej, Mathf.Max(halo * 0.8f * (r > 0.9f ? 1f : 0f), streak * fade));
                        }
                    }
                    // Центральная горка и террасы большого кратера
                    {
                        float r = Vector2.Distance(p, BigCrater.c) / BigCrater.r;
                        if (r < 1f)
                        {
                            h += 9f * Mathf.Exp(-Mathf.Pow(r / 0.16f, 2f));
                            h += 1.2f * Mathf.Sin(r * 18f) * Mathf.SmoothStep(0.5f, 0.95f, r);
                        }
                    }
                    // Борозда
                    if (z > -260f && z < -110f)
                    {
                        float best = 1e9f;
                        for (int k = 0; k < rille.Count - 1; k++)
                        {
                            float d = DistToSegment(p, rille[k], rille[k + 1]);
                            if (d < best) best = d;
                        }
                        h -= 6f * Mathf.Exp(-Mathf.Pow(best / 7f, 2f));
                        h += 0.8f * Mathf.Exp(-Mathf.Pow((best - 11f) / 3f, 2f));
                        if (best < 9f) inside = Mathf.Max(inside, 0.6f);
                    }
                    // Ровные площадки: база и посадочный модуль
                    float d0 = p.magnitude;
                    float flat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(22f, 45f, d0));
                    flat *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9f, 20f, Vector2.Distance(p, LanderPos)));
                    h *= flat;
                    if (d0 < 40f) { ej *= flat; inside *= flat; }
                    // Горы по краю карты
                    float edge = Mathf.InverseLerp(300f, 395f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
                    h += edge * edge * 45f * (0.6f + 0.4f * Mathf.PerlinNoise(x * 0.02f, z * 0.02f));

                    heights[iz, ix] = Mathf.Clamp01((h + TerrainBase) / TerrainHeight);
                    ejecta[iz, ix] = Mathf.Clamp01(ej);
                    interior[iz, ix] = Mathf.Clamp01(inside);
                }
            data.SetHeights(0, 0, heights);
            // Сразу на диск: реимпорт текстур ниже может перечитать ассет, и несохранённые высоты обнулятся.
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);

            // Слои грунта
            TerrainLayer Layer(string n, (Texture2D a, Texture2D nrm) t, float tile, float smooth)
            {
                string path = $"{Root}/Terrain/{n}.terrainlayer";
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
                l.diffuseTexture = t.a;
                l.normalMapTexture = t.nrm;
                l.normalScale = 1f;
                l.tileSize = new Vector2(tile, tile);
                l.smoothness = smooth;
                l.metallic = 0f;
                EditorUtility.SetDirty(l);
                return l;
            }
            data.terrainLayers = new[]
            {
                Layer("Regolith", RegolithLayerTex(), 7f, 0.05f),
                Layer("Mare", MareLayerTex(), 9f, 0.08f),
                Layer("Gravel", GravelLayerTex(), 3.5f, 0.04f),
                Layer("Ejecta", EjectaLayerTex(), 11f, 0.03f),
            };

            // Раскраска: уклон → щебень, лучи/кольца выбросов → светлый, «моря» и дно кратеров → тёмный
            int ar = data.alphamapResolution;
            var alpha = new float[ar, ar, 4];
            float cell = TerrainSize / (res - 1);
            for (int az = 0; az < ar; az++)
                for (int ax = 0; ax < ar; ax++)
                {
                    int hx = Mathf.RoundToInt(ax * (res - 1) / (float)(ar - 1));
                    int hz = Mathf.RoundToInt(az * (res - 1) / (float)(ar - 1));
                    int x0 = Mathf.Max(0, hx - 1), x1 = Mathf.Min(res - 1, hx + 1);
                    int z0 = Mathf.Max(0, hz - 1), z1 = Mathf.Min(res - 1, hz + 1);
                    float gx = (heights[hz, x1] - heights[hz, x0]) * TerrainHeight / ((x1 - x0) * cell);
                    float gz = (heights[z1, hx] - heights[z0, hx]) * TerrainHeight / ((z1 - z0) * cell);
                    float slope = Mathf.Sqrt(gx * gx + gz * gz);
                    float wx = hx / (float)(res - 1) * TerrainSize - TerrainSize / 2f;
                    float wz = hz / (float)(res - 1) * TerrainSize - TerrainSize / 2f;

                    float gravel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.65f, slope));
                    gravel = Mathf.Max(gravel, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.7f, Mathf.PerlinNoise(wx * 0.05f + 7f, wz * 0.05f))) * 0.6f);
                    float mare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.62f, Mathf.PerlinNoise(wx * 0.004f + 5f, wz * 0.004f + 9f)));
                    mare = Mathf.Max(mare, interior[hz, hx] * 0.55f);
                    float ej = ejecta[hz, hx] * (0.7f + 0.3f * Mathf.PerlinNoise(wx * 0.1f, wz * 0.1f));

                    float rest = 1f - gravel;
                    float wE = rest * ej;
                    float rest2 = rest * (1f - ej);
                    float wM = rest2 * mare;
                    float wR = rest2 * (1f - mare);
                    alpha[az, ax, 0] = wR;
                    alpha[az, ax, 1] = wM;
                    alpha[az, ax, 2] = gravel;
                    alpha[az, ax, 3] = wE;
                }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "MoonTerrain";
            go.transform.position = new Vector3(-TerrainSize / 2f, -TerrainBase, -TerrainSize / 2f);
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 2f;
            terrain.basemapDistance = 350f;
            terrain.drawInstanced = true;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            generatedCraters = all;
            return terrain;
        }

        static List<Crater> generatedCraters = new List<Crater>();

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ---------------------------------------------------------------- база и ориентиры

        static BaseZone BuildBase(Dictionary<string, Material> mats, Func<float, float, float> h)
        {
            var root = new GameObject("MoonBase").transform;
            Vector3 P(float x, float z) => new Vector3(x, h(x, z), z);

            PlaceModel("LandingPad.fbx", "LandingPad", root, new Vector3(0f, 0.01f, 0f), 22.5f);
            PlaceModel("Habitat.fbx", "Habitat_A", root, P(-24f, -8f), 90f);
            PlaceModel("Habitat.fbx", "Habitat_B", root, P(-8f, -30f), 10f);
            PlaceModel("Dome.fbx", "Dome", root, P(16f, -28f), -28f);
            PlaceModel("CommsTower.fbx", "CommsTower", root, P(-30f, 16f), 30f);
            int k = 0;
            foreach (var (x, z) in new[] { (27f, -2f), (27f, 8f), (27f, -12f), (35f, 3f), (35f, -7f) })
                PlaceModel("SolarArray.fbx", $"SolarArray_{k++}", root, P(x, z), 60f);
            var crates = new[] { (-14f, 10f, 0f), (-12.6f, 10.4f, 25f), (-13.3f, 8.6f, 70f), (-13.6f, 9.8f, 10f) };
            for (int i = 0; i < crates.Length; i++)
            {
                var (x, z, yaw) = crates[i];
                var pos = P(x, z) + (i == 3 ? Vector3.up * 1.0f : Vector3.zero);
                PlaceModel("Crate.fbx", $"Crate_{i}", root, pos, yaw);
            }
            PlaceModel("Flag.fbx", "BaseFlag", root, P(-8f, 12f), 200f, 1f, false);

            // Огни площадки
            for (int i = 0; i < 4; i++)
            {
                var pos = Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward * 11.4f;
                var l = new GameObject($"PadLight_{i}").AddComponent<Light>();
                l.transform.SetParent(root, false);
                l.transform.localPosition = pos + Vector3.up * 1.5f;
                l.type = LightType.Point;
                l.color = new Color(1f, 0.75f, 0.45f);
                l.range = 14f;
                l.intensity = 2f;
            }

            Prim(PrimitiveType.Cylinder, "BaseBeacon", root, new Vector3(0f, 60f, -2f), new Vector3(1.6f, 60f, 1.6f), mats["BeaconBase"], false)
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var zone = new GameObject("BaseZone");
            zone.transform.SetParent(root, false);
            zone.transform.localPosition = new Vector3(0f, 4f, 0f);
            var box = zone.AddComponent<BoxCollider>();
            box.size = new Vector3(24f, 8f, 24f);
            box.isTrigger = true;
            return zone.AddComponent<BaseZone>();
        }

        static void BuildLandmarks(Dictionary<string, Material> mats, Func<float, float, float> h)
        {
            var root = new GameObject("Landmarks").transform;
            var lp = new Vector3(LanderPos.x, h(LanderPos.x, LanderPos.y), LanderPos.y);
            PlaceModel("Lander.fbx", "Lander", root, lp, 35f);
            PlaceModel("Flag.fbx", "LanderFlag", root, lp + new Vector3(6f, 0f, -3f), 160f, 1f, false);
            PlaceModel("Crate.fbx", "LanderCrate", root, lp + new Vector3(-5f, 0f, 3f), 40f);
            var l = new GameObject("LanderLight").AddComponent<Light>();
            l.transform.SetParent(root, false);
            l.transform.position = lp + new Vector3(0f, 9f, 0f);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.85f, 0.6f);
            l.range = 20f;
            l.intensity = 1.5f;
        }

        static List<Transform> BuildRamps(Dictionary<string, Material> mats, Func<float, float, float> h)
        {
            var list = new List<Transform>();
            var root = new GameObject("Ramps").transform;
            for (int i = 0; i < RampSpots.Length; i++)
            {
                var (p, yaw) = RampSpots[i];
                // Низкий край в точке p - вперёд*5.5; ставим по высоте низкого края, чтобы не висел
                var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var low = new Vector2(p.x, p.y) - new Vector2(fwd.x, fwd.z) * 5.5f;
                float y = Mathf.Min(h(p.x, p.y), h(low.x, low.y)) - 0.05f;
                list.Add(PlaceModel("Ramp.fbx", $"Ramp_{i + 1}", root, new Vector3(p.x, y, p.y), yaw).transform);
            }
            return list;
        }

        // ---------------------------------------------------------------- камни

        static void BuildRocks(Dictionary<string, Material> mats, Func<float, float, float> h)
        {
            var boulders = ModelMeshes("Rocks.fbx", "Rock_");
            var rockMat = FindMaterials("Rock").Count > 0 ? FindMaterials("Rock")[0] : mats["Rock"];
            var pebbles = PebbleMeshes();
            var root = new GameObject("Rocks").transform;

            bool Blocked(Vector2 p, float extra = 0f)
            {
                if (p.magnitude < 32f || Vector2.Distance(p, LanderPos) < 12f) return true;
                foreach (var c in SampleCraters) if (Vector2.Distance(p, c.c) < c.r + 3f + extra) return true;
                foreach (var r in RampSpots) if (Vector2.Distance(p, r.p) < 10f + extra) return true;
                return NearMissionSpot(p, extra);
            }

            GameObject Rock(string name, Transform parent, Mesh mesh, Vector2 p, float s, bool collider, float sink)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = rockMat;
                if (!collider) mr.shadowCastingMode = ShadowCastingMode.Off;
                if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.transform.position = new Vector3(p.x, h(p.x, p.y) - s * sink, p.y);
                go.transform.rotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f));
                go.transform.localScale = new Vector3(s * Random.Range(0.85f, 1.15f), s * Random.Range(0.8f, 1.1f), s * Random.Range(0.85f, 1.15f));
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                return go;
            }

            // Одиночные валуны
            var single = new GameObject("Boulders").transform;
            single.SetParent(root, false);
            for (int placed = 0, guard = 0; placed < 80 && guard < 4000; guard++)
            {
                var p = new Vector2(Random.Range(-320f, 320f), Random.Range(-320f, 320f));
                if (Blocked(p, 4f)) continue;
                Rock($"Boulder_{placed++}", single, boulders[Random.Range(0, boulders.Count)], p, Random.Range(0.6f, 2.8f), true, 0.2f);
            }

            // Поля глыб на валах крупных кратеров
            var fields = new GameObject("CraterBoulders").transform;
            fields.SetParent(root, false);
            foreach (var c in generatedCraters)
            {
                if (c.r < 14f) continue;
                int n = Mathf.RoundToInt(c.r * 0.6f);
                for (int i = 0; i < n; i++)
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    float r = c.r * Random.Range(0.95f, 1.7f);
                    var p = c.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (Blocked(p)) continue;
                    Rock("RimBoulder", fields, boulders[Random.Range(0, boulders.Count)], p, Random.Range(0.4f, 1.8f), true, 0.25f);
                }
            }

            // Россыпь гальки по всей игровой зоне
            var gravel = new GameObject("Pebbles").transform;
            gravel.SetParent(root, false);
            for (int i = 0; i < 2200; i++)
            {
                var p = Random.insideUnitCircle * 300f;
                if (p.magnitude < 14f) continue;
                Rock("Pebble", gravel, pebbles[Random.Range(0, pebbles.Length)], p, Random.Range(0.06f, 0.3f), false, 0.3f);
            }

            // Каменистые зоны: плотная россыпь + триггер тряски
            var zones = new[] { new Vector2(22f, 50f), new Vector2(-50f, 108f), new Vector2(92f, 128f), new Vector2(-88f, 60f) };
            for (int z = 0; z < zones.Length; z++)
            {
                var center = zones[z];
                var zone = new GameObject($"RoughZone_{z + 1}");
                zone.transform.SetParent(root, false);
                zone.transform.position = new Vector3(center.x, h(center.x, center.y) + 2f, center.y);
                var box = zone.AddComponent<BoxCollider>();
                box.size = new Vector3(18f, 6f, 22f);
                box.isTrigger = true;
                zone.AddComponent<RoughZone>();
                for (int i = 0; i < 140; i++)
                {
                    var p = center + new Vector2(Random.Range(-9f, 9f), Random.Range(-11f, 11f));
                    Rock("Pebble", zone.transform, pebbles[Random.Range(0, pebbles.Length)], p, Random.Range(0.12f, 0.5f), false, 0.25f);
                }
            }
        }

        /// <summary>Лёгкие меши гальки (80 треугольников) для массовой россыпи.</summary>
        static Mesh[] PebbleMeshes()
        {
            var result = new Mesh[4];
            for (int k = 0; k < result.Length; k++)
            {
                string path = $"{Root}/Meshes/Pebble_{k}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null) { result[k] = existing; continue; }
                Icosphere(1, out var verts, out var tris);
                float seed = k * 13.7f;
                for (int i = 0; i < verts.Count; i++)
                {
                    var v = verts[i];
                    float n = Mathf.PerlinNoise(v.x * 1.9f + seed, v.y * 1.9f + v.z) * 0.5f;
                    v *= 0.75f + n;
                    v.y = v.y < -0.1f ? -0.1f + (v.y + 0.1f) * 0.3f : v.y * 0.75f;
                    verts[i] = v;
                }
                var fv = new Vector3[tris.Count];
                var fi = new int[tris.Count];
                var uv = new Vector2[tris.Count];
                for (int i = 0; i < tris.Count; i++) { fv[i] = verts[tris[i]]; fi[i] = i; uv[i] = new Vector2(fv[i].x + fv[i].y, fv[i].z + fv[i].y) * 0.5f; }
                var mesh = new Mesh { name = $"Pebble_{k}", vertices = fv, triangles = fi, uv = uv };
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, path);
                result[k] = mesh;
            }
            return result;
        }
    }
}
