using System.Collections.Generic;
using UnityEngine;

namespace Mehatron.Vehicle
{
    /// <summary>
    /// Анимация видимой подвески: рычаги поворачиваются к шаровым опорам на кулаках, амортизаторы тянутся к нижним рычагам.
    /// Работает после <see cref="RoverController"/> (тот двигает колёса и кулаки), связи обрабатываются по порядку списка:
    /// сначала рычаги, потом амортизаторы (их цели сидят на рычагах).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class SuspensionRig : MonoBehaviour
    {
        [System.Serializable]
        public class Link
        {
            [Tooltip("Деталь; её точка опоры — шарнир на корпусе.")]
            public Transform part;
            [Tooltip("Куда деталь должна смотреть (шаровая опора / точка крепления).")]
            public Transform target;
            [Tooltip("Растягивать по длине (амортизатор).")]
            public bool stretch;
            [System.NonSerialized] public Transform pivot;
            [System.NonSerialized] public float baseLength;
        }

        [SerializeField] List<Link> links = new List<Link>();

        void Start()
        {
            foreach (var l in links)
            {
                if (l.part == null || l.target == null) continue;
                // Промежуточная опора: её +Z смотрит вдоль детали, поэтому поворот и растяжение делаются по одной оси.
                var parent = l.part.parent;
                Vector3 dir = l.target.position - l.part.position;
                l.baseLength = dir.magnitude;
                var pivot = new GameObject(l.part.name + "_Pivot").transform;
                pivot.SetParent(parent, false);
                pivot.SetPositionAndRotation(l.part.position, Quaternion.LookRotation(dir, parent.forward));
                l.part.SetParent(pivot, true);
                l.pivot = pivot;
            }
        }

        void LateUpdate()
        {
            foreach (var l in links)
            {
                if (l.pivot == null) continue;
                Vector3 dir = l.target.position - l.pivot.position;
                if (dir.sqrMagnitude < 1e-6f) continue;
                l.pivot.rotation = Quaternion.LookRotation(dir, l.pivot.parent.forward);
                if (l.stretch && l.baseLength > 0f)
                    l.pivot.localScale = new Vector3(1f, 1f, dir.magnitude / l.baseLength);
            }
        }
    }
}
