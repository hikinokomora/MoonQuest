using System;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace _4DOF
{
    /// <summary>
    /// Отправляет телеметрию в приложение X-DOF через общую память Windows.
    ///
    /// Каждые <see cref="SendIntervalMs"/> миллисекунд фоновый поток записывает
    /// шесть чисел double из <see cref="Data"/> в MemoryMappedFile с именем
    /// <see cref="MapName"/>. X-DOF и эмулятор платформы читают этот же блок.
    ///
    /// Формат совпадает с пакетом 2DOF, поэтому в сцене должен работать только
    /// ОДИН отправитель: два отправителя будут перетирать данные друг друга,
    /// и платформа начнёт дёргаться между ними.
    /// </summary>
    public sealed class MotionTelemetrySender : IDisposable
    {
        /// <summary>Имя общей памяти. Должно совпадать с ключом в X-DOF.</summary>
        public const string MapName = "2DOFMemoryDataGrabber";

        /// <summary>Период отправки, мс.</summary>
        public const int SendIntervalMs = 20;

        /// <summary>Данные, которые уходят на платформу.</summary>
        public MotionTelemetryData Data { get; } = new MotionTelemetryData();

        /// <summary>Идёт ли отправка.</summary>
        public bool IsSending => _thread != null;

        private Thread _thread;
        private volatile bool _running;

        /// <summary>Запускает фоновую отправку. Повторный вызов ничего не делает.</summary>
        public void Start()
        {
            if (_thread != null)
            {
                return;
            }

            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "4DOF telemetry sender" };
            _thread.Start();
        }

        /// <summary>
        /// Останавливает отправку. Перед остановкой в память записываются нули,
        /// чтобы платформа плавно ушла в нейтраль, а не застыла в наклоне.
        /// </summary>
        public void Stop()
        {
            if (_thread == null)
            {
                return;
            }

            _running = false;
            _thread.Join(SendIntervalMs * 10);
            _thread = null;
        }

        public void Dispose() => Stop();

        private void Loop()
        {
            var buffer = new double[MotionTelemetryData.ChannelCount];
            var size = sizeof(double) * MotionTelemetryData.ChannelCount;

            using var file = MemoryMappedFile.CreateOrOpen(MapName, size);
            using var accessor = file.CreateViewAccessor(0, size);

            while (_running)
            {
                Data.CopyTo(buffer);
                accessor.WriteArray(0, buffer, 0, buffer.Length);
                Thread.Sleep(SendIntervalMs);
            }

            Array.Clear(buffer, 0, buffer.Length);
            accessor.WriteArray(0, buffer, 0, buffer.Length);
            accessor.Flush();
        }
    }
}
