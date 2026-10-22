namespace _4DOF
{
    /// <summary>
    /// Шесть степеней свободы, которые приложение X-DOF получает от Unity.
    ///
    /// Порядок и единицы измерения фиксированы программой X-DOF, менять их
    /// нельзя: X-DOF читает из общей памяти ровно шесть чисел double
    /// в порядке Pitch, Roll, Yaw, Surge, Sway, Heave.
    ///
    /// Какие из величин реально двигают капсулу, зависит от типа платформы,
    /// выбранного в X-DOF: у 2DOF нет подъёма и поворота капсулы, у 4DOF есть.
    /// Заполнять стоит все шесть — тогда один и тот же проект работает на
    /// обеих платформах.
    ///
    /// Класс потокобезопасен: значения пишутся из FixedUpdate, а читаются
    /// потоком отправки <see cref="MotionTelemetrySender"/>.
    /// </summary>
    public sealed class MotionTelemetryData
    {
        /// <summary>Количество каналов телеметрии.</summary>
        public const int ChannelCount = 6;

        private readonly object _lock = new object();
        private readonly double[] _values = new double[ChannelCount];

        /// <summary>Тангаж — наклон кузова вперёд-назад, градусы. Нос вниз — плюс.</summary>
        public float Pitch { get => Get(0); set => Set(0, value); }

        /// <summary>Крен — наклон кузова влево-вправо, градусы.</summary>
        public float Roll { get => Get(1); set => Set(1, value); }

        /// <summary>Рыскание — СКОРОСТЬ поворота вокруг вертикальной оси, градусы в секунду.</summary>
        public float Yaw { get => Get(2); set => Set(2, value); }

        /// <summary>Продольное ускорение, м/с². Разгон — плюс, торможение — минус.</summary>
        public float Surge { get => Get(3); set => Set(3, value); }

        /// <summary>Боковое ускорение, м/с². Вправо — плюс.</summary>
        public float Sway { get => Get(4); set => Set(4, value); }

        /// <summary>Вертикальное ускорение, м/с². Вверх — плюс.</summary>
        public float Heave { get => Get(5); set => Set(5, value); }

        /// <summary>Записывает все шесть каналов одной операцией.</summary>
        public void Set(float pitch, float roll, float yaw, float surge, float sway, float heave)
        {
            lock (_lock)
            {
                _values[0] = pitch;
                _values[1] = roll;
                _values[2] = yaw;
                _values[3] = surge;
                _values[4] = sway;
                _values[5] = heave;
            }
        }

        /// <summary>Обнуляет все каналы — платформа уходит в нейтраль.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                System.Array.Clear(_values, 0, ChannelCount);
            }
        }

        /// <summary>Копирует каналы в буфер в порядке, который ожидает X-DOF.</summary>
        public void CopyTo(double[] buffer)
        {
            lock (_lock)
            {
                System.Array.Copy(_values, buffer, ChannelCount);
            }
        }

        public override string ToString()
        {
            lock (_lock)
            {
                return $"Pitch {_values[0]:F1}°, Roll {_values[1]:F1}°, Yaw {_values[2]:F1}°/с, " +
                       $"Surge {_values[3]:F2}, Sway {_values[4]:F2}, Heave {_values[5]:F2} м/с²";
            }
        }

        private float Get(int index)
        {
            lock (_lock)
            {
                return (float)_values[index];
            }
        }

        private void Set(int index, float value)
        {
            lock (_lock)
            {
                _values[index] = value;
            }
        }
    }
}
