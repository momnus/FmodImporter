using System;
using System.IO;
using System.Text;
using System.Threading; // Для синхронизации доступа к файлу

namespace FMODAudioImporter
{
    public static class Logger
    {
        private static string logFilePath;
        private static readonly object fileLock = new object(); // Объект для синхронизации доступа к файлу

        static Logger()
        {
            // Устанавливаем путь к лог-файлу в директории выполнения приложения
            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "application.log");

            // Добавляем запись о старте приложения при создании логгера
            string startupLogEntry = FormatLogEntry("INFO", "Application started.");
            WriteLogEntryToFile(startupLogEntry);
        }

        // Метод для логирования сообщений об ошибках
        public static void LogError(string message, Exception ex = null)
        {
            string logEntry = FormatLogEntry("ERROR", message, ex);
            WriteLogEntryToFile(logEntry);
        }

        // Метод для логирования информационных сообщений
        public static void LogInfo(string message)
        {
            string logEntry = FormatLogEntry("INFO", message);
            WriteLogEntryToFile(logEntry);
        }

        // Метод для логирования предупреждений (добавлен)
        public static void LogWarning(string message)
        {
            string logEntry = FormatLogEntry("WARNING", message);
            WriteLogEntryToFile(logEntry);
        }


        // Форматирование записи лога
        private static string FormatLogEntry(string level, string message, Exception ex = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}");

            if (ex != null)
            {
                sb.AppendLine();
                sb.AppendLine($"Exception Type: {ex.GetType().FullName}");
                sb.AppendLine($"Exception Message: {ex.Message}");
                sb.AppendLine($"Stack Trace:");
                sb.AppendLine(ex.StackTrace);

                if (ex.InnerException != null)
                {
                    sb.AppendLine("Inner Exception:");
                    sb.AppendLine($"Inner Exception Type: {ex.InnerException.GetType().FullName}");
                    sb.AppendLine($"Inner Exception Message: {ex.InnerException.Message}");
                    sb.AppendLine($"Inner Stack Trace:");
                    sb.AppendLine(ex.InnerException.StackTrace);
                }
            }

            return sb.ToString();
        }

        // Запись записи лога в файл
        private static void WriteLogEntryToFile(string logEntry)
        {
            // Синхронизируем доступ к файлу, чтобы избежать ошибок при одновременной записи из разных потоков
            lock (fileLock)
            {
                try
                {
                    // Добавляем запись в конец файла. Создаем файл, если он не существует.
                    File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
                }
                catch (Exception fileEx)
                {
                    // Если не удалось записать в лог-файл, выводим ошибку в консоль (как резервный вариант)
                    Console.WriteLine($"Error writing to log file {logFilePath}: {fileEx.Message}");
                    Console.WriteLine(logEntry); // Выводим саму запись, которую пытались записать
                }
            }
        }
    }
}