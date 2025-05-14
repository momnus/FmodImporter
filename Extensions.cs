using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using System;
using System.IO;
using System.Linq;

namespace FMODAudioImporter
{
    public static class Extensions
    {
        // Расширение для записи списка строк в TelnetConnection
        public static async Task WriteAsync(this TelnetConnection tc, List<string> lines)
        {
            if (!tc.connected || tc.tcpSocket == null || !tc.tcpSocket.Client.Connected)
            {
                 Logger.LogError("Cannot write to Telnet: Not connected.");
                return;
            }

            try
            {
                for(int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // !!! ИЗМЕНЕНИЕ: Убираем печать ПОЛНОЙ КОМАНДЫ перед ее выполнением.
                    // Оставляем только отладочные сообщения внутри самой команды JS (с [IMPORTER_LOG]).
                    // Это должно сделать лог FMOD более "чистым" от дублирующейся информации.

                    // Отправляем саму команду FMOD Studio
                    string actualCommand = line + "\n"; // Добавляем символ новой строки
                    byte[] commandData = Encoding.ASCII.GetBytes(actualCommand);
                    await tc.tcpSocket.GetStream().WriteAsync(commandData, 0, commandData.Length);

                    // Небольшая задержка между командами в списке
                    await Task.Delay(50);

                    // Логируем отправленную команду в лог-файл приложения (это не меняем)
                    Logger.LogInfo($"Sent FMOD Command {i + 1}/{lines.Count}: {line}"); // Логируем номер команды
                }
                await tc.tcpSocket.GetStream().FlushAsync();

                Logger.LogInfo($"Finished sending {lines.Count} commands batch.");
            }
             catch (IOException ioex)
            {
                 tc.connected = false;
                 tc.Dispose();
                 Logger.LogError($"IOException during Telnet WriteAsync(List<string>): {ioex.Message}", ioex);
            }
             catch (ObjectDisposedException odex)
            {
                 tc.connected = false;
                 tc.Dispose();
                 Logger.LogError($"ObjectDisposedException during Telnet WriteAsync(List<string>): {odex.Message}", odex);
            }
            catch (Exception ex)
            {
                 tc.connected = false;
                 tc.Dispose();
                 Logger.LogError($"An unexpected error occurred during Telnet WriteAsync(List<string>): {ex.Message}", ex);
            }
        }

        // Также изменим логирование в методе WriteAsync(string line) для одиночной команды
        // Используется FmodTelnetHelper для отправки одной команды studio.project.filePath
        public static async Task WriteAsync(this TelnetConnection tc, string line)
        {
            if (!tc.connected || tc.tcpSocket == null || !tc.tcpSocket.Client.Connected)
            {
                 Logger.LogError("Cannot write to Telnet (single line): Not connected.");
                return;
            }

            try
            {
                 if (string.IsNullOrWhiteSpace(line)) return;

                // !!! ИЗМЕНЕНИЕ: Убираем печать одиночной команды перед ее выполнением.
                // FMOD сам может логировать выполнение одиночных команд.

                // Отправляем саму команду
                string actualCommand = line + "\n";
                byte[] data = Encoding.ASCII.GetBytes(actualCommand);
                await tc.tcpSocket.GetStream().WriteAsync(data, 0, data.Length);

                await tc.tcpSocket.GetStream().FlushAsync();

                 // Логируем отправленную команду в лог-файл приложения (это не меняем)
                 Logger.LogInfo($"Sent FMOD Command (single): {line}");
            }
             catch (IOException ioex)
            {
                 tc.connected = false;
                 tc.Dispose();
                 Logger.LogError($"IOException during Telnet WriteAsync(string): {ioex.Message}", ioex);
            }
             catch (ObjectDisposedException odex)
            {
                 tc.connected = false;
                 tc.Dispose();
                 Logger.LogError($"ObjectDisposedException during Telnet WriteAsync(string): {odex.Message}", odex);
            }
            catch (Exception ex)
            {
                 tc.connected = false;
                 tc.Dispose();
                 Logger.LogError($"An unexpected error occurred during Telnet WriteAsync(string): {ex.Message}", ex);
            }
        }


