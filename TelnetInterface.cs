using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using System.IO; // <-- Убедитесь, что эта директива присутствует

namespace FMODAudioImporter
{
    // Enum'ы Telnet команд и опций
    enum Verbs
    {
        WILL = 251,
        WONT = 252,
        DO = 253,
        DONT = 254,
        IAC = 255
    }

    enum Options
    {
        SGA = 3 // Suppress Go Ahead
    }

    // Публичный класс TelnetConnection, реализующий IDisposable
    public class TelnetConnection : IDisposable
    {
        public TcpClient tcpSocket { get; private set; }
        private NetworkStream networkStream;
        private const int ConnectionTimeoutMs = 10000; // Таймаут для подключения
        // Read timeout is now passed per-call to ReadAsync

        public bool connected { get; set; }

        public TelnetConnection(string Hostname, int Port)
        {
            connected = false;
            tcpSocket = new TcpClient();
        }

        public async Task<bool> ConnectAsync(string Hostname, int Port)
        {
            if (connected) return true;
            try
            {
                var connectTask = tcpSocket.ConnectAsync(Hostname, Port);
                // Ждем подключения или таймаута
                if (await Task.WhenAny(connectTask, Task.Delay(ConnectionTimeoutMs)) != connectTask)
                {
                    // Таймаут подключения
                    Dispose(); // Очищаем ресурсы при таймауте
                    Logger.LogError($"Telnet connection attempt timed out to {Hostname}:{Port}.");
                    throw new TimeoutException($"Connection attempt timed out after {ConnectionTimeoutMs} ms.");
                }

                networkStream = tcpSocket.GetStream();

                // Выполняем базовое согласование Telnet (отправляем DO SGA)
                // Это может помочь в некоторых Telnet-серверах, хотя FMOD Studio может не требовать этого.
                byte[] negotiation = new byte[] { (byte)Verbs.IAC, (byte)Verbs.DO, (byte)Options.SGA };
                await networkStream.WriteAsync(negotiation, 0, negotiation.Length);
                await networkStream.FlushAsync();

                connected = true;
                Logger.LogInfo($"Telnet connection successful to {Hostname}:{Port}.");
                return true;
            }
            catch (SocketException sex)
            {
                Logger.LogError($"SocketException during Telnet connection to {Hostname}:{Port}: {sex.Message}", sex);
                Dispose();
                return false;
            }
            catch (IOException ioex)
            {
                Logger.LogError($"IOException during Telnet connection setup to {Hostname}:{Port}: {ioex.Message}",
                    ioex);
                Dispose();
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"An unexpected error occurred during Telnet connection to {Hostname}:{Port}", ex);
                Dispose();
                return false;
            }
            
            
        }

        // Метод для асинхронного чтения данных с таймаутом
        public async Task<string> ReadAsync(TimeSpan timeout)
        {
            if (!connected || networkStream == null)
            {
                Logger.LogError("Cannot read from Telnet: Not connected or stream is null.");
                return "";
            }

            // Используем CancellationTokenSource для реализации таймаута чтения
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(timeout);
                byte[] buffer = new byte[1024];
                StringBuilder result = new StringBuilder();

                try
                {
                    // Читаем данные, пока есть доступные байты или не наступил таймаут
                    // В Telnet-соединении с FMOD Studio ответ обычно приходит одной порцией после команды,
                    // поэтому однократное чтение может быть достаточным, но цикл надежнее.
                    while (networkStream.DataAvailable || result.Length == 0) // Читаем хотя бы один раз, даже если данных пока нет
                    {
                        // Проверяем токен отмены перед чтением
                        cts.Token.ThrowIfCancellationRequested();

                        // Читаем асинхронно с учетом токена отмены
                        int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);

                        if (bytesRead > 0)
                        {
                            result.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                            // В Telnet-соединении с FMOD Studio ответ часто заканчивается символом новой строки
                            // или специфической строкой. Можно добавить логику для определения конца ответа,
                            // но пока просто читаем доступные данные.
                        }
                        else if (bytesRead == 0 && !tcpSocket.Connected)
                        {
                            // Соединение было закрыто удаленной стороной
                            Logger.LogInfo("Telnet connection closed by remote host during read.");
                            connected = false;
                            Dispose();
                            return result.ToString(); // Возвращаем то, что успели прочитать
                        }

                        // Небольшая задержка, чтобы не блокировать поток при ожидании данных
                        await Task.Delay(10, cts.Token); // Добавлена задержка с учетом токена отмены
                    }
                }
                catch (OperationCanceledException)
                {
                    // Таймаут чтения
                    Logger.LogWarning($"Telnet read operation timed out after {timeout.TotalMilliseconds} ms.");
                    throw new TimeoutException($"Read operation timed out after {timeout.TotalMilliseconds} ms.");
                }
                 catch (IOException ioex)
                {
                     Logger.LogError($"IOException during Telnet read: {ioex.Message}", ioex);
                     connected = false; // Считаем соединение потерянным при ошибке ввода/вывода
                     Dispose(); // Очищаем ресурсы
                     throw; // Перебрасываем исключение
                }
                catch (ObjectDisposedException odex)
                 {
                     Logger.LogError($"ObjectDisposedException during Telnet read: {odex.Message}", odex);
                     connected = false; // Считаем соединение потерянным
                     // Dispose() уже мог быть вызван
                     throw; // Перебрасываем исключение
                 }
                catch (Exception ex)
                {
                    Logger.LogError("An unexpected error occurred during Telnet read", ex);
                    connected = false; // При любой неожиданной ошибке считаем соединение недействительным
                    Dispose(); // Очищаем ресурсы
                    throw; // Перебрасываем исключение
                }

