using System;
using UnityEngine;

namespace Mehatron.Audio
{
    /// <summary>
    /// Процедурные звуки: в проекте нет аудиофайлов, всё синтезируется при запуске (≈0.3 c).
    /// Петли склеиваются кроссфейдом конца в начало, чтобы не было щелчка на стыке.
    /// </summary>
    public static class SynthClips
    {
        const int Rate = 44100;
        static System.Random rnd = new System.Random(7);

        static float Noise() => (float)(rnd.NextDouble() * 2.0 - 1.0);

        static AudioClip Make(string name, float seconds, Func<float, float> f, float gain = 1f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f(i / (float)Rate) * gain;
            Normalize(data, gain);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Бесшовная петля: генерируем seconds + fade и вмешиваем хвост в начало.</summary>
        static AudioClip MakeLoop(string name, float seconds, Func<float, float> f, float gain = 1f, float fade = 0.5f)
        {
            int n = Mathf.CeilToInt(seconds * Rate), nf = Mathf.CeilToInt(fade * Rate);
            var raw = new float[n + nf];
            for (int i = 0; i < raw.Length; i++) raw[i] = f(i / (float)Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                data[i] = raw[i];
                if (i < nf)
                {
                    float k = i / (float)nf;
                    data[i] = raw[i] * k + raw[n + i] * (1f - k);
                }
            }
            Normalize(data, gain);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 0f;
            foreach (var v in d) m = Mathf.Max(m, Mathf.Abs(v));
            if (m < 1e-5f) return;
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }

        static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        /// <summary>Однополюсный ФНЧ над шумом (состояние в замыкании).</summary>
        static Func<float> LowNoise(float cutoffHz)
        {
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / Rate), y = 0f;
            return () => y += a * (Noise() - y);
        }

        // ---------------------------------------------------------------- интерфейс

        public static AudioClip UiHover() => Make("UiHover", 0.05f, t => Sin(1500f, t) * Env(t, 0.002f, 0.012f), 0.35f);

        public static AudioClip UiClick() => Make("UiClick", 0.12f,
            t => (Sin(880f, t) + 0.6f * Sin(1320f, t)) * Env(t, 0.002f, 0.03f) + Sin(1760f, t) * Env(t - 0.04f, 0.002f, 0.03f) * (t > 0.04f ? 0.5f : 0f), 0.5f);

        public static AudioClip UiBack() => Make("UiBack", 0.14f, t => Sin(900f - 2800f * t, t) * Env(t, 0.003f, 0.05f), 0.45f);

        public static AudioClip UiAdjust() => Make("UiAdjust", 0.04f, t => Sin(2200f, t) * Env(t, 0.001f, 0.008f), 0.3f);

        // ---------------------------------------------------------------- игровые события

        static AudioClip Arp(string name, float[] notes, float step, float decay, float gain)
        {
            return Make(name, step * notes.Length + decay * 4f, t =>
            {
                float s = 0f;
                for (int i = 0; i < notes.Length; i++)
                {
                    float lt = t - i * step;
                    if (lt < 0f) continue;
                    s += (Sin(notes[i], lt) + 0.3f * Sin(notes[i] * 2f, lt)) * Env(lt, 0.004f, decay);
                }
                return s;
            }, gain);
        }

        public static AudioClip Chime() => Arp("Chime", new[] { 783.99f, 987.77f, 1174.66f, 1567.98f }, 0.08f, 0.25f, 0.55f);
        public static AudioClip Notify() => Arp("Notify", new[] { 659.25f, 987.77f }, 0.13f, 0.12f, 0.45f);
        public static AudioClip Success() => Arp("Success", new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f }, 0.14f, 0.5f, 0.6f);
        public static AudioClip Fail() => Arp("Fail", new[] { 392f, 329.63f, 261.63f, 196f }, 0.22f, 0.45f, 0.6f);
        public static AudioClip ScanBeep() => Make("Scan", 0.09f, t => Sin(1050f, t) * Env(t, 0.003f, 0.025f), 0.4f);

        public static AudioClip Alarm() => Make("Alarm", 0.9f, t =>
        {
            float hz = t % 0.3f < 0.15f ? 720f : 540f;
            float sq = Mathf.Sign(Sin(hz, t)) * 0.4f + Sin(hz, t) * 0.6f;
            return sq * (t < 0.85f ? 1f : (0.9f - t) / 0.05f);
        }, 0.35f);

