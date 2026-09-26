using Mehatron.Gameplay;
using Mehatron.Settings;
using Mehatron.UI.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace Mehatron.UI
{
    /// <summary>HUD на приборной панели ровера (world-space канвас): этап, кислород, корпус, скорость, компас, сообщения.</summary>
    public class CockpitHUD : MonoBehaviour
    {
        [SerializeField] GameFlow flow;
        [Tooltip("Строка этапа миссии (или счётчик образцов без миссии).")]
        [SerializeField] Text samplesText;
        [SerializeField] Text oxygenText;
        [SerializeField] Text hullText;
        [SerializeField] Text speedText;
        [Tooltip("Короткие уведомления во время миссии; в остальное время — подсказка этапа.")]
        [SerializeField] Text messageText;
        [Tooltip("Крупный текст на весь экран: меню, пауза, результат.")]
        [SerializeField] Text bigMessageText;
        [SerializeField] Image oxygenFill;
        [SerializeField] RectTransform compassArrow;
        [SerializeField] GameObject missionGroup;
        [SerializeField] float messageDuration = 3.5f;

        static readonly Color Ok = new Color(0.4f, 0.85f, 1f);
        static readonly Color Low = new Color(1f, 0.3f, 0.25f);
        static readonly Color Alert = new Color(1f, 0.85f, 0.35f);
        static readonly Color HintColor = new Color(0.75f, 0.9f, 1f, 0.85f);

        string transient;
        float transientUntil;

        void OnEnable()
        {
            GameEvents.SampleCollected += OnSample;
            GameEvents.MeteorShowerStarted += OnMeteors;
            GameEvents.LowOxygenChanged += OnLowOxygen;
            GameEvents.ObjectiveChanged += OnObjective;
            GameEvents.TaskDone += Show;
            GameEvents.QuakeStarted += OnQuake;
            GameEvents.StormChanged += OnStorm;
            GameEvents.HullCriticalChanged += OnHullCritical;
        }

        void OnDisable()
        {
            GameEvents.SampleCollected -= OnSample;
            GameEvents.MeteorShowerStarted -= OnMeteors;
            GameEvents.LowOxygenChanged -= OnLowOxygen;
            GameEvents.ObjectiveChanged -= OnObjective;
            GameEvents.TaskDone -= Show;
            GameEvents.QuakeStarted -= OnQuake;
            GameEvents.StormChanged -= OnStorm;
            GameEvents.HullCriticalChanged -= OnHullCritical;
        }

        void Show(string msg)
        {
            transient = msg;
            transientUntil = Time.unscaledTime + messageDuration;
        }

        void OnSample(int c, int t)
        {
            if (flow != null && flow.Mission == null && c >= t) Show("ВСЕ ОБРАЗЦЫ СОБРАНЫ!\nВОЗВРАЩАЙТЕСЬ НА БАЗУ");
            else if (c < t) Show($"ОБРАЗЕЦ СОБРАН  {c}/{t}");
        }

        void OnMeteors() => Show("ВНИМАНИЕ!\nМЕТЕОРИТНЫЙ ДОЖДЬ");
        void OnLowOxygen(bool low) { if (low) Show("КИСЛОРОД НА ИСХОДЕ"); }
        void OnObjective(int i, string title) { if (i > 0) Show($"ЭТАП {i + 1}: {title}"); }
        void OnQuake(float d, float s) => Show("ЛУНОТРЯСЕНИЕ!\nДЕРЖИТЕСЬ");
        void OnStorm(bool on) { if (on) Show("ЭЛЕКТРОСТАТИЧЕСКАЯ\nПЫЛЕВАЯ БУРЯ"); }
        void OnHullCritical(bool c) { if (c) Show("КОРПУС ПОВРЕЖДЁН!"); }

        void Update()
        {
            if (flow == null) return;
            bool playing = flow.State == GameState.Playing || flow.State == GameState.Paused;
            if (missionGroup != null) missionGroup.SetActive(playing);
            var mission = flow.Mission;

            if (playing)
            {
                var ox = flow.Oxygen;
                if (ox != null)
                {
                    int sec = Mathf.CeilToInt(ox.Remaining);
                    oxygenText.text = $"O2  {sec / 60}:{sec % 60:00}";
                    // Полоса без спрайта: ширину задаём якорем.
                    oxygenFill.rectTransform.anchorMax = new Vector2(ox.Normalized, 1f);
                    bool blink = ox.IsLow && Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.4f;
                    oxygenFill.color = ox.IsLow ? (blink ? Low : Low * 0.6f) : Ok;
                }
                var hull = flow.Hull;
                if (hullText != null && hull != null)
                {
                    hullText.text = $"КОРПУС  {hull.Normalized * 100f:0}%";
                    bool blink = hull.IsCritical && Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.4f;
                    hullText.color = hull.IsCritical ? (blink ? Low : Low * 0.7f) : Color.white;
                }
                samplesText.text = mission != null
                    ? $"{mission.StageIndex + 1}/{mission.StageCount}  {mission.Title}"
                    : $"ОБРАЗЦЫ  {flow.Collected}/{flow.Total}";
                if (flow.Rover != null)
                    speedText.text = GameSettings.Current.speedUnits == 1 ? $"{flow.Rover.SpeedMs:0.0} м/с" : $"{flow.Rover.SpeedMs * 3.6f:0} км/ч";
                UpdateCompass();
            }

            bool hasTransient = flow.State == GameState.Playing && Time.unscaledTime < transientUntil;
            bool hints = GameSettings.Current.hints;
            messageText.text = hasTransient ? transient : flow.State == GameState.Playing && mission != null && hints ? mission.Hint : "";
            messageText.color = hasTransient ? Alert : HintColor;

            // С игровым меню экран пульта в меню/паузе показывает только короткую подпись — всё остальное в меню.
            if (GameMenu.Instance != null)
            {
                bigMessageText.text = flow.State switch
                {
                    GameState.Menu => "ЛУННАЯ ЭКСПЕДИЦИЯ",
                    GameState.Paused => "ПАУЗА",
                    _ => "",
                };
                return;
            }
            bigMessageText.text = flow.State switch
            {
                GameState.Menu => mission != null
                    ? "ЛУННАЯ ЭКСПЕДИЦИЯ\n\nОбразцы, ретрансляторы, керн\nиз Большого кратера.\nСледите за кислородом и корпусом.\n\nНажмите СТАРТ"
                    : $"ЛУННЫЙ РОВЕР\n\nСоберите {flow.Total} образцов\nи вернитесь на базу\n\nНажмите СТАРТ",
                GameState.Paused => "ПАУЗА",
                GameState.Result => flow.LastSuccess
                    ? $"ЭКСПЕДИЦИЯ ЗАВЕРШЕНА!\n\nВремя  {FormatTime(flow.MissionTime)}\nКорпус  {(flow.Hull != null ? flow.Hull.Normalized * 100f : 100f):0}%\n\nСТАРТ — ещё раз"
                    : $"{(string.IsNullOrEmpty(flow.FailReason) ? "МИССИЯ ПРОВАЛЕНА" : flow.FailReason)}\n\n{(mission != null ? $"Этап  {mission.StageIndex + 1}/{mission.StageCount}  {mission.Title}" : $"Образцы  {flow.Collected}/{flow.Total}")}\nВремя  {FormatTime(flow.MissionTime)}\n\nСТАРТ — ещё раз",
                _ => "",
            };
        }

        void UpdateCompass()
        {
            if (compassArrow == null || flow.Rover == null) return;
            var target = flow.CurrentTarget();
            compassArrow.gameObject.SetActive(target.HasValue && GameSettings.Current.compass);
            if (!target.HasValue) return;
            Vector3 local = flow.Rover.transform.InverseTransformPoint(target.Value);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            compassArrow.localRotation = Quaternion.Euler(0f, 0f, -angle);
        }

        static string FormatTime(float t)
        {
            int s = Mathf.FloorToInt(t);
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
