// Version 70 - Added Window_Loaded handler to programmatically select Settings tab
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using System.IO;
using Path = System.IO.Path;
using System.Runtime.CompilerServices;
using System.ComponentModel; // Add for INotifyPropertyChanged

namespace FMODAudioImporter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged // Keep INotifyPropertyChanged if needed for other window properties
    {
        private FmodImporterLogic importerLogic; // Instance of the logic class

        // Добавляем свойство AppSettings
        public Settings AppSettings { get; set; }

        // Реализация INotifyPropertyChanged интерфейса (если у окна есть другие свойства, к которым идет привязка)
        // Это свойство используется для привязки StatusTextBlock
        private string _statusText;
        public string StatusText
        {
            get { return _statusText; }
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }


        // Реализация INotifyPropertyChanged интерфейса для самой MainWindow
        public event PropertyChangedEventHandler PropertyChanged;

        // Вспомогательный метод для вызова события PropertyChanged
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // Конструктор
        public MainWindow()
        {
            Logger.LogInfo("MainWindow: Initializing."); // Logging
            InitializeComponent();

            // Инициализируем AppSettings и устанавливаем DataContext для привязки
            AppSettings = new Settings();
            this.DataContext = this; // Устанавливаем DataContext окна на само окно (для привязки AppSettings и StatusText)

            // Инициализируем FmodImporterLogic с объектом настроек
            importerLogic = new FmodImporterLogic(AppSettings);

            // Подписываемся на событие обновления статуса от importerLogic
            importerLogic.StatusUpdated += ImporterLogic_StatusUpdated;

            // Устанавливаем начальный статус
            StatusText = "Ready";

            Logger.LogInfo("MainWindow: Initialization complete."); // Logging
        }

        // !!! НОВЫЙ МЕТОД: Обработчик события Loaded окна
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Выбираем вкладку "Settings" (индекс 1) после загрузки окна.
            // Это гарантирует, что содержимое вкладки будет загружено,
            // и привязки должны будут обновиться.
            Logger.LogInfo("MainWindow.Window_Loaded: Window loaded, selecting Settings tab.");
            tabControlMain.SelectedIndex = 1;
        }


        // Обработчик клика по кнопке "Connect"
        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            Logger.LogInfo($"MainWindow.Connect_Click: Attempting to connect to {AppSettings.IP}:{AppSettings.Port}");
            StatusText = "Connecting...";
            ConnectButton.IsEnabled = false;

            try
            {
                await importerLogic.ConnectAndInitializeAsync(AppSettings.IP, AppSettings.Port);
            }
            catch(Exception connectEx)
            {
                Logger.LogError($"MainWindow.Connect_Click: Error during connection attempt: {connectEx.Message}", connectEx);
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }

        // Обработчик события перетаскивания папки на окно
         private async void Window_Drop(object sender, DragEventArgs e)
         {
             Logger.LogInfo("MainWindow.Window_Drop: File drop detected.");

             if (importerLogic == null || !importerLogic.IsConnected)
             {
                  Logger.LogWarning("MainWindow.Window_Drop: Not connected to FMOD Studio. Cannot import.");
                  StatusText = "Error: Not connected to FMOD Studio";
                  return;
             }

             if (e.Data.GetDataPresent(DataFormats.FileDrop))
             {
                 string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                 if (files != null && files.Length > 0)
                 {
                     string droppedPath = files[0];

                     if (Directory.Exists(droppedPath))
                     {
                         Logger.LogInfo($"MainWindow.Window_Drop: Dropped path is a directory: {droppedPath}");
                         try
                         {
                             await importerLogic.ImportFolderAsync(droppedPath);
                         }
                         catch(Exception importEx)
                         {
                              Logger.LogError($"MainWindow.Window_Drop: Error during import process: {importEx.Message}", importEx);
                         }
                     }
                     else
                     {
                          Logger.LogWarning($"MainWindow.Window_Drop: Dropped path is not a directory: {droppedPath}");
                          StatusText = "Error: Please drop a folder.";
                     }
                 }
             }
              else
             {
                 Logger.LogWarning("MainWindow.Window_Drop: Dropped data is not a file drop.");
                 StatusText = "Error: Invalid drop type.";
             }
         }

        // Обработчик события обновления статуса от ImporterLogic
        private void ImporterLogic_StatusUpdated(string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText = status;
            });
        }

        // Обработчик клика по Hyperlink
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        
        // Обработчик события закрытия окна для освобождения ресурсов
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Logger.LogInfo("MainWindow: Window closing. Disposing importerLogic."); // Логирование
            importerLogic?.Dispose(); // Вызываем Dispose() у importerLogic, если он не null
             Logger.LogInfo("MainWindow: importerLogic disposed."); // Логирование после освобождения
        }

        // Удаляем старые обработчики TextChanged для полей настроек, так как привязка Handle'ит это
        // private void textBoxIp_TextChanged(object sender, TextChangedEventArgs e) { ... }
        // private void textBoxPort_TextChanged(object sender, TextChangedEventArgs e) { ... }
        // private void textBoxMulti_TextChanged(object sender, TextChangedEventArgs e) { ... }
        // private void textBoxScatterer_TextChanged(object sender, TextChangedEventArgs e) { ... }
        // private void textBoxSpatializer_TextChanged(object sender, TextChangedEventArgs e) { ... }

    }
}