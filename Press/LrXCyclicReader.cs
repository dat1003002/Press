using System;
using Sres.Net.EEIP;
using Press.Properties;

namespace Press.Model
{
    public class LrXCyclicReader : IDisposable
    {
        public EEIPClient Client { get; } = new EEIPClient();
        public bool IsConnected { get; private set; }

        private bool _disposed;

        public void Connect()
        {
            if (IsConnected) return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[EEIP] Connecting to {NqConfig.NqIp} ...");

                Client.IPAddress = NqConfig.NqIp;
                Client.RegisterSession();

                Client.ConfigurationAssemblyInstanceID = NqConfig.Config_Instance;

                Client.O_T_InstanceID = NqConfig.OT_Instance;
                Client.O_T_Length = NqConfig.OT_Length;
                Client.O_T_RealTimeFormat = RealTimeFormat.Modeless;
                Client.O_T_ConnectionType = ConnectionType.Point_to_Point;
                Client.RequestedPacketRate_O_T = 200000;

                Client.T_O_InstanceID = NqConfig.TO_Instance;
                Client.T_O_Length = NqConfig.TO_Length;
                Client.T_O_ConnectionType = ConnectionType.Multicast;
                Client.RequestedPacketRate_T_O = 200000;

                Client.ForwardOpen();
                IsConnected = true;

                System.Diagnostics.Debug.WriteLine("[EEIP] ForwardOpen thành công");
            }
            catch (Exception ex)
            {
                IsConnected = false;
                var msg = ex.InnerException?.Message ?? ex.Message;
                System.Diagnostics.Debug.WriteLine($"[EEIP] Connect thất bại: {msg}");
                throw;
            }
        }

        public void TryReconnect()
        {
            if (IsConnected) return;

            Disconnect();
            try
            {
                Connect();
            }
            catch
            {
            }
        }

        public void Disconnect()
        {
            if (!IsConnected) return;

            try
            {
                Client.ForwardClose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ForwardClose] lỗi không nghiêm trọng: {ex.Message}");
            }

            try
            {
                Client.UnRegisterSession();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UnRegisterSession] lỗi không nghiêm trọng: {ex.Message}");
            }

            IsConnected = false;
            System.Diagnostics.Debug.WriteLine("[EEIP] Disconnected");
        }

        public LrXData ReadPort(int port)
        {
            if (!IsConnected)
            {
                return LrXData.Invalid;
            }

            var ioData = Client.T_O_IOData;
            if (ioData == null || ioData.Length < NqConfig.TO_Length)
            {
                return LrXData.Invalid;
            }

            int offset;
            try
            {
                offset = NqConfig.GetPortOffset(port);
            }
            catch (ArgumentOutOfRangeException)
            {
                return LrXData.Invalid;
            }

            if (offset < 0 || offset + 2 > ioData.Length)
            {
                return LrXData.Invalid;
            }

            try
            {
                short raw = BitConverter.ToInt16(ioData, offset);
                double mm = raw * NqConfig.Resolution;

                return new LrXData { IsValid = true, DistanceMm = mm };
            }
            catch (Exception)
            {
                return LrXData.Invalid;
            }
        }

        public void DebugDumpBuffer()
        {
            if (!IsConnected || Client.T_O_IOData == null || Client.T_O_IOData.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG BUFFER] No data available");
                return;
            }

            var data = Client.T_O_IOData;

            System.Diagnostics.Debug.WriteLine($"[DEBUG BUFFER] Length = {data.Length} bytes | Time: {DateTime.Now:HH:mm:ss.fff}");
        }

        public void Dispose()
        {
            if (_disposed) return;

            Disconnect();

            _disposed = true;
        }
    }
}