        // Вспомогательный метод для расчета относительного пути папки
        // Возвращает относительный путь папки fullPath к basePath.
        public static string GetRelativeFolderPath(string basePath, string fullPath)
        {
             if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(fullPath))
            {
                 Logger.LogWarning($"GetRelativeFolderPath: basePath or fullPath is null or empty. basePath='{basePath}', fullPath='{fullPath}'.");
                return string.Empty;
            }

             try
             {
                 // Убедимся, что basePath заканчивается на разделитель директорий для корректного сравнения URI
                 if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) && !basePath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                 {
                     basePath += Path.DirectorySeparatorChar;
                 }

                 Uri baseUri = new Uri(basePath, UriKind.Absolute);
                 Uri fullUri = new Uri(fullPath, UriKind.Absolute);

                 // Проверяем, является ли fullPath потомком basePath
                 if (!baseUri.IsBaseOf(fullUri))
                 {
                      Logger.LogWarning($"GetRelativeFolderPath: Full path '{fullPath}' is not a descendant of base path '{basePath}'.");
                     return string.Empty;
                 }

                 Uri relativeUri = baseUri.MakeRelativeUri(fullUri);
                 string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

                 // Заменяем обратные слэши на прямые для унификации (FMOD Telnet, похоже, использует прямые слэши)
                 relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

                 // Получаем путь папки из относительного пути
                 string relativeFolderPath = Path.GetDirectoryName(relativePath);

                 // Normalize the result: handle null/empty/'.' and remove trailing slashes
                 if (string.IsNullOrEmpty(relativeFolderPath) || relativeFolderPath == ".")
                 {
                     return string.Empty; // Корень или текущая директория относительно basePath
                 }
                  else
                 {
                      // Trim any trailing slashes
                     return relativeFolderPath.TrimEnd('/');
                 }
             }
             catch (UriFormatException uriEx)
             {
                  Logger.LogError($"GetRelativeFolderPath: Error creating URI for relative path calculation: Base='{basePath}', Full='{fullPath}'. Message: {uriEx.Message}", uriEx);
                  return string.Empty;
             }
             catch (Exception ex)
             {
                  Logger.LogError($"GetRelativeFolderPath: An unexpected error occurred during calculation ('{basePath}', '{fullPath}'): {ex.Message}", ex);
                  return string.Empty;
             }
        }

        // Вспомогательный метод для проверки, содержит ли строка суффикс (регистронезависимо)
        public static bool EndsWithIgnoreCase(this string source, string value)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            {
                return false;
            }
            return source.EndsWith(value, StringComparison.OrdinalIgnoreCase);
        }

         // Вспомогательный метод для обрезки символов с конца строки
         // аналог TrimEnd(params char[]) для совместимости
         public static string TrimEnd(this string source, params char[] trimChars)
         {
             if (string.IsNullOrEmpty(source) || trimChars == null || trimChars.Length == 0)
             {
                 return source;
             }

             int end = source.Length - 1;
             while (end >= 0 && Array.IndexOf(trimChars, source[end]) != -1)
             {
                 end--;
             }

             if (end < source.Length - 1)
             {
                 return source.Substring(0, end + 1);
             }
             return source; // Нет символов для обрезки
         }

         // Вспомогательный метод для обрезки символов с начала строки
         // аналог TrimStart(params char[]) для совместимости
          public static string TrimStart(this string source, params char[] trimChars)
         {
             if (string.IsNullOrEmpty(source) || trimChars == null || trimChars.Length == 0)
             {
                 return source;
             }

             int start = 0;
             while (start < source.Length && Array.IndexOf(trimChars, source[start]) != -1)
             {
                 start++;
             }

             if (start > 0)
             {
                 return source.Substring(start);
             }
             return source; // Нет символов для обрезки
         }

        // Метод Sleep (закомментирован, так как используются асинхронные задержки)
        // public static void Sleep(float seconds)
        // {
        //     Thread.Sleep((int)(seconds * 1000));
        // }
    }
}