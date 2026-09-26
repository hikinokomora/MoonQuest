using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Mehatron.Haptics
{
    /// <summary>
    /// Жилет bHaptics (TactSuit X16/X40/Pro) через bHaptics SDK2: каждый кадр моторов уходит в
    /// <c>BhapticsLibrary.PlayMotors((int)PositionType.Vest, int[32], durationMillis)</c>.
    /// SDK2 принимает 32 значения 0..100 (перед 4×4, затем спина 4×4) и сам раскладывает их на моторы конкретной модели
    /// (у X16 — по 8 моторов спереди и сзади). Сетку модели жилета пересэмплируем в 4×4.
    /// SDK ищется через reflection: без импортированного плагина компонент просто неактивен (IsConnected = false).
    /// Для работы на сцене нужен префаб [bhaptics] из Assets/Bhaptics/SDK2/Prefabs, а в bHaptics → Developer Window —
    /// App ID и API Key (см. презентацию к курсу). На ПК должен быть запущен bHaptics Player с подключённым жилетом.
    /// </summary>
    public class BhapticsVestDevice : VestHapticsDevice
    {
        const int SdkCols = 4, SdkRows = 4, PerSide = SdkCols * SdkRows;

        [Header("bHaptics SDK2")]
        [Tooltip("PositionType.Vest = 0 в SDK2.")]
        [SerializeField] int position = 0;
        [Tooltip("Длительность каждого кадра, мс. Чуть больше периода отправки, чтобы моторы не «мигали».")]
        [SerializeField] int frameDurationMs = 80;
        [Tooltip("Отражать перед по горизонтали (если левый/правый бок перепутаны).")]
        [SerializeField] bool mirrorFront = false;
        [SerializeField] bool mirrorBack = false;
        [Tooltip("Ниже этого значения (0..100) мотор не включается — экономия и меньше «шума».")]
        [SerializeField] int minMotorValue = 4;

        static MethodInfo playMotors;
        static bool searched;

        readonly int[] motors = new int[PerSide * 2];
        bool wasActive;

        public override bool IsConnected => FindSdk();

        static bool FindSdk()
        {
            if (searched) return playMotors != null;
            searched = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Bhaptics.SDK2.BhapticsLibrary");
                if (t == null) continue;
                playMotors = t.GetMethod("PlayMotors", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(int), typeof(int[]), typeof(int) }, null);
                if (playMotors != null) break;
            }
            if (playMotors == null)
                Debug.LogWarning("[bHaptics] SDK2 не найден (Bhaptics.SDK2.BhapticsLibrary.PlayMotors). Жилет работает только в отладочном виде.");
            return playMotors != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { searched = false; playMotors = null; }

        protected override void OutputFrame(float[] front, float[] back)
        {
            bool any = false;
            any |= Resample(front, 0, mirrorFront);
            any |= Resample(back, PerSide, mirrorBack);
            // Пустые кадры не шлём (кроме одного «обнуляющего»), чтобы не забивать канал до bHaptics Player.
            if (!any && !wasActive) return;
            wasActive = any;
            try { playMotors.Invoke(null, new object[] { position, motors, frameDurationMs }); }
            catch (Exception e) { Debug.LogException(e); enabled = false; }
        }

        /// <summary>Билинейный пересэмпл сетки Model.Cols×Model.Rows в 4×4 SDK. Возвращает true, если есть ненулевые моторы.</summary>
        bool Resample(float[] src, int offset, bool mirror)
        {
            bool any = false;
            for (int r = 0; r < SdkRows; r++)
                for (int c = 0; c < SdkCols; c++)
                {
                    float u = (mirror ? SdkCols - 1 - c : c) / (float)(SdkCols - 1) * (Model.Cols - 1);
                    float v = r / (float)(SdkRows - 1) * (Model.Rows - 1);
                    float k = Sample(src, u, v);
                    int val = Mathf.RoundToInt(k * 100f);
                    if (val < minMotorValue) val = 0;
                    motors[offset + r * SdkCols + c] = val;
                    any |= val > 0;
                }
            return any;
        }

        float Sample(float[] src, float u, float v)
        {
            int c0 = Mathf.FloorToInt(u), r0 = Mathf.FloorToInt(v);
            int c1 = Mathf.Min(c0 + 1, Model.Cols - 1), r1 = Mathf.Min(r0 + 1, Model.Rows - 1);
            float fu = u - c0, fv = v - r0;
            float a = Mathf.Lerp(src[r0 * Model.Cols + c0], src[r0 * Model.Cols + c1], fu);
            float b = Mathf.Lerp(src[r1 * Model.Cols + c0], src[r1 * Model.Cols + c1], fu);
            return Mathf.Clamp01(Mathf.Lerp(a, b, fv));
        }

        /// <summary>Проверка раскладки: моторы SDK по одному (перед 0–15, спина 16–31). Запуск из контекстного меню компонента.</summary>
        [ContextMenu("Тест: моторы по одному")]
        void TestSweep()
        {
            if (Application.isPlaying && FindSdk()) StartCoroutine(Sweep());
        }

        IEnumerator Sweep()
        {
            var one = new int[PerSide * 2];
            for (int i = 0; i < one.Length; i++)
            {
                Array.Clear(one, 0, one.Length);
                one[i] = 100;
                Debug.Log($"[bHaptics] мотор {i} ({(i < PerSide ? "перед" : "спина")}, ряд {(i % PerSide) / SdkCols}, столбец {i % SdkCols})");
                playMotors.Invoke(null, new object[] { position, one, 250 });
                yield return new WaitForSecondsRealtime(0.4f);
            }
        }
    }
}
