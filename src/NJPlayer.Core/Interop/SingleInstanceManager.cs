using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NJPlayer.Core.Interop;

/// <summary>
/// Manages single-instance mutex and named-pipe IPC for forwarding command-line arguments to the running player window.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = "Local\\NJPlayer_SingleInstance_Mutex_Session";
    private const string PipeName = "NJPlayer_SingleInstance_Pipe_Session";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _pipeCts;
    private Task? _pipeListenerTask;

    public bool IsFirstInstance => _ownsMutex;
    public event Action<string[]>? ArgumentsReceived;

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    public const int SW_RESTORE = 9;

    public bool TryAcquire(string[] commandLineArgs)
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out _ownsMutex);
        }
        catch
        {
            _ownsMutex = false;
        }

        if (!_ownsMutex)
        {
            // Second instance: send arguments to running instance and exit
            SendArgumentsToRunningInstance(commandLineArgs);
            return false;
        }

        // First instance: start background pipe listener
        StartPipeListener();
        return true;
    }

    private void StartPipeListener()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        _pipeListenerTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var payload = await reader.ReadToEndAsync(token);

                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        var args = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        ArgumentsReceived?.Invoke(args);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(200, token);
                }
            }
        }, token);
    }

    private static void SendArgumentsToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1200);

            using var writer = new StreamWriter(client, Encoding.UTF8);
            var payload = string.Join("\n", args);
            writer.Write(payload);
            writer.Flush();
        }
        catch { }
    }

    public void Dispose()
    {
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();
        _pipeCts = null;

        if (_ownsMutex && _mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
            _ownsMutex = false;
        }
    }
}

