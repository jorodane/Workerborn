using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Workerborn
{
    internal sealed class DedicatedAdminPipe : IDisposable
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private readonly NamedPipeClientStream pipe;
        private readonly BlockingCollection<string> reports = new BlockingCollection<string>(4);
        private int closed;
        public bool Closed => Volatile.Read(ref closed) != 0;

        public DedicatedAdminPipe(Guid instance, ConcurrentQueue<string> commands)
        {
            pipe = new NamedPipeClientStream(".", DedicatedAdminProtocol.PipeName(instance), PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect(5000);
            }
            catch
            {
                pipe.Dispose();
                throw;
            }

            _ = Task.Run(() => Read(commands));
            _ = Task.Run(Write);
        }

        public void Publish(string status)
        {
            try { reports.TryAdd(status); }
            catch (InvalidOperationException) when (Closed) { }
        }

        private void Read(ConcurrentQueue<string> commands)
        {
            try
            {
                using var reader = new StreamReader(pipe, Utf8, false, 1024, leaveOpen: true);
                string command;
                while ((command = reader.ReadLine()) != null)
                    if (command.Length <= DedicatedAdminProtocol.MaxCommandLength && commands.Count < 16)
                        commands.Enqueue(command);
            }
            catch (Exception error) when (error is IOException || error is ObjectDisposedException) { }
            finally { Dispose(); }
        }

        private void Write()
        {
            try
            {
                using var writer = new StreamWriter(pipe, Utf8, 4096, leaveOpen: true) { AutoFlush = true };
                foreach (string status in reports.GetConsumingEnumerable())
                    writer.WriteLine(status);
            }
            catch (Exception error) when (error is IOException || error is ObjectDisposedException) { }
            finally { Dispose(); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0)
                return;
            reports.CompleteAdding();
            pipe.Dispose();
        }
    }
}
