// Version 84 - Logging first generated group script content
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Path = System.IO.Path;
using System.Net.Sockets;
using System.Threading;
using System.Text.Json; // Для сериализации списка файлов в JSON для JS
using System.Text.RegularExpressions;


namespace FMODAudioImporter
{
    // Enum для определения типа инструмента по суффиксу файла
    public enum FmodInstrumentType { Single, Multi, Scatterer, Spatializer, Unknown }

    // Вспомогательный класс для группировки файлов
    public class FileGroup
    {
        public string BaseEventName { get; set; } // Базовое имя для Event'а (без суффикса инструмента и папок)
        public FmodInstrumentType InstrumentType { get; set; } // Тип инструмента для этой группы
        public List<string> FilePaths { get; set; } = new List<string>(); // Список путей файлов в группе
        public string RelativeFolderPath { get; set; } // Относительный путь папки внутри перетащенной папки (для создания Event Folders)
        public string OriginalDroppedFolderPath { get; set; } // Исходный путь перетащенной папки (для расчета RelativeFolderPath)
    }


    public class FmodImporterLogic : IDisposable
    {
        private TelnetConnection telnetConnection;
        private CancellationTokenSource cancellationTokenSource; // Для отмены асинхронных операций
        private string projectPath; // Путь к открытому проекту FMOD Studio (пока еще есть проблемы с получением)
        private Settings currentSettings; // Текущие настройки, включая суффиксы

        // Пути к файлам с JavaScript скриптами
        private const string GlobalSetupScriptFileName = "fmod_global_setup.js";
        private const string ImportGroupScriptTemplateFileName = "fmod_import_group.js.template";

        // Содержимое скриптов, читается один раз при инициализации/подключении
        private string globalSetupScriptContent;
        private string importGroupScriptTemplateContent;


        // Событие, которое оповещает UI об изменении статуса
        public event Action<string> StatusUpdated;

        /// <summary>
        /// Показывает, активно ли Telnet-соединение с FMOD Studio.
        /// </summary>
        public bool IsConnected
        {
            get { return telnetConnection != null && telnetConnection.connected; }
        }


        // Конструктор
        public FmodImporterLogic(Settings settings)
        {
            Logger.LogInfo("FmodImporterLogic: Initializing.");
            currentSettings = settings;
            // Читаем содержимое скриптов при создании объекта логики
            ReadScriptFiles();
        }

        // Метод для чтения содержимого JS скриптов из файлов
        private void ReadScriptFiles()
        {
            Logger.LogInfo("FmodImporterLogic: Reading JS script files.");
            try
            {
                // Определяем путь к скриптам. Можно сделать его настраиваемым,
                // или искать в директории запуска приложения.
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string globalSetupScriptPath = Path.Combine(baseDirectory, GlobalSetupScriptFileName);
                string importGroupScriptTemplatePath = Path.Combine(baseDirectory, ImportGroupScriptTemplateFileName);

                if (!File.Exists(globalSetupScriptPath))
                {
                    Logger.LogError($"Global setup script not found at: {globalSetupScriptPath}");
                    globalSetupScriptContent = null; // Скрипт не найден
                }
                else
                {
                    globalSetupScriptContent = File.ReadAllText(globalSetupScriptPath, Encoding.UTF8);
                    Logger.LogInfo($"Successfully read global setup script: {globalSetupScriptPath}");
                }

                if (!File.Exists(importGroupScriptTemplatePath))
                {
                    Logger.LogError($"Import group script template not found at: {importGroupScriptTemplatePath}");
                    importGroupScriptTemplateContent = null; // Шаблон не найден
                }
                else
                {
                    importGroupScriptTemplateContent = File.ReadAllText(importGroupScriptTemplatePath, Encoding.UTF8);
                     Logger.LogInfo($"Successfully read import group script template: {importGroupScriptTemplatePath}");
                }

                 // Проверка, прочитаны ли оба файла
                 if (globalSetupScriptContent == null || importGroupScriptTemplateContent == null)
                 {
                      UpdateStatus("Error reading JS script files. Check logs.");
                      Logger.LogError("One or more JS script files could not be read. Import will not work.");
                 }

            }
            catch (Exception ex)
            {
                Logger.LogError($"Error reading JS script files: {ex.Message}", ex);
                UpdateStatus("Error reading JS script files.");
                globalSetupScriptContent = null;
                importGroupScriptTemplateContent = null;
            }
        }


