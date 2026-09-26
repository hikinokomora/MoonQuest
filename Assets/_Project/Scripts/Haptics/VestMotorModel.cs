using UnityEngine;

namespace Mehatron.Haptics
{
    /// <summary>
    /// Модель жилета: сетка моторов спереди и сзади (по умолчанию 4×5 + 4×5, как bHaptics TactSuit X40).
    /// Индекс мотора = row * Cols + col, row 0 — верх, col 0 — левый бок носящего.
    /// </summary>
    public class VestMotorModel
    {
        public readonly int Cols;
        public readonly int Rows;
        public readonly float[] Front;
        public readonly float[] Back;

        public VestMotorModel(int cols = 4, int rows = 5)
        {
            Cols = cols;
            Rows = rows;
            Front = new float[cols * rows];
            Back = new float[cols * rows];
        }

        public int MotorsPerSide => Cols * Rows;

        /// <summary>Угол мотора вокруг корпуса: 0 — центр груди, 90 — правый бок, 180 — центр спины.</summary>
        public float MotorAngle(bool front, int col)
        {
            float t = Cols == 1 ? 0.5f : col / (float)(Cols - 1); // 0 — левый бок, 1 — правый
            // Спина: от левого бока (-130°) через 180° к правому (-230° == 130°).
            return front ? Mathf.Lerp(-50f, 50f, t) : -130f - 100f * t;
        }

        public float MotorHeight(int row) => Rows == 1 ? 0.5f : 1f - row / (float)(Rows - 1);

        public void Pulse(bool front, int col, int row, float intensity)
        {
            if (col < 0 || col >= Cols || row < 0 || row >= Rows) return;
            var side = front ? Front : Back;
            int i = row * Cols + col;
            side[i] = Mathf.Max(side[i], Mathf.Clamp01(intensity));
        }

        public void PulseRow(int row, float intensity, bool front = true, bool back = true)
        {
            for (int c = 0; c < Cols; c++)
            {
                if (front) Pulse(true, c, row, intensity);
                if (back) Pulse(false, c, row, intensity);
            }
        }

        public void PulseAll(float intensity, bool front = true, bool back = true)
        {
            for (int r = 0; r < Rows; r++) PulseRow(r, intensity, front, back);
        }

        /// <summary>Направленный удар с гауссовым спадом по углу и высоте.</summary>
        public void Impact(float angleDeg, float height, float intensity, float angleSpread = 50f, float heightSpread = 0.45f)
        {
            for (int side = 0; side < 2; side++)
            {
                bool front = side == 0;
                for (int c = 0; c < Cols; c++)
                {
                    float da = Mathf.DeltaAngle(angleDeg, MotorAngle(front, c)) / angleSpread;
                    for (int r = 0; r < Rows; r++)
                    {
                        float dh = (height - MotorHeight(r)) / heightSpread;
                        float w = Mathf.Exp(-(da * da + dh * dh));
                        if (w > 0.05f) Pulse(front, c, r, intensity * w);
                    }
                }
            }
        }

        public void Decay(float amount)
        {
            for (int i = 0; i < Front.Length; i++)
            {
                Front[i] = Mathf.Max(0f, Front[i] - amount);
                Back[i] = Mathf.Max(0f, Back[i] - amount);
            }
        }

        public void Clear()
        {
            System.Array.Clear(Front, 0, Front.Length);
            System.Array.Clear(Back, 0, Back.Length);
        }
    }
}
