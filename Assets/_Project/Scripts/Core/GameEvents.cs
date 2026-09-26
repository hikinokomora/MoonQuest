using System;
using UnityEngine;

namespace Mehatron
{
    public enum GameState { Menu, Playing, Paused, Result }

    public enum HapticPattern
    {
        Landing, SampleCollected, Heartbeat, MeteorWarning, MissionComplete, MissionFailed,
        /// <summary>Проверка систем: моторы по очереди, перед → спина.</summary>
        SystemsCheck,
        /// <summary>Новая задача: двойной тап по плечам спереди.</summary>
        Notify,
        /// <summary>Задача выполнена: кольцо вокруг корпуса.</summary>
        ObjectiveComplete,
        /// <summary>Импульс сканирования/бурения, сила растёт с прогрессом.</summary>
        ScanPulse,
        /// <summary>Лунотрясение: волны снизу вверх.</summary>
        Quake,
        /// <summary>Разряд статического электричества: россыпь коротких «искр».</summary>
        StaticShock,
        /// <summary>Пополнение кислорода: прохладная волна от пояса к плечам.</summary>
        OxygenRefill,
        /// <summary>Тревога корпуса (вкл/выкл как Heartbeat): удары по бокам.</summary>
        HullAlarm,
    }

    /// <summary>
    /// Шина игровых событий. Геймплей только публикует события, а платформа и жилет подписываются на них,
    /// поэтому игровой код ничего не знает о железе.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>Ровер приземлился после полёта. Параметр: сила 0..1.</summary>
        public static event Action<float> Landed;
        /// <summary>Удар/взрыв рядом с ровером. Параметры: мировая точка, сила 0..1.</summary>
        public static event Action<Vector3, float> Impact;
        /// <summary>Столкновение кузова с препятствием (для урона корпусу). Параметры: точка, сила 0..1.</summary>
        public static event Action<Vector3, float> Collided;
        /// <summary>Взрыв метеорита (для урона корпусу). Параметры: точка, сила 0..1 по расстоянию.</summary>
        public static event Action<Vector3, float> MeteorExploded;
        /// <summary>Собран образец. Параметры: собрано, всего.</summary>
        public static event Action<int, int> SampleCollected;
        public static event Action<bool> LowOxygenChanged;
        public static event Action MeteorShowerStarted;
        public static event Action<GameState> StateChanged;
        /// <summary>Миссия завершена. Параметр: успех.</summary>
        public static event Action<bool> MissionFinished;

        /// <summary>Старт миссии: тест жилета и платформы.</summary>
        public static event Action SystemsCheck;
        /// <summary>Новый этап. Параметры: номер (с 0), заголовок.</summary>
        public static event Action<int, string> ObjectiveChanged;
        /// <summary>Выполнена задача (этап или его часть). Параметр: текст для HUD.</summary>
        public static event Action<string> TaskDone;
        /// <summary>Тик сканирования/бурения, прогресс 0..1.</summary>
        public static event Action<float> ScanPulse;
        /// <summary>Лунотрясение. Параметры: длительность, сила 0..1.</summary>
        public static event Action<float, float> QuakeStarted;
        public static event Action<bool> StormChanged;
        /// <summary>Разряд статики рядом с ровером. Параметры: точка, сила 0..1.</summary>
        public static event Action<Vector3, float> StaticDischarge;
        /// <summary>Порыв ветра в буре. Параметр: -1 слева … +1 справа (откуда дует).</summary>
        public static event Action<float> Gust;
        /// <summary>Пополнен кислород. Параметр: добавлено секунд.</summary>
        public static event Action<float> OxygenRefilled;
        /// <summary>Повреждение корпуса. Параметр: прочность 0..1 после удара.</summary>
        public static event Action<float> HullDamaged;
        public static event Action<bool> HullCriticalChanged;

        public static void RaiseLanded(float strength) => Landed?.Invoke(Mathf.Clamp01(strength));
        public static void RaiseImpact(Vector3 point, float strength) => Impact?.Invoke(point, Mathf.Clamp01(strength));
        public static void RaiseCollided(Vector3 point, float strength) => Collided?.Invoke(point, Mathf.Clamp01(strength));
        public static void RaiseMeteorExploded(Vector3 point, float strength) => MeteorExploded?.Invoke(point, Mathf.Clamp01(strength));
        public static void RaiseSampleCollected(int collected, int total) => SampleCollected?.Invoke(collected, total);
        public static void RaiseLowOxygenChanged(bool low) => LowOxygenChanged?.Invoke(low);
        public static void RaiseMeteorShowerStarted() => MeteorShowerStarted?.Invoke();
        public static void RaiseStateChanged(GameState state) => StateChanged?.Invoke(state);
        public static void RaiseMissionFinished(bool success) => MissionFinished?.Invoke(success);
        public static void RaiseSystemsCheck() => SystemsCheck?.Invoke();
        public static void RaiseObjectiveChanged(int index, string title) => ObjectiveChanged?.Invoke(index, title);
        public static void RaiseTaskDone(string text) => TaskDone?.Invoke(text);
        public static void RaiseScanPulse(float progress) => ScanPulse?.Invoke(Mathf.Clamp01(progress));
        public static void RaiseQuakeStarted(float duration, float strength) => QuakeStarted?.Invoke(duration, Mathf.Clamp01(strength));
        public static void RaiseStormChanged(bool active) => StormChanged?.Invoke(active);
        public static void RaiseStaticDischarge(Vector3 point, float strength) => StaticDischarge?.Invoke(point, Mathf.Clamp01(strength));
        public static void RaiseGust(float side) => Gust?.Invoke(Mathf.Clamp(side, -1f, 1f));
        public static void RaiseOxygenRefilled(float seconds) => OxygenRefilled?.Invoke(seconds);
        public static void RaiseHullDamaged(float hull) => HullDamaged?.Invoke(Mathf.Clamp01(hull));
        public static void RaiseHullCriticalChanged(bool critical) => HullCriticalChanged?.Invoke(critical);

        // В проекте отключена перезагрузка домена при входе в Play Mode — чистим подписчиков вручную.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Landed = null;
            Impact = null;
            Collided = null;
            MeteorExploded = null;
            SampleCollected = null;
            LowOxygenChanged = null;
            MeteorShowerStarted = null;
            StateChanged = null;
            MissionFinished = null;
            SystemsCheck = null;
            ObjectiveChanged = null;
            TaskDone = null;
            ScanPulse = null;
            QuakeStarted = null;
            StormChanged = null;
            StaticDischarge = null;
            Gust = null;
            OxygenRefilled = null;
            HullDamaged = null;
            HullCriticalChanged = null;
        }
    }
}
