using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.Gameplay
{
    /// <summary>Кристалл-образец: вращается, парит, собирается при въезде ровера в триггер.</summary>
    [RequireComponent(typeof(Collider))]
    public class SamplePickup : MonoBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField] float spinSpeed = 45f;
        [SerializeField] float bobHeight = 0.15f;
        [SerializeField] float bobSpeed = 1.5f;

        public bool Collected { get; private set; }
        public GameFlow Flow { get; set; }

        Vector3 visualBase;
        float phase;

        void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            if (visual != null) visualBase = visual.localPosition;
            phase = Random.value * 10f;
        }

        void Update()
        {
            if (visual == null || Collected) return;
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
            visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight);
        }

        void OnTriggerEnter(Collider other)
        {
            if (Collected || Flow == null || Flow.State != GameState.Playing) return;
            if (other.attachedRigidbody == null || other.attachedRigidbody.GetComponent<RoverController>() == null) return;
            Collected = true;
            gameObject.SetActive(false);
            Flow.OnSampleCollected(this);
        }

        /// <summary>Засчитать образец без подъезда (пропуск этапа оператором).</summary>
        public void ForceCollect()
        {
            if (Collected || Flow == null) return;
            Collected = true;
            gameObject.SetActive(false);
            Flow.OnSampleCollected(this);
        }

        public void ResetSample()
        {
            Collected = false;
            gameObject.SetActive(true);
        }
    }
}