        // Асинхронное подключение к FMOD Studio через Telnet и инициализация
        public async Task ConnectAndInitializeAsync(string ip, int port)
        {
            // Отменяем предыдущие операции, если они были
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            // Очищаем предыдущее соединение, если оно было
            if (telnetConnection != null)
            {
                UpdateStatus("Connection disposed");
                telnetConnection.Dispose();
                telnetConnection = null;
                projectPath = null; // Сбрасываем путь проекта при разрыве соединения
                Logger.LogInfo("FmodImporterLogic: Old Telnet connection disposed.");
            }


            UpdateStatus($"Connecting to {ip}:{port}...");
            Logger.LogInfo($"FmodImporterLogic: Attempting to connect to {ip}:{port}");

            telnetConnection = new TelnetConnection(ip, port);

            try
            {
                bool isConnected = await telnetConnection.ConnectAsync(ip, port);

                if (isConnected)
                {
                    Logger.LogInfo("FmodImporterLogic: Successfully connected to FMOD Studio.");

                    // ***********************************************************
                    // ПОЛУЧЕНИЕ ПУТИ К ПРОЕКТУ - ЗДЕСЬ ЕЩЕ ОСТАЕТСЯ ПРОБЛЕМА С ПАРСИНГОМ!
                    // Мы используем FmodTelnetHelper, но нужно убедиться, что он корректно парсит ответ.
                    // projectPath используется для статуса и, возможно, для определения относительных путей
                    // при импорте ассетов, если FMOD Telnet этого требует.
                    // Пока что, команды импорта ассетов используют абсолютные пути ОС.
                    this.projectPath = await FmodTelnetHelper.GetProjectPathAsync(telnetConnection);

                    if (!string.IsNullOrEmpty(this.projectPath))
                    {
                         Logger.LogInfo($"Received project path: {this.projectPath}");
                         try
                         {
                             string fileNameFromPath = Path.GetFileNameWithoutExtension(this.projectPath);
                             UpdateStatus($"Connected (Project: {fileNameFromPath})");
                         }
                         catch
                         {
                              UpdateStatus($"Connected (Project path: {this.projectPath})");
                         }
                    }
                    else
                    {
                         Logger.LogWarning("Could not retrieve project path.");
                         UpdateStatus("Connected (No project info)"); // Статус будет "Connected (No project info)" если путь не получен
                    }
                    // ***********************************************************

                    // *** ОТПРАВЛЯЕМ ГЛОБАЛЬНЫЙ НАСТРОЕЧНЫЙ СКРИПТ ПОСЛЕ ПОДКЛЮЧЕНИЯ ***
                    if (!string.IsNullOrEmpty(globalSetupScriptContent))
                    {
                        Logger.LogInfo("FmodImporterLogic: Sending global setup script.");
                         // Отправляем содержимое файла как один блок команды
                        await SendCommandsToFmod(new List<string> { globalSetupScriptContent });
                         Logger.LogInfo("FmodImporterLogic: Global setup script sent.");
                    }
                    else
                    {
                        Logger.LogError("FmodImporterLogic: Global setup script content is missing. Initialization incomplete.");
                        UpdateStatus("Error: Global script missing.");
                        // Возможно, стоит разорвать соединение или отключить функционал импорта, если скрипт не загружен
                    }


                }
                else
                {
                    Logger.LogError("FmodImporterLogic: Failed to connect to FMOD Studio within timeout.");
                    UpdateStatus("Connection failed (Timeout)");
                    if (telnetConnection != null) { telnetConnection.Dispose(); telnetConnection = null; }
                }
            }
            catch (SocketException sockEx)
            {
                Logger.LogError($"FmodImporterLogic: Socket error during connection: {sockEx.Message}", sockEx);
                UpdateStatus($"Connection failed (Socket error: {sockEx.SocketErrorCode})");
                 if (telnetConnection != null) { telnetConnection.Dispose(); telnetConnection = null; }
            }
            catch (TimeoutException timeoutEx)
            {
                 Logger.LogError($"FmodImporterLogic: Timeout during connection or initialization: {timeoutEx.Message}", timeoutEx);
                 UpdateStatus("Connection failed (Init Timeout)");
                 if (telnetConnection != null) { telnetConnection.Dispose(); telnetConnection = null; }
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("FmodImporterLogic: Connection attempt was cancelled.");
                UpdateStatus("Connection cancelled");
                 if (telnetConnection != null) { telnetConnection.Dispose(); telnetConnection = null; }
            }
            catch (Exception ex)
            {
                Logger.LogError($"FmodImporterLogic: An unexpected error occurred during connection or initialization: {ex.Message}", ex);
                UpdateStatus($"Connection failed (Error: {ex.GetType().Name})");
                 if (telnetConnection != null) { telnetConnection.Dispose(); telnetConnection = null; }
            }
             finally
             {
                 cancellationTokenSource?.Dispose();
                 cancellationTokenSource = null;
             }
        }

