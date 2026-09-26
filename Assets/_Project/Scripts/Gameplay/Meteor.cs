using System;
using UnityEngine;

namespace Mehatron.Gameplay
{
    /// <summary>Метеор летит по прямой к заранее найденной точке на грунте и взрывается.</summary>
    public class Meteor : MonoBehaviour
    {
        Vector3 start, target;
        float time, duration;
        Action<Vector3> onHit;

        public void Launch(Vector3 from, Vector3 to, float fallTime, Action<Vector3> hit)
        {
            start = from;
            target = to;
            duration = Mathf.Max(0.1f, fallTime);
            onHit = hit;
            transform.position = from;
        }

        void Update()
        {
            time += Time.deltaTime;
            float t = time / duration;
            transform.position = Vector3.Lerp(start, target, t * t); // ускоряется к земле
            if (t < 1f) return;
            onHit?.Invoke(target);
            Destroy(gameObject);
        }
    }
}
