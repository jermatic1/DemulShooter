using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DsCore;

namespace DemulShooter
{
    /// <summary>
    /// TCP Input Server for simplified mouse protocol.
    /// Compatible with batocera-wine-guns for BTN_LEFT (trigger), BTN_RIGHT (reload), and BTN_MIDDLE (action).
    /// Packet format: X(float) Y(float) EnableInputsHack(byte) HideCrosshairs(byte) Trigger(byte) Reload(byte) Action(byte)
    /// Total: 13 bytes
    /// </summary>
    internal delegate void TcpInputDataHandler(float[] axisX, float[] axisY, bool[] trigger, bool[] reload, bool[] action);

    internal class TcpInputServer
    {
        private const int DS_PORT = 33610;
        private const int MAX_PLAYERS = 4;

        private readonly TcpInputDataHandler _dataHandler;
        private TcpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;

        public TcpInputServer(TcpInputDataHandler dataHandler)
        {
            _dataHandler = dataHandler;
        }

        public void Start()
        {
            if (_running)
                return;

            _running = true;
            _listenerThread = new Thread(new ThreadStart(ListenerThreadLoop));
            _listenerThread.IsBackground = true;
            _listenerThread.Start();
        }

        public void Stop()
        {
            _running = false;

            try
            {
                _listener?.Stop();
            }
            catch { }

            try
            {
                if (_listenerThread != null && _listenerThread.IsAlive)
                    _listenerThread.Join(1000);
            }
            catch { }
        }

        private void ListenerThreadLoop()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, DS_PORT);
                _listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, 1);
                _listener.Start();
                Logger.WriteLog("TcpInputServer listening on 127.0.0.1:" + DS_PORT);

                while (_running)
                {
                    TcpClient client = null;
                    try
                    {
                        client = _listener.AcceptTcpClient();
                        Logger.WriteLog("TcpInputServer: client connected " + client.Client.RemoteEndPoint);
                        HandleClient(client);
                    }
                    catch (SocketException ex)
                    {
                        if (_running)
                            Logger.WriteLog("TcpInputServer socket error: " + ex.Message);
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLog("TcpInputServer error: " + ex.Message);
                    }
                    finally
                    {
                        if (client != null)
                        {
                            try { client.Close(); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog("TcpInputServer failed to start: " + ex.Message);
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[256];
                int expectedSize = -1;

                while (_running && client.Connected)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;

                    // Auto-detect packet size from first valid packet
                    if (expectedSize < 0)
                    {
                        expectedSize = DetectPacketSize(buffer, bytesRead);
                        if (expectedSize < 0)
                            continue;
                    }

                    if (bytesRead >= expectedSize)
                    {
                        TryParsePacket(buffer, bytesRead, expectedSize);
                    }
                }
            }
        }

        private int DetectPacketSize(byte[] buffer, int length)
        {
            // Common packet sizes for DemulShooter games
            int[] validSizes = { 11, 20, 21, 22, 24, 25, 38, 42 };

            foreach (int size in validSizes)
            {
                if (length >= size)
                    return size;
            }

            return length > 0 ? length : -1;
        }

        private void TryParsePacket(byte[] buffer, int bytesRead, int packetSize)
        {
            try
            {
                // Simple mouse protocol: X(float) Y(float) EnableInputsHack(byte) HideCrosshairs(byte) Trigger(byte) Reload(byte) Action(byte)
                // Total: 13 bytes
                const int EXPECTED_SIZE = 13;

                if (bytesRead < EXPECTED_SIZE)
                    return;

                float axisX = BitConverter.ToSingle(buffer, 0);
                float axisY = BitConverter.ToSingle(buffer, 4);

                // Skip EnableInputsHack and HideCrosshairs flags
                int offset = 8;

                bool trigger = buffer[offset++] != 0;  // BTN_LEFT
                bool reload = buffer[offset++] != 0;   // BTN_RIGHT
                bool action = buffer[offset++] != 0;   // BTN_MIDDLE

                // Convert to arrays for the handler (only player 1 supported in this simple protocol)
                float[] axisXArray = new float[4] { axisX, 0, 0, 0 };
                float[] axisYArray = new float[4] { axisY, 0, 0, 0 };
                bool[] triggerArray = new bool[4] { trigger, false, false, false };
                bool[] reloadArray = new bool[4] { reload, false, false, false };
                bool[] actionArray = new bool[4] { action, false, false, false };

                _dataHandler?.Invoke(axisXArray, axisYArray, triggerArray, reloadArray, actionArray);
            }
            catch (Exception ex)
            {
                Logger.WriteLog("TcpInputServer packet parse error: " + ex.Message);
            }
        }
    }
}