                return result.ToString();
            }
        }


        // Расширение для записи списка строк в TelnetConnection
        public async Task WriteAsync(List<string> lines)
        {
            if (!connected || tcpSocket == null || !tcpSocket.Client.Connected)
            {
                 Logger.LogError("Cannot write to Telnet: Not connected.");
                // Не выбрасываем исключение, просто выходим
                return;
            }

            try
            {
                foreach (var line in lines)
                {
                    // Добавляем символ новой строки, который FMOD Telnet ожидает как завершение команды
                    string command = line + "\n";
                    byte[] data = Encoding.ASCII.GetBytes(command);
                    await tcpSocket.GetStream().WriteAsync(data, 0, data.Length);
                     // Небольшая задержка между командами в списке
                    await Task.Delay(20); // Уменьшена задержка, так как это команды в одном логическом блоке
                }
                await tcpSocket.GetStream().FlushAsync(); // Убедимся, что все отправлено
                 Logger.LogInfo($"Sent {lines.Count} commands batch."); // Логируем отправленную команду
            }
             catch (IOException ioex)
            {
                 connected = false; // Считаем соединение потерянным при ошибке ввода/вывода
                 Dispose(); // Очищаем ресурсы
                 Logger.LogError($"IOException during Telnet WriteAsync(List<string>): {ioex.Message}", ioex);
                 throw; // Перебрасываем исключение
            }
             catch (ObjectDisposedException odex)
            {
                 connected = false; // Считаем соединение потерянным
                 Dispose(); // Очищаем ресурсы
                 Logger.LogError($"ObjectDisposedException during Telnet WriteAsync(List<string>): {odex.Message}", odex);
                 throw; // Перебрасываем исключение
            }
            catch (Exception ex)
            {
                 connected = false; // При любой неожиданной ошибке считаем соединение недействительным
                 Dispose(); // Очищаем ресурсы
                 Logger.LogError($"An unexpected error occurred during Telnet WriteAsync(List<string>): {ex.Message}", ex);
                 throw; // Перебрасываем исключение
            }
        }


        // Метод для асинхронной записи одной строки с таймаутом (добавлен для удобства)
        public async Task WriteAsync(string line)
        {
            if (!connected || tcpSocket == null || !tcpSocket.Client.Connected)
            {
                 Logger.LogError("Cannot write to Telnet: Not connected.");
                // Не выбрасываем исключение, просто выходим
                return;
            }

            try
            {
                // Добавляем символ новой строки, который FMOD Telnet ожидает как завершение команды
                string command = line + "\n";
                byte[] data = Encoding.ASCII.GetBytes(command);
                await tcpSocket.GetStream().WriteAsync(data, 0, data.Length);
                await tcpSocket.GetStream().FlushAsync(); // Убедимся, что все отправлено
                 Logger.LogInfo($"Sent command: {line}"); // Логируем отправленную команду
            }
             catch (IOException ioex)
            {
                 connected = false; // Считаем соединение потерянным при ошибке ввода/вывода
                 Dispose(); // Очищаем ресурсы
                 Logger.LogError($"IOException during Telnet WriteAsync(string): {ioex.Message}", ioex);
                 throw; // Перебрасываем исключение
            }
             catch (ObjectDisposedException odex)
            {
                 connected = false; // Считаем соединение потерянным
                 Dispose(); // Очищаем ресурсы
                 Logger.LogError($"ObjectDisposedException during Telnet WriteAsync(string): {odex.Message}", odex);
                 throw; // Перебрасываем исключение
            }
            catch (Exception ex)
            {
                 connected = false; // При любой неожиданной ошибке считаем соединение недействительным
                 Dispose(); // Очищаем ресурсы
                 Logger.LogError($"An unexpected error occurred during Telnet WriteAsync(string): {ex.Message}", ex);
                 throw; // Перебрасываем исключение
            }
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                 Logger.LogInfo("Disposing TelnetConnection.");
                 // Note: CancellationTokenSource for reading is now created and disposed
                 // within the ReadAsync method using a `using` statement.
                 // No need to cancel/dispose readCancellationTokenSource here.

                if (networkStream != null)
                {
                    try { networkStream.Dispose(); } catch { }
                    networkStream = null;
                }

                if (tcpSocket != null)
                {
                    try { tcpSocket.Close(); } catch { }
                    tcpSocket = null;
                }
            }
             connected = false;
        }

         ~TelnetConnection()
         {
             Dispose(false);
         }
    }
}
