using Mehatron.Settings;
using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.Audio
{
    public enum UiSound { Hover, Click, Back, Adjust }

    /// <summary>
    /// Звук игры: музыка меню, мотор, кабина, ветер бури и события миссии. Группы громкости — из <see cref="GameSettings"/>.
    /// Удары и разряды звучат с нужной стороны (3D-источник ставится по направлению на событие).
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        [SerializeField] RoverController rover;
        [Tooltip("Слушатель (камера в кабине) — от него считаем направление на события.")]
        [SerializeField] Transform listener;

        static GameAudio instance;

        AudioSource music, engine, cabin, wind, ui;
        AudioSource[] pool;
        int poolNext;

        AudioClip hover, click, back, adjust, chime, notify, success, fail, scan, alarm, thump, boom, zap, hiss, rumble;

        GameState state = GameState.Menu;
        bool storm;

        void Awake()
        {
            instance = this;
            hover = SynthClips.UiHover(); click = SynthClips.UiClick(); back = SynthClips.UiBack(); adjust = SynthClips.UiAdjust();
            chime = SynthClips.Chime(); notify = SynthClips.Notify(); success = SynthClips.Success(); fail = SynthClips.Fail();
            scan = SynthClips.ScanBeep(); alarm = SynthClips.Alarm(); thump = SynthClips.Thump(); boom = SynthClips.Boom();
            zap = SynthClips.Zap(); hiss = SynthClips.Hiss(); rumble = SynthClips.Rumble();

            music = Loop("Music", SynthClips.MusicLoop());
            engine = Loop("Engine", SynthClips.EngineLoop());
            cabin = Loop("Cabin", SynthClips.CabinLoop());
            wind = Loop("Wind", SynthClips.WindLoop());
            ui = Source("UI");
            ui.ignoreListenerPause = true;
            pool = new AudioSource[8];
            for (int i = 0; i < pool.Length; i++)
            {
                pool[i] = Source("Sfx" + i);
                pool[i].spatialBlend = 0.85f;
                pool[i].minDistance = 3f;
                pool[i].maxDistance = 60f;
            }
            music.ignoreListenerPause = true;
        }

        AudioSource Source(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        AudioSource Loop(string name, AudioClip clip)
        {
            var s = Source(name);
            s.clip = clip;
            s.loop = true;
            s.volume = 0f;
            s.Play();
            return s;
        }

        void OnEnable()
        {
            GameEvents.StateChanged += OnState;
            GameEvents.Impact += OnImpact;
            GameEvents.MeteorExploded += OnMeteor;
            GameEvents.Landed += OnLanded;
            GameEvents.SampleCollected += OnSample;
            GameEvents.TaskDone += OnTask;
            GameEvents.ObjectiveChanged += OnObjective;
            GameEvents.ScanPulse += OnScan;
            GameEvents.QuakeStarted += OnQuake;
            GameEvents.StaticDischarge += OnStatic;
            GameEvents.StormChanged += OnStorm;
            GameEvents.OxygenRefilled += OnRefill;
            GameEvents.HullCriticalChanged += OnHullCritical;
            GameEvents.LowOxygenChanged += OnLowOxygen;
            GameEvents.MeteorShowerStarted += OnMeteorWarning;
            GameEvents.MissionFinished += OnFinished;
        }

        void OnDisable()
        {
            GameEvents.StateChanged -= OnState;
            GameEvents.Impact -= OnImpact;
            GameEvents.MeteorExploded -= OnMeteor;
            GameEvents.Landed -= OnLanded;
            GameEvents.SampleCollected -= OnSample;
            GameEvents.TaskDone -= OnTask;
            GameEvents.ObjectiveChanged -= OnObjective;
            GameEvents.ScanPulse -= OnScan;
            GameEvents.QuakeStarted -= OnQuake;
            GameEvents.StaticDischarge -= OnStatic;
            GameEvents.StormChanged -= OnStorm;
            GameEvents.OxygenRefilled -= OnRefill;
            GameEvents.HullCriticalChanged -= OnHullCritical;
            GameEvents.LowOxygenChanged -= OnLowOxygen;
            GameEvents.MeteorShowerStarted -= OnMeteorWarning;
            GameEvents.MissionFinished -= OnFinished;
        }

        // ---------------------------------------------------------------- API

        public static void PlayUi(UiSound s)
        {
            if (instance == null) return;
            var clip = s switch { UiSound.Hover => instance.hover, UiSound.Click => instance.click, UiSound.Back => instance.back, _ => instance.adjust };
            instance.ui.PlayOneShot(clip, GameSettings.Current.uiVolume);
        }

        /// <summary>Проверка звука из настроек.</summary>
        public static void PlayTest()
        {
            if (instance != null) instance.Play2D(instance.success, 0.9f);
        }

        void Play2D(AudioClip clip, float volume) => ui.PlayOneShot(clip, volume * GameSettings.Current.sfxVolume);

        /// <summary>Звук «со стороны» события: источник ставим по направлению, но недалеко, чтобы было слышно.</summary>
        void PlayAt(AudioClip clip, Vector3 point, float volume)
        {
            var s = pool[poolNext];
            poolNext = (poolNext + 1) % pool.Length;
            Vector3 origin = listener != null ? listener.position : point;
            Vector3 dir = point - origin;
            s.transform.position = origin + (dir.sqrMagnitude > 0.01f ? dir.normalized * Mathf.Min(dir.magnitude, 6f) : Vector3.zero);
            s.pitch = Random.Range(0.9f, 1.1f);
            s.PlayOneShot(clip, volume * GameSettings.Current.sfxVolume);
        }

        // ---------------------------------------------------------------- события

        void OnState(GameState s) => state = s;
        void OnImpact(Vector3 p, float k) { if (k > 0.05f) PlayAt(thump, p, Mathf.Lerp(0.3f, 1f, k)); }
        void OnMeteor(Vector3 p, float k) => PlayAt(boom, p, Mathf.Lerp(0.25f, 1f, k));
        void OnLanded(float k) => Play2D(thump, Mathf.Lerp(0.3f, 1f, k));
        void OnSample(int c, int t) => Play2D(chime, 0.8f);
        void OnTask(string text) => Play2D(chime, 0.7f);
        void OnObjective(int i, string title) { if (i > 0) Play2D(notify, 0.7f); }
        void OnScan(float k) => Play2D(scan, Mathf.Lerp(0.3f, 0.7f, k));
        void OnQuake(float d, float k) => Play2D(rumble, k);
        void OnStatic(Vector3 p, float k) => PlayAt(zap, p, k);
        void OnStorm(bool on) => storm = on;
        void OnRefill(float s) => Play2D(hiss, 0.8f);
        void OnHullCritical(bool c) { if (c) Play2D(alarm, 0.7f); }
        void OnLowOxygen(bool low) { if (low) Play2D(alarm, 0.7f); }
        void OnMeteorWarning() => Play2D(alarm, 0.8f);
        void OnFinished(bool ok) => Play2D(ok ? success : fail, 0.9f);

        void Update()
        {
            var s = GameSettings.Current;
            float dt = Time.unscaledDeltaTime;
            bool playing = state == GameState.Playing;

            // Музыка: в меню полностью, в игре — выключена (слышны кабина и события).
            float musicTarget = s.menuMusic && !playing ? s.musicVolume : 0f;
            music.volume = Mathf.MoveTowards(music.volume, musicTarget * 0.6f, dt * 0.4f);

            float speed = rover != null ? rover.SpeedMs : 0f;
            float throttle = rover != null && rover.Input != null ? Mathf.Max(rover.Input.Throttle, rover.Input.Brake) : 0f;
            float engineTarget = playing ? (0.15f + 0.5f * Mathf.Clamp01(speed / 12f) + 0.25f * throttle) * s.engineVolume : 0f;
            engine.volume = Mathf.MoveTowards(engine.volume, engineTarget, dt * 1.5f);
            engine.pitch = Mathf.Lerp(engine.pitch, 0.7f + Mathf.Clamp01(speed / 12f) * 0.9f + throttle * 0.1f, dt * 4f);

            cabin.volume = Mathf.MoveTowards(cabin.volume, (playing ? 0.35f : 0.12f) * s.ambienceVolume, dt);
            wind.volume = Mathf.MoveTowards(wind.volume, (storm && playing ? 0.9f : 0f) * s.ambienceVolume, dt * 0.3f);
            wind.pitch = 0.9f + 0.2f * Mathf.PerlinNoise(Time.time * 0.3f, 0f);

            // Пауза: глушим игровые петли, музыка и интерфейс играют.
            bool paused = state == GameState.Paused;
            engine.mute = cabin.mute = wind.mute = paused;
        }
    }
}
