using System.Collections.Generic;
using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.Gameplay
{
    public enum StageKind { SystemsCheck, Geology, JumpTrial, Relays, SupplyDrop, CoreSample, ReturnToBase }

    /// <summary>
    /// Сценарий экспедиции (~10–15 минут): этапы идут по порядку, каждый даёт цель для компаса/автопилота
    /// и публикует события, на которые реагируют платформа и жилет.
    /// </summary>
    public class ExpeditionMission : MonoBehaviour
    {
        [SerializeField] GameFlow flow;
        [SerializeField] RoverController rover;
        [SerializeField] OxygenTimer oxygen;
        [SerializeField] HullIntegrity hull;
        [SerializeField] MeteorShower meteors;
        [SerializeField] DustStorm storm;
        [SerializeField] BaseZone baseZone;

        [Header("Точки")]
        [Tooltip("Трамплины: ось +Z смотрит по направлению прыжка.")]
        [SerializeField] List<Transform> ramps = new List<Transform>();
        [Tooltip("Ретрансляторы — разворачиваются в этом порядке.")]
        [SerializeField] List<MissionPoint> relays = new List<MissionPoint>();
        [SerializeField] MissionPoint supplyPod;
        [SerializeField] MissionPoint coreSample;

        [Header("Этапы")]
        [SerializeField] float systemsCheckTime = 8f;
        [SerializeField] int jumpsRequired = 3;
        [SerializeField] float minJumpStrength = 0.3f;
        [Tooltip("Приземление засчитывается как прыжок, только если трамплин ближе этого расстояния.")]
        [SerializeField] float rampJumpRadius = 35f;
        [Tooltip("Если игрок не справился с прыжками, этап засчитывается через это время.")]
        [SerializeField] float jumpTrialTimeout = 150f;
        [SerializeField] float relayHoldTime = 12f;
        [SerializeField] float coreHoldTime = 15f;
        [Tooltip("Удержание засчитывается только почти на месте.")]
        [SerializeField] float holdMaxSpeed = 2.2f;

        [Header("События")]
        [SerializeField] float quakeDelay = 5f;
        [SerializeField] float quakeDuration = 8f;
        [Range(0f, 1f)] [SerializeField] float quakeStrength = 0.8f;
        [SerializeField] float quakeImpulse = 1.2f;
        [SerializeField] float meteorDuration = 70f;
        [SerializeField] float podDropHeight = 160f;
        [SerializeField] float podFallTime = 4f;
        [SerializeField] float podOxygen = 360f;
        [SerializeField] float podRepair = 40f;
        [Tooltip("Буря стихает, когда ровер подъезжает к базе ближе этого расстояния.")]
        [SerializeField] float stormEndDistance = 130f;

        static readonly string[] Titles =
        {
            "ПРОВЕРКА СИСТЕМ", "ГЕОЛОГИЯ", "ИСПЫТАНИЕ ПОДВЕСКИ", "СВЯЗЬ С ЗЕМЛЁЙ", "СБРОС С ОРБИТЫ", "ПЫЛЕВАЯ БУРЯ", "ВОЗВРАЩЕНИЕ",
        };

        public StageKind Stage { get; private set; }
        public int StageIndex => (int)Stage;
        public int StageCount => Titles.Length;
        public string Title => Titles[StageIndex];
        /// <summary>Подсказка, что делать сейчас (строка на HUD).</summary>
        public string Hint { get; private set; } = "";
        public bool Completed { get; private set; }
        public bool Running { get; private set; }
        /// <summary>Прогресс удержания 0..1 (развёртывание/бурение) или -1.</summary>
        public float HoldProgress { get; private set; } = -1f;
        /// <summary>Ровер в зоне удержания — автопилоту пора тормозить.</summary>
        public bool StopHere { get; private set; }

        float stageTime;
        int jumps;
        int relayIndex;
        float hold;
        float nextPulse;
        float quakeAt = -1f, quakeLeft;
        bool podLanded, podPicked;
        float podPickedAt;
        int rampIndex = -1;
        bool rampApproachDone;

        void OnEnable() => GameEvents.Landed += OnLanded;
        void OnDisable() => GameEvents.Landed -= OnLanded;

        public void ResetMission()
        {
            Running = false;
            Completed = false;
            Stage = StageKind.SystemsCheck;
            HoldProgress = -1f;
            StopHere = false;
            quakeAt = -1f;
            quakeLeft = 0f;
            foreach (var r in relays) if (r != null) r.ResetPoint();
            if (supplyPod != null) supplyPod.ResetPoint();
            if (coreSample != null) coreSample.ResetPoint();
            if (meteors != null) meteors.StopShower();
            if (storm != null) storm.StopImmediate();
            if (hull != null) hull.ResetHull();
            // Сброшенный груз ещё «на орбите»: прячем до начала этапа.
            if (supplyPod != null) supplyPod.gameObject.SetActive(false);
        }

        public void Begin()
        {
            ResetMission();
            Running = true;
            if (hull != null) hull.Active = true;
            EnterStage(StageKind.SystemsCheck);
        }

        public void Stop()
        {
            Running = false;
            if (hull != null) hull.Active = false;
            if (meteors != null) meteors.StopShower();
            if (storm != null) storm.End();
            HoldProgress = -1f;
        }

        void EnterStage(StageKind s)
        {
            Stage = s;
            stageTime = 0f;
            hold = 0f;
            HoldProgress = -1f;
            switch (s)
            {
                case StageKind.SystemsCheck:
                    GameEvents.RaiseSystemsCheck();
                    break;
                case StageKind.JumpTrial:
                    jumps = 0;
                    rampIndex = -1;
                    break;
                case StageKind.Relays:
                    relayIndex = 0;
                    ActivateRelay();
                    break;
                case StageKind.SupplyDrop:
                    podLanded = podPicked = false;
                    if (supplyPod != null)
                    {
                        supplyPod.gameObject.SetActive(true);
                        supplyPod.ResetPoint();
                        supplyPod.SetActive(true);
                        supplyPod.Drop(podDropHeight, podFallTime, OnPodLanded);
                    }
                    if (meteors != null) meteors.Begin(meteorDuration);
                    break;
                case StageKind.CoreSample:
                    if (coreSample != null) coreSample.SetActive(true);
                    if (storm != null) storm.Begin();
                    break;
            }
            GameEvents.RaiseObjectiveChanged(StageIndex, Title);
            int t = flow != null ? Mathf.FloorToInt(flow.MissionTime) : 0;
            Debug.Log($"[Mission] {t / 60}:{t % 60:00}  этап {StageIndex + 1}/{StageCount} «{Title}»  O2 {(oxygen != null ? oxygen.Remaining : 0f):0} c  корпус {(hull != null ? hull.Normalized * 100f : 100f):0}%");
        }

        void NextStage()
        {
            if (Stage == StageKind.ReturnToBase)
            {
                Completed = true;
                Running = false;
                return;
            }
            EnterStage(Stage + 1);
        }

        void Update()
        {
            if (!Running || flow == null || flow.State != GameState.Playing) return;
            stageTime += Time.deltaTime;
            StopHere = false;
            UpdateQuake();

            switch (Stage)
            {
                case StageKind.SystemsCheck:
                    Hint = stageTime < systemsCheckTime * 0.5f ? "Тест тактильного жилета…" : "Тест платформы…";
                    if (stageTime >= systemsCheckTime)
                    {
                        GameEvents.RaiseTaskDone("СИСТЕМЫ В НОРМЕ");
                        NextStage();
                    }
                    break;

                case StageKind.Geology:
                    Hint = $"Соберите образцы в кратерах  {flow.Collected}/{flow.Total}";
                    if (flow.Collected >= flow.Total)
                    {
                        GameEvents.RaiseTaskDone("ВСЕ ОБРАЗЦЫ СОБРАНЫ");
                        NextStage();
                    }
                    break;

                case StageKind.JumpTrial:
                    Hint = $"Прыгните с трамплина  {jumps}/{jumpsRequired}";
                    if (jumps >= jumpsRequired || stageTime >= jumpTrialTimeout)
                    {
                        GameEvents.RaiseTaskDone(jumps >= jumpsRequired ? "ПОДВЕСКА В НОРМЕ" : "ИСПЫТАНИЕ ЗАСЧИТАНО");
                        NextStage();
                    }
                    break;

                case StageKind.Relays:
                    UpdateRelays();
                    break;

                case StageKind.SupplyDrop:
                    UpdateSupply();
                    break;

                case StageKind.CoreSample:
                    Hint = "Пробурите керн в Большом кратере";
                    if (coreSample != null && Hold(coreSample, coreHoldTime, "Бурение"))
                    {
                        coreSample.Complete();
                        GameEvents.RaiseTaskDone("КЕРН ПОЛУЧЕН");
                        NextStage();
                    }
                    break;

                case StageKind.ReturnToBase:
                    Hint = "Вернитесь на базу";
                    if (storm != null && storm.Active && baseZone != null && rover != null &&
                        Vector3.Distance(rover.transform.position, baseZone.transform.position) < stormEndDistance)
                    {
                        storm.End();
                        GameEvents.RaiseTaskDone("БУРЯ СТИХАЕТ");
                    }
                    if (baseZone != null && baseZone.RoverInside && rover != null && rover.SpeedMs < 4f)
                        NextStage();
                    break;
            }
        }

        /// <summary>Для оператора/тестов: засчитать текущий этап.</summary>
        public void DebugSkipStage()
        {
            if (!Running) return;
            if (Stage == StageKind.Geology)
                foreach (var s in flow.Samples) if (s != null && !s.Collected) s.ForceCollect();
            if (Stage == StageKind.Relays)
                for (; relayIndex < relays.Count; relayIndex++) if (relays[relayIndex] != null) relays[relayIndex].Complete();
            if (Stage == StageKind.SupplyDrop && meteors != null) meteors.StopSpawning();
            if (Stage == StageKind.CoreSample && coreSample != null) coreSample.Complete();
            Debug.Log($"[Mission] Этап «{Title}» пропущен оператором.");
            NextStage();
        }

        // ---------------------------------------------------------------- этапы

        void ActivateRelay()
        {
            for (int i = 0; i < relays.Count; i++)
                if (relays[i] != null) relays[i].SetActive(i == relayIndex);
        }

        void UpdateRelays()
        {
            if (relayIndex >= relays.Count)
            {
                GameEvents.RaiseTaskDone("СВЯЗЬ С ЗЕМЛЁЙ УСТАНОВЛЕНА");
                NextStage();
                return;
            }
            var r = relays[relayIndex];
            Hint = $"Разверните ретранслятор  {relayIndex + 1}/{relays.Count}";
            if (r == null || !Hold(r, relayHoldTime, "Развёртывание")) return;

            r.Complete();
            relayIndex++;
            hold = 0f;
            HoldProgress = -1f;
            if (relayIndex == 1) quakeAt = stageTime + quakeDelay;
            if (relayIndex < relays.Count)
            {
                GameEvents.RaiseTaskDone($"РЕТРАНСЛЯТОР {relayIndex}/{relays.Count} РАЗВЁРНУТ");
                ActivateRelay();
            }
        }

        void UpdateSupply()
        {
            if (supplyPod == null) { NextStage(); return; }
            if (!podLanded)
            {
                Hint = "Контейнер с кислородом падает — следите за небом";
                return;
            }
            if (!podPicked)
            {
                Hint = "Заберите контейнер с кислородом";
                if (supplyPod.RoverInside)
                {
                    podPicked = true;
                    podPickedAt = stageTime;
                    supplyPod.Complete();
                    if (oxygen != null) oxygen.AddTime(podOxygen);
                    if (hull != null) hull.Repair(podRepair);
                    if (meteors != null) meteors.StopSpawning();
                    GameEvents.RaiseTaskDone("КИСЛОРОД ПОПОЛНЕН\nКОРПУС ПОДЛАТАН");
                }
                return;
            }
            Hint = "Метеоритный дождь стихает…";
            if (stageTime - podPickedAt > 4f) NextStage();
        }

        void OnPodLanded(Vector3 point)
        {
            podLanded = true;
            GameEvents.RaiseImpact(point, rover != null
                ? 1f - Mathf.InverseLerp(10f, 120f, Vector3.Distance(point, rover.transform.position))
                : 0.5f);
        }

        void OnLanded(float strength)
        {
            if (!Running || Stage != StageKind.JumpTrial || strength < minJumpStrength || !NearRamp()) return;
            jumps++;
            rampIndex = -1;
            if (jumps < jumpsRequired) GameEvents.RaiseTaskDone($"ПРЫЖОК {jumps}/{jumpsRequired}");
        }

        /// <summary>Прыжок засчитываем только у трамплина, а не на кочках.</summary>
        bool NearRamp()
        {
            if (rover == null) return false;
            foreach (var r in ramps)
                if (r != null && Vector3.Distance(r.position, rover.transform.position) < rampJumpRadius) return true;
            return false;
        }

        /// <summary>Удержание в зоне точки на малой скорости; публикует импульсы сканирования.</summary>
        bool Hold(MissionPoint p, float time, string label)
        {
            if (!p.RoverInside)
            {
                // Вне зоны прогресс медленно теряется — нельзя «проскочить» точку.
                hold = Mathf.Max(0f, hold - Time.deltaTime * 0.5f);
                HoldProgress = hold > 0f ? hold / time : -1f;
                return false;
            }
            StopHere = true;
            bool still = rover == null || rover.SpeedMs <= holdMaxSpeed;
            if (still) hold += Time.deltaTime;
            float k = Mathf.Clamp01(hold / time);
            HoldProgress = k;
            Hint = still ? $"{label}…  {k * 100f:0}%" : "Остановитесь в зоне";
            if (still)
            {
                nextPulse -= Time.deltaTime;
                if (nextPulse <= 0f)
                {
                    nextPulse = Mathf.Lerp(0.7f, 0.18f, k);
                    GameEvents.RaiseScanPulse(k);
                }
            }
            return hold >= time;
        }

        void UpdateQuake()
        {
            if (quakeAt >= 0f && stageTime >= quakeAt)
            {
                quakeAt = -1f;
                quakeLeft = quakeDuration;
                GameEvents.RaiseQuakeStarted(quakeDuration, quakeStrength);
            }
            if (quakeLeft <= 0f || rover == null) return;
            quakeLeft -= Time.deltaTime;
            // Грунт ходит под колёсами: случайные вертикальные и боковые толчки кузова.
            if (rover.IsGrounded && Random.value < 0.15f)
            {
                Vector3 kick = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.4f, 1f), Random.Range(-0.6f, 0.6f));
                float fade = Mathf.Clamp01(quakeLeft / quakeDuration);
                rover.Body.AddForceAtPosition(kick * quakeImpulse * quakeStrength * fade * rover.Body.mass,
                    rover.transform.position + Random.insideUnitSphere, ForceMode.Impulse);
            }
        }

        // ---------------------------------------------------------------- цель

        /// <summary>Куда вести игрока (компас, автопилот).</summary>
        public Vector3? CurrentTarget()
        {
            if (!Running || rover == null) return null;
            Vector3 pos = rover.transform.position;
            switch (Stage)
            {
                case StageKind.Geology: return flow.NearestSample();
                case StageKind.JumpTrial: return RampTarget(pos);
                case StageKind.Relays:
                    return relayIndex < relays.Count && relays[relayIndex] != null ? relays[relayIndex].Position : (Vector3?)null;
                case StageKind.SupplyDrop: return supplyPod != null && !podPicked ? supplyPod.Position : (Vector3?)null;
                case StageKind.CoreSample: return coreSample != null ? coreSample.Position : (Vector3?)null;
                case StageKind.ReturnToBase: return baseZone != null ? baseZone.transform.position : (Vector3?)null;
                default: return null;
            }
        }

        /// <summary>Сначала точка разгона перед трамплином, затем точка за ним по оси прыжка.</summary>
        Vector3? RampTarget(Vector3 pos)
        {
            if (ramps.Count == 0) return null;
            if (rampIndex < 0)
            {
                float best = float.MaxValue;
                for (int i = 0; i < ramps.Count; i++)
                {
                    if (ramps[i] == null) continue;
                    float d = (Approach(ramps[i]) - pos).sqrMagnitude;
                    if (d < best) { best = d; rampIndex = i; }
                }
                rampApproachDone = false;
                if (rampIndex < 0) return null;
            }
            var r = ramps[rampIndex];
            Vector3 approach = Approach(r);
            if (!rampApproachDone && Vector3.ProjectOnPlane(approach - pos, Vector3.up).magnitude < 8f) rampApproachDone = true;
            // Проехали трамплин и не взлетели — выбираем заново.
            if (rampApproachDone && Vector3.Dot(pos - r.position, r.forward) > 20f) rampIndex = -1;
            return rampApproachDone ? r.position + r.forward * 25f : approach;
        }

        static Vector3 Approach(Transform ramp) => ramp.position - ramp.forward * 35f;
    }
}