        // Метод для импорта папки с аудиофайлами
        public async Task ImportFolderAsync(string folderPath)
        {
            if (!IsConnected)
            {
                Logger.LogWarning("FmodImporterLogic: Import failed - Not connected to FMOD Studio.");
                UpdateStatus("Error: Not connected");
                return;
            }

            // *** Проверка, что скрипты успешно прочитаны ***
            if (string.IsNullOrEmpty(globalSetupScriptContent) || string.IsNullOrEmpty(importGroupScriptTemplateContent))
            {
                 Logger.LogError("FmodImporterLogic: Import failed - JS script files were not loaded.");
                 UpdateStatus("Error: JS scripts not loaded.");
                 return;
            }


            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                Logger.LogWarning($"FmodImporterLogic: Import failed - Invalid folder path: {folderPath}");
                UpdateStatus("Error: Invalid folder");
                return;
            }

            // !!! ВАЖНО: Импорт может работать некорректно, если projectPath не получен.
            // Команды создания ассетов и Event'ов могут требовать контекста проекта.
            if (string.IsNullOrEmpty(projectPath))
            {
                 Logger.LogWarning("FmodImporterLogic: Import started but FMOD project path is unknown. Folder creation may be incorrect and commands may fail.");
                 UpdateStatus("Warning: Unknown project path. Import may fail.");
                 // Продолжаем, но с предупреждением.
            }


             // Получаем абсолютный путь к проекту для расчета относительных путей - возможно не нужно для Telnet команд создания Event/Asset
             // Telnet команды, похоже, используют абсолютные пути ОС к файлам для импорта ассетов
             // и пути в иерархии FMOD для Event/Folder.
             // Относительный путь файла к перетащенной папке важен для создания иерархии Event Folder.


            UpdateStatus($"Scanning folder: {Path.GetFileName(folderPath)}...");
            Logger.LogInfo($"FmodImporterLogic: Scanning folder for audio files: {folderPath}");

            try
            {
                var audioFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                                          .Where(file => file.EndsWithIgnoreCase(".wav") || // Используем вспомогательный метод
                                                         file.EndsWithIgnoreCase(".aiff") ||
                                                         file.EndsWithIgnoreCase(".aif") ||
                                                         file.EndsWithIgnoreCase(".ogg") ||
                                                         file.EndsWithIgnoreCase(".mp3"))
                                          .ToList();

                Logger.LogInfo($"FmodImporterLogic: Found {audioFiles.Count} supported audio files.");
                UpdateStatus($"Found {audioFiles.Count} audio files.");


                if (!audioFiles.Any())
                {
                    Logger.LogWarning("FmodImporterLogic: No supported audio files found in the folder.");
                    UpdateStatus("No audio files found.");
                    return;
                }

                // Группируем файлы по базовому имени, типу инструмента и относительной папке
                // Используем currentSettings для получения суффиксов
                var fileGroups = GroupFiles(audioFiles, folderPath); // projectDirectory здесь не нужен

                Logger.LogInfo($"FmodImporterLogic: Grouped files into {fileGroups.Count} groups.");
                UpdateStatus($"Processing {fileGroups.Count} file groups.");

                // Генерируем и отправляем Telnet команды для создания Event'ов и инструментов
                await GenerateAndSendFmodCommands(fileGroups);

                 UpdateStatus("Import process finished.");
                 Logger.LogInfo("FmodImporterLogic: Import process completed.");

            }
            catch (DirectoryNotFoundException dirEx)
            {
                Logger.LogError($"FmodImporterLogic: Folder not found: {folderPath}", dirEx);
                UpdateStatus("Error: Folder not found");
            }
            catch (UnauthorizedAccessException unauthEx)
            {
                Logger.LogError($"FmodImporterLogic: Access denied to folder: {folderPath}", unauthEx);
                UpdateStatus("Error: Access denied");
            }
             catch (Exception ex)
            {
                 Logger.LogError($"FmodImporterLogic: An error occurred during import: {ex.Message}", ex);
                 UpdateStatus($"Error during import: {ex.GetType().Name}");
            }
        }

