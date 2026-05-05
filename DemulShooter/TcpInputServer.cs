using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DsCore;

namespace DemulShooter
{
    /// <summary>
    /// TCP Input Server for simplified mouse protocol supporting 4 players.
    /// Compatible with batocera-wine-guns for BTN_LEFT (trigger), BTN_RIGHT (reload), and BTN_MIDDLE (action).
    /// Packet format: X[4](float) Y[4](float) EnableInputsHack(byte) HideCrosshairs(byte) Trigger[4](byte) Reload[4](byte) Action[4](byte)
    /// Total: 46 bytes
    /// </summary>
    internal delegate void TcpInputDataHandler(float[] axisX, float[] axisY, bool[] trigger, bool[] reload, bool[] action);

    internal class TcpInputServer
    {
        private const int MAX_PLAYERS = 4;

        private readonly TcpInputDataHandler _dataHandler;
        private readonly int _port;
        private TcpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;

        public TcpInputServer(TcpInputDataHandler dataHandler, int port)
        {
            _dataHandler = dataHandler;
            _port = port;
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
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, 1);
                _listener.Start();
                Logger.WriteLog("TcpInputServer listening on 127.0.0.1:" + _port);

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
                // Simple mouse protocol for 4 players: X[4](float) Y[4](float) EnableInputsHack(byte) HideCrosshairs(byte) Trigger[4](byte) Reload[4](byte) Action[4](byte)
                // Total: 16 + 16 + 1 + 1 + 4 + 4 + 4 = 46 bytes
                const int EXPECTED_SIZE = 46;

                if (bytesRead < EXPECTED_SIZE)
                    return;

                float[] axisX = new float[4];
                float[] axisY = new float[4];
                bool[] trigger = new bool[4];
                bool[] reload = new bool[4];
                bool[] action = new bool[4];

                int offset = 0;

                // Read X coordinates for all 4 players
                for (int i = 0; i < 4; i++)
                {
                    axisX[i] = BitConverter.ToSingle(buffer, offset);
                    offset += 4;
                }

                // Read Y coordinates for all 4 players
                for (int i = 0; i < 4; i++)
                {
                    axisY[i] = BitConverter.ToSingle(buffer, offset);
                    offset += 4;
                }

                // Skip EnableInputsHack and HideCrosshairs flags
                offset += 2;

                // Read trigger states for all 4 players
                for (int i = 0; i < 4; i++)
                    trigger[i] = buffer[offset++] != 0;

                // Read reload states for all 4 players
                for (int i = 0; i < 4; i++)
                    reload[i] = buffer[offset++] != 0;

                // Read action states for all 4 players
                for (int i = 0; i < 4; i++)
                    action[i] = buffer[offset++] != 0;

                _dataHandler?.Invoke(axisX, axisY, trigger, reload, action);
            }
            catch (Exception ex)
            {
                Logger.WriteLog("TcpInputServer packet parse error: " + ex.Message);
            }
        }
    }
}
