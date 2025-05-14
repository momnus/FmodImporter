using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FMODAudioImporter
{
    /// <summary>
    /// Класс для хранения настроек приложения, поддерживающий уведомление об изменении свойств.
    /// </summary>
    public class Settings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        // Вспомогательный метод для вызова события PropertyChanged
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // Свойства для хранения настроек
        private string _ip = "127.0.0.1";
        public string IP
        {
            get { return _ip; }
            set
            {
                if (_ip != value)
                {
                    _ip = value;
                    OnPropertyChanged();
                }
            }
        }

        // !!! ИСПРАВЛЕНИЕ: Устанавливаем порт по умолчанию на 3663
        private int _port = 3663;
        public int Port
        {
            get { return _port; }
            set
            {
                if (_port != value)
                {
                    _port = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _multiSuffix = "_m"; // Пример суффикса из XAML
        public string MultiSuffix
        {
            get { return _multiSuffix; }
            set
            {
                if (_multiSuffix != value)
                {
                    _multiSuffix = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _scattererSuffix = "_c"; // Пример суффикса из XAML
        public string ScattererSuffix
        {
            get { return _scattererSuffix; }
            set
            {
                if (_scattererSuffix != value)
                {
                    _scattererSuffix = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _spatializerSuffix = "_s"; // Пример суффикса из XAML
        public string SpatializerSuffix
        {
            get { return _spatializerSuffix; }
            set
            {
                if (_spatializerSuffix != value)
                {
                    _spatializerSuffix = value;
                    OnPropertyChanged();
                }
            }
        }

        // Конструктор с значениями по умолчанию
        public Settings()
        {
            // Значения полей инициализированы выше, можно оставить конструктор пустым или добавить логику загрузки/сохранения
        }
    }
}