        // Группирует файлы на основе суффиксов и структуры папок
        private List<FileGroup> GroupFiles(List<string> filePaths, string originalDroppedFolderPath)
        {
            Logger.LogInfo($"FmodImporterLogic.GroupFiles: Starting grouping for {filePaths.Count} files. Base folder: '{originalDroppedFolderPath}'.");
            var groups = new Dictionary<string, FileGroup>();

            // Убедимся, что пути к папкам заканчиваются на разделитель для корректного расчета относительного пути
            if (!originalDroppedFolderPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                originalDroppedFolderPath += Path.DirectorySeparatorChar;
            }

            foreach (var filePath in filePaths)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                FmodInstrumentType type = FmodInstrumentType.Single; // Предполагаем Single по умолчанию
                string baseName = fileName;

                Logger.LogInfo($"FmodImporterLogic.GroupFiles: Processing file: '{filePath}'");
                Logger.LogInfo($"FmodImporterLogic.GroupFiles: Initial fileName: '{fileName}'");


                // Определяем тип инструмента и базовое имя по суффиксам из currentSettings
                // Важно: определить ОСНОВНОЙ тип инструмента (Single, Multi, Scatterer).
                // Spatializer пока считаем отдельным маркером для эффекта.

                 string tempBaseName = fileName;
                 FmodInstrumentType determinedType = FmodInstrumentType.Single; // Начинаем с Single как базового

                 // *** Логирование определения типа инструмента ***
                 bool multiSuffixFound = !string.IsNullOrEmpty(currentSettings.MultiSuffix) && tempBaseName.EndsWithIgnoreCase(currentSettings.MultiSuffix);
                 bool scattererSuffixFound = !string.IsNullOrEmpty(currentSettings.ScattererSuffix) && tempBaseName.EndsWithIgnoreCase(currentSettings.ScattererSuffix);
                 bool spatializerSuffixFound = !string.IsNullOrEmpty(currentSettings.SpatializerSuffix) && tempBaseName.EndsWithIgnoreCase(currentSettings.SpatializerSuffix); // Проверяем Spatializer суффикс, но он не меняет determinedType здесь

                 Logger.LogInfo($"FmodImporterLogic.GroupFiles: Suffix checks: Multi={multiSuffixFound}, Scatterer={scattererSuffixFound}, Spatializer={spatializerSuffixFound}");


                 if (multiSuffixFound)
                 {
                     determinedType = FmodInstrumentType.Multi;
                     tempBaseName = tempBaseName.Substring(0, tempBaseName.Length - currentSettings.MultiSuffix.Length);
                     Logger.LogInfo($"FmodImporterLogic.GroupFiles: Determined type: Multi. tempBaseName after removing suffix: '{tempBaseName}'");
                 }
                 // Если это Multi, оно не может быть Scatterer (предполагая взаимоисключающие основные типы)
                 else if (scattererSuffixFound) // Используем вспомогательный метод
                 {
                     determinedType = FmodInstrumentType.Scatterer;
                     tempBaseName = tempBaseName.Substring(0, tempBaseName.Length - currentSettings.ScattererSuffix.Length);
                     Logger.LogInfo($"FmodImporterLogic.GroupFiles: Determined type: Scatterer. tempBaseName after removing suffix: '{tempBaseName}'");
                 }
                 else
                 {
                      // Если нет суффиксов Multi или Scatterer, это Single
                      determinedType = FmodInstrumentType.Single;
                      Logger.LogInfo($"FmodImporterLogic.GroupFiles: Determined type: Single.");
                 }
                 // Spatializer суффикс не меняет determinedType, он влияет на эффект.

                 baseName = tempBaseName; // Базовое имя после удаления суффикса основного типа инструмента
                 type = determinedType; // Устанавливаем тип группы

                 Logger.LogInfo($"FmodImporterLogic.GroupFiles: Final baseName: '{baseName}', Final type: {type}");


                // Рассчитываем относительный путь папки файла относительно перетащенной папки
                // Используем GetRelativeFolderPath из Extensions
                string fileDirectory = Path.GetDirectoryName(filePath);
                string fmodRelativeFolderPath = Extensions.GetRelativeFolderPath(originalDroppedFolderPath, fileDirectory);

                Logger.LogInfo($"FmodImporterLogic.GroupFiles: fileDirectory: '{fileDirectory}'");
                Logger.LogInfo($"FmodImporterLogic.GroupFiles: fmodRelativeFolderPath calculated: '{fmodRelativeFolderPath}'");


                // Определяем уникальный ключ для группировки.
                // Все файлы, которые должны попасть в один Event под одной иерархией папок с одним основным типом инструмента.
                // Это может быть несколько файлов для Multi/Scatterer, но обычно один для Single.
                // Ключ: Относительный путь папки + Базовое имя Event'а + Тип инструмента

                // Используем Path.Combine для создания ключа, чтобы корректно обработать пустой RelativeFolderPath
                // Заменяем разделители на "/" для консистентности в ключе, так как FMOD пути используют "/"
                string keyPathPart = string.IsNullOrEmpty(fmodRelativeFolderPath) ? string.Empty : fmodRelativeFolderPath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

                string groupKey = $"{keyPathPart}_{baseName}_{type}";

                Logger.LogInfo($"FmodImporterLogic.GroupFiles: Generated groupKey: '{groupKey}'");


                if (!groups.TryGetValue(groupKey, out FileGroup group))
                {
                    Logger.LogInfo($"FmodImporterLogic.GroupFiles: Creating new group for key: '{groupKey}'");
                    group = new FileGroup
                    {
                        BaseEventName = baseName, // Базовое имя файла
                        InstrumentType = type,
                        RelativeFolderPath = fmodRelativeFolderPath, // Относительный путь папки для Event'а Folder
                        OriginalDroppedFolderPath = originalDroppedFolderPath, // Сохраняем для справки
                    };
                    groups[groupKey] = group;
                }
                else
                {
                     Logger.LogInfo($"FmodImporterLogic.GroupFiles: Adding file to existing group for key: '{groupKey}'.");
                }

                group.FilePaths.Add(filePath);
                 Logger.LogInfo($"FmodImporterLogic.GroupFiles: Added file '{filePath}' to group '{groupKey}'. Current file count in group: {group.FilePaths.Count}");

            }

