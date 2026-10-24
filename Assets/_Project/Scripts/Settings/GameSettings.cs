using System;
using UnityEngine;

namespace Mehatron.Settings
{
    public enum Difficulty { Easy, Normal, Hard }

    /// <summary>
    /// Все пользовательские настройки. Хранятся в PlayerPrefs как JSON, применяются <see cref="SettingsApplier"/>.
    /// Значения по умолчанию рассчитаны на ПК + Quest через Link.
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        // ---------------------------------------------------------------- графика
        /// <summary>0 низкое, 1 среднее, 2 высокое, 3 ультра, 4 своё.</summary>
        public int qualityPreset = 2;
        public float renderScale = 1f;
        /// <summary>1, 2, 4, 8.</summary>
        public int msaa = 4;
        /// <summary>0 билинейное, 1 FSR 1.0, 2 STP.</summary>
        public int upscaler = 0;
        /// <summary>0 выкл, 1 низкие, 2 средние, 3 высокие, 4 ультра.</summary>
        public int shadows = 3;
        public float shadowDistance = 120f;
        public bool softShadows = true;
        public bool ssao = true;
        /// <summary>0 четверть, 1 половина, 2 полное.</summary>
        public int textureQuality = 2;
        /// <summary>0 выкл, 1 вкл, 2 принудительно.</summary>
        public int anisotropic = 2;
        /// <summary>0 низкая, 1 средняя, 2 высокая.</summary>
        public int terrainDetail = 2;
        public float lodBias = 1.5f;
        public bool hdr = true;

        // ---------------------------------------------------------------- изображение
        public bool postProcessing = true;
        public bool bloom = true;
        public float bloomIntensity = 0.6f;
        public float brightness = 0f;
        public float contrast = 5f;
        public float saturation = 0f;
        public bool vignette = true;
        public bool filmGrain = false;
        /// <summary>0 нет, 1 ACES, 2 нейтральный.</summary>
        public int tonemapping = 1;

        // ---------------------------------------------------------------- экран (без шлема)
        public int resolutionIndex = -1;
        /// <summary>0 полный экран, 1 окно без рамки, 2 окно.</summary>
        public int windowMode = 2;
        public bool vsync = true;
        /// <summary>0 без ограничения, иначе FPS.</summary>
        public int fpsLimit = 0;
        public float fieldOfView = 70f;
        public bool showFps = false;

        // ---------------------------------------------------------------- звук (0..1)
        public float masterVolume = 0.8f;
        public float musicVolume = 0.5f;
        public float sfxVolume = 0.8f;
        public float engineVolume = 0.7f;
        public float ambienceVolume = 0.6f;
        public float uiVolume = 0.7f;
        public bool muteInBackground = true;

        // ---------------------------------------------------------------- управление
        public float steerSensitivity = 2f;
        public float steerDeadzone = 0.01f;
        public bool invertSteer = false;
        public float pedalDeadzone = 0.05f;
        public float stickSensitivity = 1f;
        public float stickDeadzone = 0.08f;
        public float stickExpo = 0.35f;
        public bool invertStickPitch = false;
        public bool useThrottleLever = true;
        public float keyboardSteerSpeed = 3f;
        public float mouseSensitivity = 0.15f;
        public float seatHeight = 0f;

        // ---------------------------------------------------------------- платформа и жилет
        public bool motionEnabled = true;
        public float motionIntensity = 0.3f;
        public float motionSmoothness = 3f;
        public bool motionPitch = true;
        public bool motionRoll = true;
        public bool motionHeave = true;
        public bool motionYaw = true;
        public bool motionSurge = true;
        public bool motionSway = true;
        public float roughnessIntensity = 1f;
        public bool hapticsEnabled = true;
        public float hapticsIntensity = 1f;
        public float hapticsRumble = 1f;

        // ---------------------------------------------------------------- игра
        public Difficulty difficulty = Difficulty.Normal;
        public bool compass = true;
        public bool hints = true;
        /// <summary>0 км/ч, 1 м/с.</summary>
        public int speedUnits = 0;
        public bool menuMusic = true;

        // ---------------------------------------------------------------- хранение

        const string Key = "Mehatron.Settings.v1";

        public static GameSettings Current { get; private set; } = Load();
        /// <summary>Вызывается после любого изменения (меню меняет поля и зовёт <see cref="NotifyChanged"/>).</summary>
        public static event Action Changed;

        public static void NotifyChanged() => Changed?.Invoke();

        public static GameSettings Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<GameSettings>(json) ?? new GameSettings();
            }
            catch (Exception e) { Debug.LogWarning("[Settings] Не удалось прочитать настройки: " + e.Message); }
            return new GameSettings();
        }

        public static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Current));
            PlayerPrefs.Save();
        }

        public static void ResetToDefaults()
        {
            Current = new GameSettings();
            NotifyChanged();
        }

        /// <summary>Перечитать сохранённое (отмена несохранённых правок).</summary>
        public static void Revert()
        {
            Current = Load();
            NotifyChanged();
        }

        /// <summary>Пресет качества выставляет связанные параметры графики.</summary>
        public void ApplyQualityPreset(int preset)
        {
            qualityPreset = preset;
            switch (preset)
            {
                case 0: renderScale = 0.8f; msaa = 1; upscaler = 1; shadows = 1; shadowDistance = 60f; softShadows = false; ssao = false; textureQuality = 1; anisotropic = 0; terrainDetail = 0; lodBias = 0.7f; bloom = false; break;
                case 1: renderScale = 0.9f; msaa = 2; upscaler = 1; shadows = 2; shadowDistance = 90f; softShadows = true; ssao = false; textureQuality = 2; anisotropic = 1; terrainDetail = 1; lodBias = 1f; bloom = true; break;
                case 2: renderScale = 1f; msaa = 4; upscaler = 0; shadows = 3; shadowDistance = 120f; softShadows = true; ssao = true; textureQuality = 2; anisotropic = 2; terrainDetail = 2; lodBias = 1.5f; bloom = true; break;
                case 3: renderScale = 1.2f; msaa = 8; upscaler = 0; shadows = 4; shadowDistance = 200f; softShadows = true; ssao = true; textureQuality = 2; anisotropic = 2; terrainDetail = 2; lodBias = 2f; bloom = true; break;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            Current = Load();
        }
    }
}
