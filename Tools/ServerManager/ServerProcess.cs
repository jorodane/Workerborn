using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Workerborn.ServerManager;

internal sealed class ServerProcess : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private static readonly JsonSerializerOptions StatusJson = new() { IncludeFields = true };
    private readonly Process child;
    private readonly string logPath;
    private readonly NamedPipeServerStream pipe;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim writeLock = new(1);
    private readonly char[] logBuffer = new char[8192];
    private readonly Task reading;
    private StreamReader? log;
    private StreamWriter? commands;
    private DedicatedAdminStatus? status;
    private string? transportError;
    private int stopFailure;

    public bool Exited => child.HasExited;
    public int ExitCode => child.ExitCode;
    public bool Connected => Volatile.Read(ref commands) != null;
    public DedicatedAdminStatus? Status => Volatile.Read(ref status);
    public string? TransportError => Volatile.Read(ref transportError);

    public ServerProcess(ServerWorld world)
    {
        string folder = AppContext.BaseDirectory;
        string hostFile = Path.Combine(folder, "WorkerbornServerManager.host");
        string executable = File.Exists(hostFile) ? File.ReadAllText(hostFile).Trim() : "WorkerbornServer.exe";
        executable = Path.Combine(folder, Path.GetFileName(executable));
        if (!File.Exists(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("관리 창과 같은 폴더에서 Workerborn 서버 실행 파일을 찾을 수 없어요.", executable);

        Guid instance = Guid.NewGuid();
        logPath = Path.Combine(Path.GetTempPath(), $"WorkerbornServer-{instance:N}.log");
        pipe = new NamedPipeServerStream(DedicatedAdminProtocol.PipeName(instance), PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string arg in new[]
        {
            "-workerborn-server", "-batchmode", "-nographics", "-logFile", logPath,
            "-world", world.Path, "-bind", world.Bind, "-port", world.Port.ToString(CultureInfo.InvariantCulture),
            "-character-limit", world.InitialCharacterLimit.ToString(CultureInfo.InvariantCulture),
            DedicatedAdminProtocol.PipeOption, instance.ToString("N")
        })
            start.ArgumentList.Add(arg);

        try { child = Process.Start(start) ?? throw new InvalidOperationException("서버 실행에 실패했어요."); }
        catch
        {
            pipe.Dispose();
            lifetime.Dispose();
            throw;
        }
        reading = ReadStatusAsync(lifetime.Token);
    }

    private async Task ReadStatusAsync(CancellationToken cancellation)
    {
        try
        {
            await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Utf8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Utf8, leaveOpen: true) { AutoFlush = true };
            Volatile.Write(ref commands, writer);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellation).ConfigureAwait(false)) != null)
            {
                var report = JsonSerializer.Deserialize<DedicatedAdminStatus>(line, StatusJson);
                if (report == null)
                    continue;
                if (report.stopFailed)
                    Interlocked.Exchange(ref stopFailure, 1);
                Volatile.Write(ref status, report);
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException or JsonException)
        {
            if (!cancellation.IsCancellationRequested)
                Volatile.Write(ref transportError, error.Message);
        }
        finally
        {
            Volatile.Write(ref commands, null);
            pipe.Dispose();
        }
    }

    public async Task<bool> SendAsync(string command)
    {
        if (!Connected || command.Length > DedicatedAdminProtocol.MaxCommandLength ||
            command.AsSpan().IndexOfAny('\r', '\n') >= 0)
            return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await writeLock.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                var writer = Volatile.Read(ref commands);
                if (writer == null)
                    return false;
                await writer.WriteLineAsync(command.AsMemory(), timeout.Token).ConfigureAwait(false);
                return true;
            }
            finally { writeLock.Release(); }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return false;
        }
    }

    public bool TakeStopFailure() => Interlocked.Exchange(ref stopFailure, 0) != 0;

    public void CancelStartup() => pipe.Dispose();

    public string ReadLog()
    {
        if (log == null)
        {
            if (!File.Exists(logPath))
                return "";
            log = new StreamReader(new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), Utf8);
        }
        int length = log.Read(logBuffer, 0, logBuffer.Length);
        return new string(logBuffer, 0, length);
    }

    public void Dispose()
    {
        lifetime.Cancel();
        pipe.Dispose();
        child.Dispose();
        log?.Dispose();
        _ = reading.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
        try { File.Delete(logPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