        public static AudioClip Thump()
        {
            var ln = LowNoise(400f);
            return Make("Thump", 0.45f, t => Sin(85f - 50f * t, t) * Env(t, 0.003f, 0.12f) + ln() * 2.5f * Env(t, 0.001f, 0.05f), 0.9f);
        }

        public static AudioClip Boom()
        {
            var ln = LowNoise(250f);
            var ln2 = LowNoise(1200f);
            return Make("Boom", 2.2f, t => ln() * 4f * Env(t, 0.01f, 0.5f) + ln2() * 0.8f * Env(t, 0.002f, 0.08f) + Sin(48f, t) * Env(t, 0.01f, 0.4f), 1f);
        }

        public static AudioClip Zap() => Make("Zap", 0.5f, t =>
        {
            // Треск: редкие импульсы + высокочастотное шипение.
            float crack = rnd.NextDouble() < 0.02 ? Noise() * 3f : 0f;
            return (Noise() * 0.5f + crack) * Env(t, 0.001f, 0.12f) * (0.6f + 0.4f * Sin(60f, t));
        }, 0.7f);

        public static AudioClip Hiss()
        {
            var ln = LowNoise(3000f);
            return Make("Hiss", 1.6f, t => ln() * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.6f)), 0.55f);
        }

        public static AudioClip Rumble()
        {
            var ln = LowNoise(90f);
            return Make("Quake", 4f, t => (ln() * 6f + Sin(32f, t) * 0.4f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 4f)), 1f);
        }

        // ---------------------------------------------------------------- петли

        public static AudioClip EngineLoop()
        {
            var ln = LowNoise(600f);
            // Электромотор ровера: гармоники «вой» + мягкий шум трансмиссии.
            return MakeLoop("Engine", 2f, t => Sin(110f, t) * 0.5f + Sin(220f, t) * 0.3f + Sin(330f, t) * 0.12f + Sin(660f, t) * 0.05f + ln() * 1.2f, 0.6f);
        }

        public static AudioClip WindLoop()
        {
            var ln = LowNoise(500f);
            var ln2 = LowNoise(2500f);
            return MakeLoop("Wind", 6f, t => ln() * (0.6f + 0.4f * Mathf.PerlinNoise(t * 0.7f, 3f)) * 3f + ln2() * 0.3f * Mathf.PerlinNoise(t * 1.3f, 9f), 0.7f, 1.5f);
        }

        public static AudioClip CabinLoop()
        {
            var ln = LowNoise(180f);
            // Системы жизнеобеспечения: тихий гул 50 Гц + вентилятор.
            return MakeLoop("Cabin", 4f, t => Sin(50f, t) * 0.25f + Sin(100f, t) * 0.12f + ln() * 1.5f, 0.5f, 1f);
        }

        /// <summary>Эмбиент-пад для меню: медленно дышащий минорный аккорд с хором и «звёздным» мерцанием.</summary>
        public static AudioClip MusicLoop()
        {
            float[] chord = { 110f, 164.81f, 220f, 261.63f, 329.63f, 392f };
            float[] phase = { 0f, 1.3f, 2.1f, 0.7f, 3.3f, 4.1f };
            return MakeLoop("Music", 32f, t =>
            {
                float s = 0f;
                for (int i = 0; i < chord.Length; i++)
                {
                    float lfo = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * t / (9f + i * 2.3f) + phase[i]);
                    s += (Sin(chord[i], t) + Sin(chord[i] * 1.003f, t) + 0.25f * Sin(chord[i] * 2f, t)) * lfo / (1f + i * 0.3f);
                }
                // Мерцание: высокие ноты пентатоники вспыхивают и гаснут.
                float bell = 0f;
                for (int k = 0; k < 8; k++)
                {
                    float start = k * 4f + 1.3f * (k % 3);
                    float lt = t - start;
                    if (lt > 0f && lt < 3f)
                    {
                        float hz = new[] { 880f, 987.77f, 1318.5f, 1174.66f, 1567.98f }[k % 5];
                        bell += Sin(hz, lt) * Env(lt, 0.02f, 0.9f) * 0.15f;
                    }
                }
                return s * 0.18f + bell;
            }, 0.5f, 4f);
        }
    }
}