            Logger.LogInfo($"FmodImporterLogic.GroupFiles: Finished grouping. Created {groups.Count} groups.");
            return groups.Values.ToList();
        }


        // Генерирует и отправляет Telnet команды для создания Event'ов и инструментов
        private async Task GenerateAndSendFmodCommands(List<FileGroup> fileGroups)
        {
            // Список блоков команд для отправки
            List<string> commandBlocks = new List<string>();

            // --- ЭТАП 1: Импорт всех аудиофайлов как ассетов ---
            // Команды импорта ассетов могут оставаться отдельными, так как они не зависят от переменных Event'ов
            UpdateStatus("Importing audio files as assets...");
            Logger.LogInfo("FmodImporterLogic: Generating asset import commands.");

            foreach (var group in fileGroups)
            {
                foreach (var filePath in group.FilePaths)
                {
                    string absoluteFilePath = Path.GetFullPath(filePath).Replace(@"\", @"/");
                    commandBlocks.Add($"studio.project.importAudioFile('{EscapeJavaScriptString(absoluteFilePath)}');"); // Используем EscapeJavaScriptString
                }
            }

            // --- ЭТАП 2: Создание Event Folders, Events, Tracks, Instruments ---
            // Сообщения о начале этапа уже есть после отправки глобального скрипта
            UpdateStatus("Creating FMOD Event Folders, Events and Instruments...");
            Logger.LogInfo("FmodImporterLogic: Generating Event Folder/Event/Instrument command blocks.");


            // *** Добавляем глобальный блок настройки первым в списке команд ***
            // Сначала генерируем его
            StringBuilder globalJsSetup = new StringBuilder();
            globalJsSetup.AppendLine("var folderCache = {};"); // Global cache for created/found folders
            // Вставляем код функции createEventFolder из внешнего файла
            if (!string.IsNullOrEmpty(globalSetupScriptContent))
            {
                globalJsSetup.Append(globalSetupScriptContent); // Append content of the global setup file
            }
            else
            {
                 Logger.LogError("Global setup script content is missing. Cannot add to command blocks.");
                 // Добавим пустой блок или комментарий, чтобы не сломать индексацию, если это критично
                 // или просто пропустим добавление этого блока, если он уже был отправлен при подключении
                 // В текущей логике, глобальный скрипт отправляется сразу при подключении,
                 // так что его не нужно добавлять в этот список еще раз.
            }

             // Глобальный скрипт уже отправлен при подключении.
             // Удаляем его добавление здесь, чтобы не дублировать.
             // commandBlocks.Add(globalJsSetup.ToString()); // <-- Эту строку удаляем или комментируем

            // *** Генерируем блоки команд для каждой группы на основе шаблона ***
            if (string.IsNullOrEmpty(importGroupScriptTemplateContent))
            {
                 Logger.LogError("Import group script template content is missing. Cannot generate commands for groups.");
                 UpdateStatus("Error: Import script template missing.");
                 return; // Не можем продолжить без шаблона
            }

            foreach (var group in fileGroups)
            {
                // Читаем шаблон для текущей группы
                string groupScript = importGroupScriptTemplateContent;

                // Подготавливаем данные для замены в шаблоне
                string eventName = group.BaseEventName;
                string fmodRelativeFolderPath = group.RelativeFolderPath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/').Trim('/');

                // Экранируем значения для вставки в JavaScript строки
                string jsEventName = EscapeJavaScriptString(eventName);
                string jsRelativeFolderPath = EscapeJavaScriptString(fmodRelativeFolderPath);

                // Сериализуем список файлов в JSON строку
                var options = new JsonSerializerOptions { WriteIndented = false };
                string filePathsJson = JsonSerializer.Serialize(group.FilePaths.Select(p => Path.GetFullPath(p).Replace(@"\", @"/")).ToList(), options); // Экранируем и нормализуем пути для JSON
                string jsFilePathsJson = EscapeJavaScriptString(filePathsJson); // Дополнительно экранируем JSON строку для вставки в JS

                // Определяем тип инструмента для использования в шаблоне (возможно, понадобится в будущем для ветвления)
                string jsInstrumentType = group.InstrumentType.ToString(); // Передаем имя enum как строку


                // Выполняем замену заполнителей в шаблоне
                groupScript = groupScript.Replace("{EVENT_NAME}", jsEventName);
                groupScript = groupScript.Replace("{RELATIVE_FOLDER_PATH}", jsRelativeFolderPath);
                groupScript = groupScript.Replace("{FILE_PATHS_JSON}", jsFilePathsJson); // Передаем список файлов как JSON строку
                groupScript = groupScript.Replace("{INSTRUMENT_TYPE}", jsInstrumentType); // Передаем тип инструмента

                 // Добавляем команду сохранения проекта в конец каждого скрипта группы
                 groupScript += $"\nstudio.project.save();\nstudio.system.print('[IMPORTER_LOG] Project saved after processing group \"' + '{jsRelativeFolderPath}' + '/' + '{jsEventName}' + '\".');";


                // Добавляем сгенерированный скрипт для этой группы в список команд

                // *** ДОБАВЛЕНО: Логируем содержимое первого сгенерированного скрипта для отладки ***
                // Логируем первый скрипт группы. Индекс 0 после команд импорта ассетов.
                if (commandBlocks.Count == fileGroups.Count) // commandBlocks.Count будет равен количеству групп после добавления всех asset commands
                {
                    // Индекс первого скрипта группы после asset commands.
                    // Если asset commands добавляются ПЕРЕД глобальным скриптом,
                    // то индекс первого скрипта группы будет = количеству asset commands + 1 (глобальный скрипт).
                    // Если глобальный скрипт отправляется отдельно при подключении (как сейчас),
                    // то индекс первого скрипта группы будет = количеству asset commands.
                    // В текущей реализации asset commands добавляются ПЕРЕД циклом групп.
                    // Так что первый скрипт группы добавляется, когда commandBlocks уже содержит все asset commands.
                    // Количество asset commands = fileGroups.Count (если каждый файл - отдельный ассет)
                    // Но у нас 11 файлов, а групп тоже 11. Каждый файл - отдельный ассет.
                    // Значит, asset commands = 11. Первый скрипт группы добавляется на 12-й позиции (индекс 11).
                    // Исправляем условие для логирования первого скрипта группы.
                    // У нас 11 asset import commands. Первый скрипт группы будет 12-м элементом списка (индекс 11).
                    // commandBlocks.Count будет 11 ДО добавления первого скрипта группы.

                    // Правильное условие: commandBlocks.Count равен количеству asset import команд до добавления первого скрипта группы
                     int numberOfAssetCommands = 0;
                     foreach(var grp in fileGroups) numberOfAssetCommands += grp.FilePaths.Count;

                    if (commandBlocks.Count == numberOfAssetCommands) // Если текущее количество блоков равно количеству asset commands
                    {
                         Logger.LogInfo($"FmodImporterLogic: Content of the FIRST generated group script for '{fmodRelativeFolderPath}/{eventName}':\n{groupScript}");
                    }
                }
                // *** КОНЕЦ ДОБАВЛЕННОГО ЛОГИРОВАНИЯ ***


                commandBlocks.Add(groupScript);

                 Logger.LogInfo($"FmodImporterLogic: Generated script for group '{fmodRelativeFolderPath}/{eventName}' ({group.InstrumentType}). Added as block {commandBlocks.Count}.");
            } // Конец цикла по группам


            // Теперь отправляем сгенерированные блоки команд (по одному блоку на группу, плюс ассеты)
            await SendCommandsToFmod(commandBlocks);
        }

        // Вспомогательный метод для экранирования строк для JavaScript литералов
        // В контексте вставки в JS строку.
        private string EscapeJavaScriptString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";
            // Экранируем обратные слэши, одинарные кавычки и символы новой строки
            return input.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
        }


        // Метод для отправки команд в FMOD Studio через TelnetConnection
        // Этот метод инкапсулирует логику отправки списка строк команд.
        private async Task SendCommandsToFmod(List<string> commands)
        {
             if (!IsConnected)
             {
                 Logger.LogWarning("Cannot send commands to Telnet: Not connected.");
                 UpdateStatus("Error: Cannot send commands (Not connected)");
                 return;
             }

            // Обновляем статус перед отправкой
            UpdateStatus($"Sending {commands.Count} command blocks to FMOD Studio...");
            Logger.LogInfo($"FmodImporterLogic: Sending {commands.Count} command blocks to FMOD Studio.");

            try
            {
                // Используем расширение WriteAsync для отправки списка строк
                // WriteAsync уже добавляет символы новой строки и задержки между командами в списке.
                await telnetConnection.WriteAsync(commands);

                Logger.LogInfo("FmodImporterLogic: Command blocks sent successfully.");
                // Статус "Import process finished" устанавливается после вызова этого метода в ImportFolderAsync
            }
            catch (Exception ex)
            {
                Logger.LogError($"FmodImporterLogic: An error occurred while sending commands: {ex.Message}", ex);
                UpdateStatus($"Error sending commands: {ex.GetType().Name}");
            }
        }


        // Метод для обновления статуса в UI
        private void UpdateStatus(string status)
        {
            // Invoke the StatusUpdated event on the UI thread
            // (Предполагается, что StatusUpdated подписан на Dispatcher.Invoke в MainWindow)
            StatusUpdated?.Invoke(status);

            // Log the status
             Logger.LogInfo($"Status: {status}"); // Логируем статус
        }

        // Реализация IDisposable для освобождения ресурсов
        public void Dispose()
        {
            Logger.LogInfo("FmodImporterLogic: Disposing.");
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;

            if (telnetConnection != null)
            {
                // Обновляем статус перед диспозом, если соединение было активно
                if (telnetConnection.connected) UpdateStatus("Connection disposed");
                telnetConnection.Dispose();
                telnetConnection = null;
            }

            Logger.LogInfo("FmodImporterLogic: Disposed.\n");
        }
    } // End of FmodImporterLogic class
} // End of FMODAudioImporter namespace