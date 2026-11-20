using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Workerborn
{
    internal sealed class VillageTcpPeer : IDisposable
    {
        public const int DefaultPort = 7777;
        public const int MaxPayload = 256 * 1024;
        private const int QueueLimit = 2 * MaxPayload;
        private readonly TcpClient client;
        private readonly ConcurrentQueue<byte[]> incoming = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> outgoing = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim pending = new SemaphoreSlim(0);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private int incomingSize;
        private int outgoingSize;
        private int closed;
        public bool Closed => Volatile.Read(ref closed) != 0;

        public VillageTcpPeer(TcpClient client)
        {
            this.client = client;
            client.NoDelay = true;
            _ = Run();
        }

        public bool Send(byte[] data)
        {
            if (Closed || data == null || data.Length == 0 || data.Length > MaxPayload)
                return false;
            if (Interlocked.Add(ref outgoingSize, data.Length) > QueueLimit)
            {
                Dispose();
                return false;
            }
            outgoing.Enqueue(data);
            pending.Release();
            return true;
        }

        public bool TryRead(out byte[] data)
        {
            if (!incoming.TryDequeue(out data))
                return false;
            Interlocked.Add(ref incomingSize, -data.Length);
            return true;
        }

        private async Task Run()
        {
            Task read = null;
            Task write = null;
            try
            {
                var stream = client.GetStream();
                read = Receive(stream);
                write = Transmit(stream);
                await Task.WhenAny(read, write).ConfigureAwait(false);
            }
            catch (Exception e) when (IsTransportClose(e)) { }
            finally { Dispose(); }

            if (read != null && write != null)
                try { await Task.WhenAll(read, write).ConfigureAwait(false); }
                catch (Exception e) when (IsTransportClose(e)) { }
        }

        private static bool IsTransportClose(Exception e) =>
            e is IOException || e is InvalidOperationException || e is SocketException || e is OperationCanceledException;

        private async Task Receive(NetworkStream stream)
        {
            var header = new byte[4];
            while (await ReadExactly(stream, header).ConfigureAwait(false))
            {
                int length = (int)((uint)header[0] << 24 | (uint)header[1] << 16 | (uint)header[2] << 8 | header[3]);
                if (length <= 0 || length > MaxPayload || Interlocked.Add(ref incomingSize, length) > QueueLimit)
                    return;
                var data = new byte[length];
                if (!await ReadExactly(stream, data).ConfigureAwait(false))
                    return;
                incoming.Enqueue(data);
            }
        }

        private async Task<bool> ReadExactly(NetworkStream stream, byte[] data)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int count = await stream.ReadAsync(data, offset, data.Length - offset, cancellation.Token).ConfigureAwait(false);
                if (count == 0)
                    return false;
                offset += count;
            }
            return true;
        }

        private async Task Transmit(NetworkStream stream)
        {
            var header = new byte[4];
            while (!Closed)
            {
                await pending.WaitAsync(cancellation.Token).ConfigureAwait(false);
                if (!outgoing.TryDequeue(out var data))
                    continue;
                header[0] = (byte)(data.Length >> 24);
                header[1] = (byte)(data.Length >> 16);
                header[2] = (byte)(data.Length >> 8);
                header[3] = (byte)data.Length;
                await stream.WriteAsync(header, 0, header.Length, cancellation.Token).ConfigureAwait(false);
                await stream.WriteAsync(data, 0, data.Length, cancellation.Token).ConfigureAwait(false);
                Interlocked.Add(ref outgoingSize, -data.Length);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) == 0)
            {
                cancellation.Cancel();
                client.Close();
            }
        }

        public static bool TryEndpoint(string address, out string host, out int port)
        {
            host = null;
            port = DefaultPort;
            if (string.IsNullOrWhiteSpace(address) || address.Length > 255)
                return false;
            var parts = address.Trim().Split(':');
            if (parts.Length > 2 || (parts.Length == 2 && (!int.TryParse(parts[1], out port) || port < 1 || port > 65535)))
                return false;
            host = parts[0].Trim().ToLowerInvariant();
            return Uri.CheckHostName(host) == UriHostNameType.Dns || IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork;
        }
    }
}
