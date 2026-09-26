using System.Collections.Generic;
using System.Reflection;
using Mehatron.Gameplay;
using Mehatron.Haptics;
using Mehatron.Motion;
using Mehatron.Vehicle;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Mehatron.Settings
{
    /// <summary>
    /// Применяет <see cref="GameSettings"/> к движку (URP, качество, экран, постобработка) и к игре
    /// (руль, платформа, жилет, сложность). Ассеты проекта (URP, MotionSettings, InputCalibration) не портятся:
    /// игровые компоненты переводятся на рабочие копии, а значения URP-ассета в редакторе восстанавливаются при выходе.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SettingsApplier : MonoBehaviour
    {
        [SerializeField] RoverInput roverInput;
        [SerializeField] VRSeatRig seatRig;
        [SerializeField] MotionCueing motion;
        [SerializeField] HapticsDirector haptics;
        [SerializeField] List<VestHapticsDevice> vests = new List<VestHapticsDevice>();
        [SerializeField] OxygenTimer oxygen;
        [SerializeField] HullIntegrity hull;
        [Tooltip("Камеры, которым включается постобработка и задаётся FOV (кабина, меню).")]
        [SerializeField] List<Camera> cameras = new List<Camera>();

        public static SettingsApplier Instance { get; private set; }

        UniversalRenderPipelineAsset urp;
        UrpSnapshot urpOriginal;
        ScriptableRendererFeature ssaoFeature;
        bool ssaoOriginal;
        QualitySnapshot qualityOriginal;

        Volume volume;
        Bloom bloom;
        Tonemapping tonemap;
        ColorAdjustments color;
        Vignette vignette;
        FilmGrain grain;

        MotionSettings motionBase;

        public static readonly int[] FpsLimits = { 0, 60, 72, 90, 120, 144 };

        struct UrpSnapshot
        {
            public float renderScale, shadowDistance; public int msaa, shadowRes, cascades; public bool hdr; public UpscalingFilterSelection upscaler;
        }

        struct QualitySnapshot
        {
            public AnisotropicFiltering aniso; public int mipLimit, vsync; public float lodBias;
        }

        void Awake()
        {
            Instance = this;
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urpOriginal = new UrpSnapshot
                {
                    renderScale = urp.renderScale, shadowDistance = urp.shadowDistance, msaa = urp.msaaSampleCount,
                    shadowRes = urp.mainLightShadowmapResolution, cascades = urp.shadowCascadeCount, hdr = urp.supportsHDR,
                    upscaler = urp.upscalingFilter,
                };
                ssaoFeature = FindFeature(urp, "ScreenSpaceAmbientOcclusion");
                if (ssaoFeature != null) ssaoOriginal = ssaoFeature.isActive;
            }
            qualityOriginal = new QualitySnapshot
            {
                aniso = QualitySettings.anisotropicFiltering, mipLimit = QualitySettings.globalTextureMipmapLimit,
                vsync = QualitySettings.vSyncCount, lodBias = QualitySettings.lodBias,
            };

            if (roverInput != null) roverInput.UseRuntimeCopy();
            if (motion != null && motion.Settings != null)
            {
                motionBase = Instantiate(motion.Settings); // исходные значения для масштабирования
                motion.UseRuntimeCopy();
            }
            CreateVolume();
            GameSettings.Changed += Apply;
        }

        void Start() => Apply();

        void OnDestroy()
        {
            GameSettings.Changed -= Apply;
            if (Instance == this) Instance = null;
            RestoreAssets();
        }

        void OnApplicationFocus(bool focus)
        {
            if (GameSettings.Current.muteInBackground) AudioListener.pause = !focus && !Application.isEditor;
        }

        /// <summary>URP-ассет и QualitySettings общие для проекта: в редакторе возвращаем как было.</summary>
        void RestoreAssets()
        {
            if (urp != null)
            {
                urp.renderScale = urpOriginal.renderScale;
                urp.shadowDistance = urpOriginal.shadowDistance;
                urp.msaaSampleCount = urpOriginal.msaa;
                urp.mainLightShadowmapResolution = urpOriginal.shadowRes;
                urp.shadowCascadeCount = urpOriginal.cascades;
                urp.supportsHDR = urpOriginal.hdr;
                urp.upscalingFilter = urpOriginal.upscaler;
                if (ssaoFeature != null) ssaoFeature.SetActive(ssaoOriginal);
            }
            QualitySettings.anisotropicFiltering = qualityOriginal.aniso;
            QualitySettings.globalTextureMipmapLimit = qualityOriginal.mipLimit;
            QualitySettings.vSyncCount = qualityOriginal.vsync;
            QualitySettings.lodBias = qualityOriginal.lodBias;
        }

        static ScriptableRendererFeature FindFeature(UniversalRenderPipelineAsset asset, string typeName)
        {
            var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(asset) is not ScriptableRendererData[] list) return null;
            foreach (var data in list)
                if (data != null)
                    foreach (var f in data.rendererFeatures)
                        if (f != null && f.GetType().Name.Contains(typeName)) return f;
            return null;
        }

        void CreateVolume()
        {
            var go = new GameObject("SettingsVolume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            bloom = profile.Add<Bloom>(true);
            tonemap = profile.Add<Tonemapping>(true);
            color = profile.Add<ColorAdjustments>(true);
            vignette = profile.Add<Vignette>(true);
            grain = profile.Add<FilmGrain>(true);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.65f);
            vignette.color.Override(Color.black);
            vignette.smoothness.Override(0.45f);
            grain.type.Override(FilmGrainLookup.Medium1);
            volume.sharedProfile = profile;
        }

        public void AddCamera(Camera cam)
        {
            if (cam != null && !cameras.Contains(cam)) cameras.Add(cam);
            Apply();
        }

        public void Apply()
        {
            var s = GameSettings.Current;
            ApplyGraphics(s);
            ApplyPost(s);
            ApplyDisplay(s);
            ApplyAudio(s);
            ApplyControls(s);
            ApplyHardware(s);
            ApplyGame(s);
        }

        // ---------------------------------------------------------------- графика

        void ApplyGraphics(GameSettings s)
        {
            if (urp != null)
            {
                urp.renderScale = Mathf.Clamp(s.renderScale, 0.5f, 2f);
                urp.msaaSampleCount = s.msaa;
                urp.upscalingFilter = s.upscaler switch
                {
                    1 => UpscalingFilterSelection.FSR,
                    2 => UpscalingFilterSelection.STP,
                    _ => UpscalingFilterSelection.Auto,
                };
                urp.supportsHDR = s.hdr;
                urp.shadowDistance = s.shadows == 0 ? 0f : s.shadowDistance;
                urp.mainLightShadowmapResolution = s.shadows switch { 1 => 512, 2 => 1024, 3 => 2048, 4 => 4096, _ => 256 };
                urp.shadowCascadeCount = s.shadows switch { 0 => 1, 1 => 1, 2 => 2, 3 => 4, _ => 4 };
                if (ssaoFeature != null) ssaoFeature.SetActive(s.ssao);
            }
            var sun = RenderSettings.sun;
            if (sun != null)
                sun.shadows = s.shadows == 0 ? LightShadows.None : s.softShadows ? LightShadows.Soft : LightShadows.Hard;

            QualitySettings.globalTextureMipmapLimit = 2 - Mathf.Clamp(s.textureQuality, 0, 2);
            QualitySettings.anisotropicFiltering = s.anisotropic switch
            {
                0 => AnisotropicFiltering.Disable,
                1 => AnisotropicFiltering.Enable,
                _ => AnisotropicFiltering.ForceEnable,
            };
            QualitySettings.lodBias = s.lodBias;

            foreach (var t in Terrain.activeTerrains)
            {
                t.heightmapPixelError = s.terrainDetail switch { 0 => 12f, 1 => 6f, _ => 2f };
                t.basemapDistance = s.terrainDetail switch { 0 => 150f, 1 => 400f, _ => 1000f };
                t.detailObjectDistance = s.terrainDetail switch { 0 => 40f, 1 => 80f, _ => 150f };
            }
        }

        void ApplyPost(GameSettings s)
        {
            foreach (var cam in cameras)
            {
                if (cam == null) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = s.postProcessing;
                data.antialiasing = AntialiasingMode.None;
            }
            if (volume == null) return;
            volume.weight = s.postProcessing ? 1f : 0f;
            bloom.active = s.bloom;
            bloom.intensity.Override(s.bloomIntensity);
            tonemap.active = s.tonemapping > 0;
            tonemap.mode.Override(s.tonemapping == 2 ? TonemappingMode.Neutral : TonemappingMode.ACES);
            color.postExposure.Override(s.brightness);
            color.contrast.Override(s.contrast);
            color.saturation.Override(s.saturation);
            vignette.active = s.vignette;
            vignette.intensity.Override(0.28f);
            grain.active = s.filmGrain;
            grain.intensity.Override(0.25f);
        }

        // ---------------------------------------------------------------- экран

        public static Resolution[] Resolutions()
        {
            var list = new List<Resolution>();
            foreach (var r in Screen.resolutions)
            {
                // Одно разрешение — один пункт (частоты обновления схлопываем, берём наибольшую).
                int i = list.FindIndex(x => x.width == r.width && x.height == r.height);
                if (i < 0) list.Add(r);
                else if (r.refreshRateRatio.value > list[i].refreshRateRatio.value) list[i] = r;
            }
            if (list.Count == 0) list.Add(Screen.currentResolution);
            return list.ToArray();
        }

        void ApplyDisplay(GameSettings s)
        {
            QualitySettings.vSyncCount = s.vsync ? 1 : 0;
            Application.targetFrameRate = s.fpsLimit > 0 ? s.fpsLimit : -1;
            foreach (var cam in cameras)
                if (cam != null && !cam.stereoEnabled) cam.fieldOfView = s.fieldOfView;

            if (Application.isEditor) return;
            var mode = s.windowMode switch
            {
                0 => FullScreenMode.ExclusiveFullScreen,
                1 => FullScreenMode.FullScreenWindow,
                _ => FullScreenMode.Windowed,
            };
            var res = Resolutions();
            if (s.resolutionIndex >= 0 && s.resolutionIndex < res.Length)
            {
                var r = res[s.resolutionIndex];
                if (Screen.width != r.width || Screen.height != r.height || Screen.fullScreenMode != mode)
                    Screen.SetResolution(r.width, r.height, mode, r.refreshRateRatio);
            }
            else if (Screen.fullScreenMode != mode) Screen.fullScreenMode = mode;
        }

        // ---------------------------------------------------------------- звук

        void ApplyAudio(GameSettings s)
        {
            AudioListener.volume = Mathf.Clamp01(s.masterVolume);
            // Громкости групп читает Audio.GameAudio каждый кадр.
        }

        // ---------------------------------------------------------------- управление

        void ApplyControls(GameSettings s)
        {
            if (roverInput != null && roverInput.Calibration != null)
            {
                var c = roverInput.Calibration;
                c.steerSensitivity = s.steerSensitivity;
                c.steerDeadzone = s.steerDeadzone;
                c.invertSteer = s.invertSteer;
                c.pedalDeadzone = s.pedalDeadzone;
                c.stickSteerSensitivity = s.stickSensitivity;
                c.stickDeadzone = s.stickDeadzone;
                c.stickExpo = s.stickExpo;
                c.invertStickPitch = s.invertStickPitch;
                c.useThrottleLever = s.useThrottleLever;
                roverInput.KeyboardSteerSpeed = s.keyboardSteerSpeed;
            }
            if (seatRig != null)
            {
                seatRig.MouseSensitivity = s.mouseSensitivity;
                seatRig.HeightOffset = s.seatHeight;
            }
        }

        // ---------------------------------------------------------------- платформа и жилет

        void ApplyHardware(GameSettings s)
        {
            if (motion != null && motion.Settings != null)
            {
                var m = motion.Settings;
                m.masterIntensity = s.motionEnabled ? Mathf.Clamp01(s.motionIntensity) : 0f;
                m.maxRatePerSecond = s.motionSmoothness;
                m.usePitch = s.motionPitch;
                m.useRoll = s.motionRoll;
                m.useHeave = s.motionHeave;
                m.useYaw = s.motionYaw;
                if (motionBase != null)
                {
                    m.roughnessHeave = motionBase.roughnessHeave * s.roughnessIntensity;
                    m.roughnessTilt = motionBase.roughnessTilt * s.roughnessIntensity;
                }
            }
            foreach (var v in vests)
                if (v != null) v.MasterIntensity = s.hapticsEnabled ? s.hapticsIntensity : 0f;
            if (haptics != null) haptics.RumbleScale = s.hapticsRumble;
        }

        /// <summary>Тест жилета из меню: волна по всем моторам.</summary>
        public void TestHaptics()
        {
            foreach (var v in vests)
                if (v != null && v.isActiveAndEnabled) v.PlayPattern(HapticPattern.SystemsCheck, 0.8f);
        }

        // ---------------------------------------------------------------- игра

        void ApplyGame(GameSettings s)
        {
            if (oxygen != null) oxygen.DurationScale = s.difficulty switch { Difficulty.Easy => 1.3f, Difficulty.Hard => 0.8f, _ => 1f };
            if (hull != null) hull.DamageScale = s.difficulty switch { Difficulty.Easy => 0.5f, Difficulty.Hard => 1.5f, _ => 1f };
        }
    }
}
