using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DsCore;

namespace DemulShooter
{
    /// <summary>
    /// TCP Input Server for DemulShooter binary protocol.
    /// Compatible with batocera-wine-guns and other clients using the DemulShooter packet format.
    /// Listens on 127.0.0.1:33610 and parses binary packets containing gun coordinates and button states.
    /// </summary>
    internal delegate void TcpInputDataHandler(float[] axisX, float[] axisY, bool[] trigger, bool[] reload);

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
                float[] axisX = new float[MAX_PLAYERS];
                float[] axisY = new float[MAX_PLAYERS];
                bool[] trigger = new bool[MAX_PLAYERS];
                bool[] reload = new bool[MAX_PLAYERS];
                bool[] action = new bool[MAX_PLAYERS];

                int offset = 0;

                // Default/pbx format: X[2] Y[2] EnableInputsHack HideCrosshairs Trigger[2]
                if (packetSize == 20 && bytesRead >= 20)
                {
                    axisX[0] = BitConverter.ToSingle(buffer, offset); offset += 4;
                    axisX[1] = BitConverter.ToSingle(buffer, offset); offset += 4;
                    axisY[0] = BitConverter.ToSingle(buffer, offset); offset += 4;
                    axisY[1] = BitConverter.ToSingle(buffer, offset); offset += 4;

                    offset += 1; // EnableInputsHack
                    offset += 1; // HideCrosshairs

                    trigger[0] = buffer[offset++] != 0;
                    trigger[1] = buffer[offset++] != 0;
                }
                // rha format: X[4] Y[4] EnableInputsHack HideCrosshairs Trigger[4]
                else if (packetSize == 38 && bytesRead >= 38)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        axisX[i] = BitConverter.ToSingle(buffer, offset);
                        offset += 4;
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        axisY[i] = BitConverter.ToSingle(buffer, offset);
                        offset += 4;
                    }

                    offset += 1; // EnableInputsHack
                    offset += 1; // HideCrosshairs

                    for (int i = 0; i < 4; i++)
                        trigger[i] = buffer[offset++] != 0;
                }
                // tra format: X[4] Y[4] EnableInputsHack HideCrosshairs Reload[4] Trigger[4]
                else if (packetSize == 42 && bytesRead >= 42)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        axisX[i] = BitConverter.ToSingle(buffer, offset);
                        offset += 4;
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        axisY[i] = BitConverter.ToSingle(buffer, offset);
                        offset += 4;
                    }

                    offset += 1; // EnableInputsHack
                    offset += 1; // HideCrosshairs

                    for (int i = 0; i < 4; i++)
                        reload[i] = buffer[offset++] != 0;
                    for (int i = 0; i < 4; i++)
                        trigger[i] = buffer[offset++] != 0;
                }
                // wws format: X[2] Y[2] EnableInputsHack HideCrosshairs Reload[2] Trigger[2]
                else if (packetSize == 22 && bytesRead >= 22)
                {
                    axisX[0] = BitConverter.ToSingle(buffer, offset); offset += 4;
                    axisX[1] = BitConverter.ToSingle(buffer, offset); offset += 4;
                    axisY[0] = BitConverter.ToSingle(buffer, offset); offset += 4;
                    axisY[1] = BitConverter.ToSingle(buffer, offset); offset += 4;

                    offset += 1; // EnableInputsHack
                    offset += 1; // HideCrosshairs

                    reload[0] = buffer[offset++] != 0;
                    reload[1] = buffer[offset++] != 0;
                    trigger[0] = buffer[offset++] != 0;
                    trigger[1] = buffer[offset++] != 0;
                }
                // pvz format: X[1] Y[1] EnableInputsHack HideCrosshairs Trigger[1]
                else if (packetSize == 11 && bytesRead >= 11)
                {
                    axisX[0] = BitConverter.ToSingle(buffer, offset); offset += 4;
                    axisY[0] = BitConverter.ToSingle(buffer, offset); offset += 4;

                    offset += 1; // EnableInputsHack
                    offset += 1; // HideCrosshairs

                    trigger[0] = buffer[offset++] != 0;
                }
                else
                {
                    // Unknown format, skip
                    return;
                }

                _dataHandler?.Invoke(axisX, axisY, trigger, reload);
            }
            catch (Exception ex)
            {
                Logger.WriteLog("TcpInputServer packet parse error: " + ex.Message);
            }
        }
    }
}
