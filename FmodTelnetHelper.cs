using System.Threading.Tasks;
using System;
using System.IO;
using FMODAudioImporter;
using System.Linq;
using System.Text;

namespace FMODAudioImporter
{
    /// <summary>
    /// Вспомогательный класс для выполнения специфичных команд FMOD Studio через Telnet.
    /// </summary>
    public static class FmodTelnetHelper
    {
        /// <summary>
        /// Асинхронно получает путь к текущему проекту FMOD Studio через Telnet.
        /// </summary>
        /// <param name="connection">Активное Telnet-соединение с FMOD Studio.</param>
        /// <returns>Путь к файлу проекта FMOD Studio или null, если не удалось получить или распарсить.</returns>
        public static async Task<string> GetProjectPathAsync(TelnetConnection connection)
        {
            if (connection == null || !connection.connected)
            {
                Logger.LogWarning("FmodTelnetHelper.GetProjectPathAsync: Telnet connection is not active.");
                return null;
            }

            try
            {
                // Отправляем правильную команду для получения пути к файлу проекта
                await connection.WriteAsync("studio.project.filePath");
                Logger.LogInfo("FmodTelnetHelper.GetProjectPathAsync: Sent 'studio.project.filePath'.");

                // Читаем ответ. Ожидаем путь к проекту в ответе.
                // Таймаут увеличен до 10 секунд на случай долгих ответов
                string projectPathResponse = await connection.ReadAsync(TimeSpan.FromSeconds(10));
                Logger.LogInfo($"FmodTelnetHelper.GetProjectPathAsync: Received raw response: '{projectPathResponse}'."); // Log raw response for debugging

                string projectFilePath = null;
                if (!string.IsNullOrEmpty(projectPathResponse))
                {
                    // !!! НОВОЕ: Ищем префикс "out():" в сыром ответе и извлекаем путь после него.
                    const string prefix = "out():";
                    int prefixIndex = projectPathResponse.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);

                    if (prefixIndex != -1)
                    {
                        Logger.LogInfo($"FmodTelnetHelper.GetProjectPathAsync: Found prefix '{prefix}' in raw response at index {prefixIndex}.");

                        // Извлекаем часть строки после префикса
                        string potentialPathWithOptions = projectPathResponse.Substring(prefixIndex + prefix.Length);

                        // Ищем конец пути - он должен заканчиваться на .fspro, а затем могут идти пробелы, новые строки, или замыкающая кавычка.
                        // Найдем индекс первого символа, который не может быть частью пути файла.
                        // Путь может содержать буквы, цифры, '/', ':', '.', '_', '-'
                        // Символы, которые МОГУТ завершать путь в этом контексте ответа: пробел, \r, \n, '. (если он не часть .fspro)', одинарная кавычка (').
                        // Самый надежный способ - найти ".fspro" и взять все до символов новой строки или конца ответа после него.
                        int fsproIndex = potentialPathWithOptions.IndexOf(".fspro", StringComparison.OrdinalIgnoreCase);

                        if (fsproIndex != -1)
                        {
                             // Путь заканчивается на .fspro. Берем подстроку от начала potentialPathOptions до конца .fspro
                             string pathCandidate = potentialPathWithOptions.Substring(0, fsproIndex + ".fspro".Length);

                             // Теперь обрезаем любые окружающие пробелы или кавычки из этого кандидата.
                             projectFilePath = pathCandidate.Trim().Trim('\'', '"');
                             Logger.LogInfo($"FmodTelnetHelper.GetProjectPathAsync: Parsed path ending with .fspro: '{projectFilePath}'.");
                        }
                         else
                         {
                              // Если .fspro не найден, возможно, формат ответа изменился, или это другое сообщение.
                             Logger.LogWarning($"FmodTelnetHelper.GetProjectPathAsync: Found '{prefix}' but could not find '.fspro' after it.");
                              // Попробуем просто взять всю оставшуюся строку после префикса и обрезать. (Менее надежно)
                             projectFilePath = potentialPathWithOptions.Trim().Trim('\'', '"');
                             Logger.LogInfo($"FmodTelnetHelper.GetProjectPathAsync: Falling back to trimming after prefix: '{projectFilePath}'.");
                         }

                    }
                    else
                    {
                        // Если префикс "out():" не найден в сыром ответе вообще
                        Logger.LogWarning("FmodTelnetHelper.GetProjectPathAsync: Could not find prefix 'out():' in the raw response.");
                         // Полный ответ уже логируется выше как "Received raw response"
                    }
                }
                else
                {
                    Logger.LogWarning("FmodTelnetHelper.GetProjectPathAsync: Received empty or null response for project path.");
                }

                // Возвращаем извлеченный путь к файлу проекта
                return projectFilePath; // Может быть null, если парсинг не удался
            }
            catch (TimeoutException timeoutEx)
            {
                 Logger.LogError($"FmodTelnetHelper.GetProjectPathAsync: Timeout while waiting for project path response: {timeoutEx.Message}", timeoutEx);
                 return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"FmodTelnetHelper.GetProjectPathAsync: An error occurred while getting or parsing project path: {ex.Message}", ex);
                return null;
            }
        }
    }
}