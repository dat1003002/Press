using System;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Sres.Net.EEIP;
using Press.Properties;

namespace Press.Model
{
    public class LrXMessageWriter
    {
        private readonly EEIPClient _client;

        public LrXMessageWriter(EEIPClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<bool> SendZeroShiftAsync(byte port)
        {
            try
            {
                var stream = GetNetworkStream();
                if (stream == null || !stream.CanWrite)
                    return false;

                uint session = GetSessionHandle();
                if (session == 0)
                    return false;

                stream.ReadTimeout = 1200;
                stream.WriteTimeout = 1200;

                byte[] path = { 0x20, 0x85, 0x24, 0x01, 0x30, port };

                byte[] isdu =
                {
                    (byte)NqConfig.IndexOpCmd, (byte)(NqConfig.IndexOpCmd >> 8),
                    NqConfig.SubIndex,
                    (byte)(NqConfig.ValZeroShift >> 8), (byte)NqConfig.ValZeroShift
                };

                int requestLen = 1 + 1 + path.Length + isdu.Length;
                int cmdLen = 6 + 2 + 4 + 4 + requestLen;

                byte[] packet = new byte[24 + cmdLen];

                packet[0] = 0x6F; // SendRRData
                BitConverter.GetBytes((ushort)cmdLen).CopyTo(packet, 2);
                BitConverter.GetBytes(session).CopyTo(packet, 4);

                int ptr = 24;
                ptr += 6; // Interface + timeout = 0
                packet[ptr++] = 0x02; packet[ptr++] = 0x00; // Item count = 2

                // Null Address Item
                ptr += 4;

                // Unconnected Data Item
                packet[ptr++] = 0xB2; packet[ptr++] = 0x00;
                BitConverter.GetBytes((ushort)requestLen).CopyTo(packet, ptr);
                ptr += 2;

                packet[ptr++] = 0x4C; // ISDU_Write
                packet[ptr++] = (byte)(path.Length / 2);

                Array.Copy(path, 0, packet, ptr, path.Length); ptr += path.Length;
                Array.Copy(isdu, 0, packet, ptr, isdu.Length);

                await stream.WriteAsync(packet, 0, packet.Length);
                await Task.Delay(150);

                if (stream.DataAvailable)
                {
                    byte[] resp = new byte[512];
                    await stream.ReadAsync(resp, 0, resp.Length);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ZeroShift] Lỗi: {ex.Message}");
                return false;
            }
        }

        private NetworkStream? GetNetworkStream()
        {
            try
            {
                var type = _client.GetType();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;

                foreach (var name in new[] { "ns", "stream", "networkStream", "_stream" })
                {
                    var field = type.GetField(name, flags);
                    if (field != null)
                        return field.GetValue(_client) as NetworkStream;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private uint GetSessionHandle()
        {
            try
            {
                var type = _client.GetType();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;

                var prop = type.GetProperty("SessionHandle", flags);
                if (prop != null)
                    return (uint)prop.GetValue(_client)!;

                var field = type.GetField("sessionHandle", flags) ?? type.GetField("_sessionHandle", flags);
                if (field != null)
                    return (uint)field.GetValue(_client)!;

                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}