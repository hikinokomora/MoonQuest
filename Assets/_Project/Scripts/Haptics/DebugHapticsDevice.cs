using Mehatron.UI;
using UnityEngine;

namespace Mehatron.Haptics
{
    /// <summary>Виртуальный жилет: рисует сетку моторов (перед/спина) на экране оператора.</summary>
    public class DebugHapticsDevice : VestHapticsDevice
    {
        public override bool IsConnected => true;

        protected override void OutputFrame(float[] front, float[] back) { }

        void OnGUI()
        {
            if (!OperatorPanel.Draw || Model == null) return;
            const float cell = 18f, gap = 3f;
            float gridW = Model.Cols * (cell + gap);
            float gridH = Model.Rows * (cell + gap);
            var area = new Rect(280f, 252f, gridW * 2f + 40f, gridH + 40f);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x + 8f, area.y + 2f, 200f, 20f), "<b>Жилет</b>   перед | спина", OperatorPanel.RichLabel);
            DrawGrid(OutFront, area.x + 10f, area.y + 24f, cell, gap, false);
            DrawGrid(OutBack, area.x + 26f + gridW, area.y + 24f, cell, gap, true);
        }

        void DrawGrid(float[] v, float x0, float y0, float cell, float gap, bool mirror)
        {
            for (int r = 0; r < Model.Rows; r++)
                for (int c = 0; c < Model.Cols; c++)
                {
                    // Перед рисуем как видит оператор (левый бок носящего справа), спину — как со спины.
                    int dc = mirror ? c : Model.Cols - 1 - c;
                    float k = v[r * Model.Cols + c];
                    var rect = new Rect(x0 + dc * (cell + gap), y0 + r * (cell + gap), cell, cell);
                    OperatorPanel.DrawRect(rect, Color.Lerp(new Color(0.15f, 0.15f, 0.18f), new Color(1f, 0.55f, 0.15f), k));
                }
        }
    }
}
