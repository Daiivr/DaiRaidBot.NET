﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchOffsetType;

namespace SysBot.Base
{
    /// <summary>
    /// Connection to a Nintendo Switch hosting the sys-module via a socket (WiFi).
    /// </summary>
    /// <remarks>
    /// Interactions are performed asynchronously.
    /// </remarks>
    public sealed class SwitchSocketAsync : SwitchSocket, ISwitchConnectionAsync
    {
        private SwitchSocketAsync(IWirelessConnectionConfig cfg) : base(cfg) { }

        public static SwitchSocketAsync CreateInstance(IWirelessConnectionConfig cfg)
        {
            return new SwitchSocketAsync(cfg);
        }

        public override void Connect()
        {
            if (Connected)
            {
                Log("Already connected prior, skipping initial connection.");
                return;
            }

            Log("Connecting to device...");
            IAsyncResult result = Connection.BeginConnect(Info.IP, Info.Port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(5000, true);
            if (!success || !Connection.Connected)
            {
                InitializeSocket();
                throw new Exception("Failed to connect to device.");
            }
            Connection.EndConnect(result);
            Log("Connected!");
            Label = Name;
        }

        public override void Reset()
        {
            if (Connected)
                Disconnect();
            else
                InitializeSocket();
            Connect();
        }

        public override void Disconnect()
        {
            Log("Disconnecting from device...");
            IAsyncResult result = Connection.BeginDisconnect(false, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(5000, true);
            if (!success || Connection.Connected)
            {
                InitializeSocket();
                throw new Exception("Failed to disconnect from device.");
            }
            Connection.EndDisconnect(result);
            Log("Disconnected! Resetting Socket.");
            InitializeSocket();
        }

        /// <summary> Only call this if you are sending small commands. </summary>
        public async Task<int> SendAsync(byte[] buffer, CancellationToken token)
        {
            return await Connection.SendAsync(buffer, token).AsTask();
        }

        private async Task<byte[]> ReadBytesFromCmdAsync(byte[] cmd, int length, CancellationToken token)
        {
            await SendAsync(cmd, token).ConfigureAwait(false);
            var size = (length * 2) + 1;
            var buffer = ArrayPool<byte>.Shared.Rent(size);
            var mem = buffer.AsMemory()[..size];
            await Connection.ReceiveAsync(mem, token);
            var result = DecodeResult(mem, length);
            ArrayPool<byte>.Shared.Return(buffer, true);
            return result;
        }
        private static byte[] DecodeResult(ReadOnlyMemory<byte> buffer, int length)
        {
            var result = new byte[length];
            var span = buffer.Span[..^1]; // Last byte is always a terminator
            Decoder.LoadHexBytesTo(span, result, 2);
            return result;
        }
        public async Task<byte[]> ReadBytesAsync(uint offset, int length, CancellationToken token) => await Read(offset, length, Heap, token).ConfigureAwait(false);
        public async Task<byte[]> ReadBytesMainAsync(ulong offset, int length, CancellationToken token) => await Read(offset, length, Main, token).ConfigureAwait(false);
        public async Task<byte[]> ReadBytesAbsoluteAsync(ulong offset, int length, CancellationToken token) => await Read(offset, length, Absolute, token).ConfigureAwait(false);

        public async Task<byte[]> ReadBytesMultiAsync(IReadOnlyDictionary<ulong, int> offsetSizes, CancellationToken token) => await ReadMulti(offsetSizes, Heap, token).ConfigureAwait(false);
        public async Task<byte[]> ReadBytesMainMultiAsync(IReadOnlyDictionary<ulong, int> offsetSizes, CancellationToken token) => await ReadMulti(offsetSizes, Main, token).ConfigureAwait(false);
        public async Task<byte[]> ReadBytesAbsoluteMultiAsync(IReadOnlyDictionary<ulong, int> offsetSizes, CancellationToken token) => await ReadMulti(offsetSizes, Absolute, token).ConfigureAwait(false);

        public async Task WriteBytesAsync(byte[] data, uint offset, CancellationToken token) => await Write(data, offset, Heap, token).ConfigureAwait(false);
        public async Task WriteBytesMainAsync(byte[] data, ulong offset, CancellationToken token) => await Write(data, offset, Main, token).ConfigureAwait(false);
        public async Task WriteBytesAbsoluteAsync(byte[] data, ulong offset, CancellationToken token) => await Write(data, offset, Absolute, token).ConfigureAwait(false);

        public async Task<ulong> GetMainNsoBaseAsync(CancellationToken token)
        {
            byte[] baseBytes = await ReadBytesFromCmdAsync(SwitchCommand.GetMainNsoBase(), sizeof(ulong), token).ConfigureAwait(false);
            Array.Reverse(baseBytes, 0, 8);
            return BitConverter.ToUInt64(baseBytes, 0);
        }

        public async Task<ulong> GetHeapBaseAsync(CancellationToken token)
        {
            var baseBytes = await ReadBytesFromCmdAsync(SwitchCommand.GetHeapBase(), sizeof(ulong), token).ConfigureAwait(false);
            Array.Reverse(baseBytes, 0, 8);
            return BitConverter.ToUInt64(baseBytes, 0);
        }

        public async Task<string> GetTitleID(CancellationToken token)
        {
            var bytes = await ReadRaw(SwitchCommand.GetTitleID(), 17, token).ConfigureAwait(false);
            return Encoding.ASCII.GetString(bytes).Trim();
        }

        public async Task<string> GetBotbaseVersion(CancellationToken token)
        {
            // Allows up to 9 characters for version, and trims extra '\0' if unused.
            var bytes = await ReadRaw(SwitchCommand.GetBotbaseVersion(), 10, token).ConfigureAwait(false);
            return Encoding.ASCII.GetString(bytes).Trim('\0');
        }

        public async Task<string> GetGameInfo(string info, CancellationToken token)
        {
            var bytes = await ReadRaw(SwitchCommand.GetGameInfo(info), 17, token).ConfigureAwait(false);
            return Encoding.ASCII.GetString(bytes).Trim(new char[] { '\0', '\n' });
        }

        public async Task<bool> IsProgramRunning(ulong pid, CancellationToken token)
        {
            var bytes = await ReadRaw(SwitchCommand.IsProgramRunning(pid), 17, token).ConfigureAwait(false);
            return ulong.TryParse(Encoding.ASCII.GetString(bytes).Trim(), out var value) && value == 1;
        }

        private async Task<byte[]> Read(ulong offset, int length, SwitchOffsetType type, CancellationToken token)
        {
            var method = type.GetReadMethod();
            if (length <= MaximumTransferSize)
            {
                var cmd = method(offset, length);
                return await ReadBytesFromCmdAsync(cmd, length, token).ConfigureAwait(false);
            }

            byte[] result = new byte[length];
            for (int i = 0; i < length; i += MaximumTransferSize)
            {
                int len = MaximumTransferSize;
                int delta = length - i;
                if (delta < MaximumTransferSize)
                    len = delta;

                var cmd = method(offset + (uint)i, len);
                var bytes = await ReadBytesFromCmdAsync(cmd, len, token).ConfigureAwait(false);
                bytes.CopyTo(result, i);
                await Task.Delay((MaximumTransferSize / DelayFactor) + BaseDelay, token).ConfigureAwait(false);
            }
            return result;
        }

        private async Task<byte[]> ReadMulti(IReadOnlyDictionary<ulong, int> offsetSizes, SwitchOffsetType type, CancellationToken token)
        {
            var method = type.GetReadMultiMethod();
            var cmd = method(offsetSizes);
            var totalSize = offsetSizes.Values.Sum();
            return await ReadBytesFromCmdAsync(cmd, totalSize, token).ConfigureAwait(false);
        }

        private async Task Write(byte[] data, ulong offset, SwitchOffsetType type, CancellationToken token)
        {
            var method = type.GetWriteMethod();
            if (data.Length <= MaximumTransferSize)
            {
                var cmd = method(offset, data);
                await SendAsync(cmd, token).ConfigureAwait(false);
                return;
            }
            int byteCount = data.Length;
            for (int i = 0; i < byteCount; i += MaximumTransferSize)
            {
                var slice = data.SliceSafe(i, MaximumTransferSize);
                var cmd = method(offset + (uint)i, slice);
                await SendAsync(cmd, token).ConfigureAwait(false);
                await Task.Delay((MaximumTransferSize / DelayFactor) + BaseDelay, token).ConfigureAwait(false);
            }
        }

        public async Task<byte[]> ReadRaw(byte[] command, int length, CancellationToken token)
        {
            await SendAsync(command, token).ConfigureAwait(false);
            var buffer = new byte[length];
            await Connection.ReceiveAsync(buffer, token);
            return buffer;
        }

        public async Task SendRaw(byte[] command, CancellationToken token)
        {
            await SendAsync(command, token).ConfigureAwait(false);
        }

        public async Task<byte[]> PointerPeek(int size, IEnumerable<long> jumps, CancellationToken token)
        {
            return await ReadBytesFromCmdAsync(SwitchCommand.PointerPeek(jumps, size), size, token).ConfigureAwait(false);
        }

        public async Task PointerPoke(byte[] data, IEnumerable<long> jumps, CancellationToken token)
        {
            await SendAsync(SwitchCommand.PointerPoke(jumps, data), token).ConfigureAwait(false);
        }

        public async Task<ulong> PointerAll(IEnumerable<long> jumps, CancellationToken token)
        {
            var offsetBytes = await ReadBytesFromCmdAsync(SwitchCommand.PointerAll(jumps), sizeof(ulong), token).ConfigureAwait(false);
            Array.Reverse(offsetBytes, 0, 8);
            return BitConverter.ToUInt64(offsetBytes, 0);
        }

        public async Task<ulong> PointerRelative(IEnumerable<long> jumps, CancellationToken token)
        {
            var offsetBytes = await ReadBytesFromCmdAsync(SwitchCommand.PointerRelative(jumps), sizeof(ulong), token).ConfigureAwait(false);
            Array.Reverse(offsetBytes, 0, 8);
            return BitConverter.ToUInt64(offsetBytes, 0);
        }

        public async Task<byte[]> PixelPeek(CancellationToken token)
        {
            await SendAsync(SwitchCommand.PixelPeek(), token).ConfigureAwait(false);
            await Task.Delay(Connection.ReceiveBufferSize / DelayFactor + BaseDelay, token).ConfigureAwait(false);

            var data = await FlexRead(token).ConfigureAwait(false);
            var result = Array.Empty<byte>();
            try
            {
                result = Decoder.ConvertHexByteStringToBytes(data);
            }
            catch (Exception e)
            {
                LogError($"Malformed screenshot data received:\n{e.Message}");
            }

            return result;
        }

        private async Task<byte[]> FlexRead(CancellationToken token)
        {
            List<byte> flexBuffer = new();
            int available = Connection.Available;
            Connection.ReceiveTimeout = 1_000;

            do
            {
                byte[] buffer = new byte[available];
                try
                {
                    Connection.Receive(buffer, available, SocketFlags.None);
                    flexBuffer.AddRange(buffer);
                }
                catch (Exception ex)
                {
                    LogError($"Socket exception thrown while receiving data:\n{ex.Message}");
                    return Array.Empty<byte>();
                }

                await Task.Delay(MaximumTransferSize / DelayFactor + BaseDelay, token).ConfigureAwait(false);
                available = Connection.Available;
            } while (flexBuffer.Count == 0 || flexBuffer.Last() != (byte)'\n');

            Connection.ReceiveTimeout = 0;
            return flexBuffer.ToArray();
        }

        public async Task<long> GetUnixTime(CancellationToken token)
        {
            var result = await ReadBytesFromCmdAsync(SwitchCommand.GetUnixTime(), 8, token).ConfigureAwait(false);
            Array.Reverse(result);
            return BitConverter.ToInt64(result, 0);
        }
    }
}