using System.Collections.Generic;
using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.Gameplay
{
    /// <summary>Игровой цикл: Меню → Экспедиция (этапы ведёт <see cref="ExpeditionMission"/>) → Результат → снова.</summary>
    public class GameFlow : MonoBehaviour
    {
        [SerializeField] RoverController rover;
        [SerializeField] Transform spawnPoint;
        [SerializeField] BaseZone baseZone;
        [SerializeField] OxygenTimer oxygen;
        [SerializeField] MeteorShower meteorShower;
        [SerializeField] ExpeditionMission mission;
        [SerializeField] HullIntegrity hull;
        [SerializeField] VRSeatRig seatRig;
        [SerializeField] List<SamplePickup> samples = new List<SamplePickup>();
        [Tooltip("Лунная гравитация 1.62 м/с² слишком «ватная» для демо — берём ~1/3 земной.")]
        [SerializeField] float gravity = 3.2f;

        public GameState State { get; private set; } = GameState.Menu;
        public int Collected { get; private set; }
        public int Total => samples.Count;
        public float MissionTime { get; private set; }
        public bool LastSuccess { get; private set; }
        public OxygenTimer Oxygen => oxygen;
        public RoverController Rover => rover;
        public ExpeditionMission Mission => mission;
        /// <summary>Старт/рестарт делает игровое меню — кнопка «Старт» сама миссию не запускает.</summary>
        public bool MenuDriven { get; set; }
        public IReadOnlyList<SamplePickup> Samples => samples;
        public HullIntegrity Hull => hull;
        /// <summary>Причина провала для экрана результата.</summary>
        public string FailReason { get; private set; } = "";

        GameState stateBeforePause;

        void Awake()
        {
            Physics.gravity = new Vector3(0f, -gravity, 0f);
            foreach (var s in samples)
                if (s != null) s.Flow = this;
        }

        void Start() => GoToMenu();

        void Update()
        {
            var input = rover != null ? rover.Input : null;
            switch (State)
            {
                case GameState.Menu:
                case GameState.Result:
                    if (!MenuDriven && input != null && input.StartPressed) StartMission();
                    break;
                case GameState.Playing:
                    MissionTime += Time.deltaTime;
                    if (input != null && input.ResetPressed) rover.ResetUpright();
                    RescueIfFellThrough();
                    if (mission != null ? mission.Completed : Collected >= Total && baseZone != null && baseZone.RoverInside) Finish(true);
                    else if (oxygen != null && oxygen.Depleted) Finish(false, "КИСЛОРОД ЗАКОНЧИЛСЯ");
                    else if (hull != null && hull.Broken) Finish(false, "КОРПУС РОВЕРА РАЗРУШЕН");
                    break;
            }
        }

        public void StartMission()
        {
            Time.timeScale = 1f;
            ResetWorld();
            if (oxygen != null) oxygen.Running = true;
            if (mission == null && meteorShower != null) meteorShower.Arm();
            rover.ControlsEnabled = true;
            if (seatRig != null) seatRig.Recenter();
            SetState(GameState.Playing);
            if (mission != null) mission.Begin();
        }

        public void GoToMenu()
        {
            Time.timeScale = 1f;
            ResetWorld();
            SetState(GameState.Menu);
        }

        public void TogglePause()
        {
            if (State == GameState.Playing)
            {
                stateBeforePause = State;
                Time.timeScale = 0f;
                SetState(GameState.Paused);
            }
            else if (State == GameState.Paused)
            {
                Time.timeScale = 1f;
                SetState(stateBeforePause);
            }
        }

        public void OnSampleCollected(SamplePickup sample)
        {
            Collected++;
            GameEvents.RaiseSampleCollected(Collected, Total);
        }

        /// <summary>Куда вести игрока: цель текущего этапа миссии (или ближайший образец / база без миссии).</summary>
        public Vector3? CurrentTarget()
        {
            if (rover == null) return null;
            if (mission != null) return mission.CurrentTarget();
            if (Collected >= Total) return baseZone != null ? baseZone.transform.position : (Vector3?)null;
            return NearestSample();
        }

        public Vector3? NearestSample()
        {
            if (rover == null) return null;
            Vector3 p = rover.transform.position;
            SamplePickup best = null;
            float bestD = float.MaxValue;
            foreach (var s in samples)
            {
                if (s == null || s.Collected) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best != null ? best.transform.position : (Vector3?)null;
        }

        void Finish(bool success, string reason = "")
        {
            LastSuccess = success;
            FailReason = reason;
            if (mission != null) mission.Stop();
            int t = Mathf.FloorToInt(MissionTime);
            Debug.Log($"[Mission] {t / 60}:{t % 60:00}  {(success ? "УСПЕХ" : "ПРОВАЛ: " + reason)}");
            if (oxygen != null) oxygen.Running = false;
            if (meteorShower != null) meteorShower.StopShower();
            rover.ControlsEnabled = false;
            SetState(GameState.Result);
            GameEvents.RaiseMissionFinished(success);
        }

        /// <summary>Страховка: если физика протолкнула ровер под террейн, ставим его обратно на грунт.</summary>
        void RescueIfFellThrough()
        {
            var t = Terrain.activeTerrain;
            if (t == null || rover == null) return;
            Vector3 p = rover.transform.position;
            float ground = t.SampleHeight(p) + t.transform.position.y;
            if (p.y > ground - 5f) return;
            Vector3 fwd = Vector3.ProjectOnPlane(rover.transform.forward, Vector3.up);
            rover.Teleport(new Vector3(p.x, ground + 2f, p.z), Quaternion.LookRotation(fwd.sqrMagnitude > 0.01f ? fwd : Vector3.forward));
            Debug.LogWarning("[GameFlow] Ровер провалился под грунт — возвращён на поверхность.");
        }

        void ResetWorld()
        {
            Collected = 0;
            MissionTime = 0f;
            foreach (var s in samples)
                if (s != null) s.ResetSample();
            if (oxygen != null) oxygen.ResetTimer();
            if (mission != null) mission.ResetMission();
            else if (hull != null) hull.ResetHull();
            if (meteorShower != null) meteorShower.StopShower();
            if (rover != null)
            {
                rover.ControlsEnabled = false;
                rover.Roughness = 0f;
                if (spawnPoint != null) rover.Teleport(spawnPoint.position, spawnPoint.rotation);
            }
        }

        void SetState(GameState s)
        {
            State = s;
            GameEvents.RaiseStateChanged(s);
        }
    }
}
