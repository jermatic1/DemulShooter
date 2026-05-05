using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DsCore;

namespace DemulShooter
{
    internal delegate void TcpInputCommandHandler(int playerId, int? x, int? y, bool? fire, bool? reload, bool? action);

    internal class TcpInputServer
    {
        private readonly int _port;
        private readonly TcpInputCommandHandler _commandHandler;
        private TcpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;

        public TcpInputServer(int port, TcpInputCommandHandler commandHandler)
        {
            _port = port;
            _commandHandler = commandHandler;
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
                _listener.Start();
                Logger.WriteLog("TcpInputServer listening on 127.0.0.1:" + _port);

                while (_running)
                {
                    TcpClient client = null;
                    try
                    {
                        client = _listener.AcceptTcpClient();
                        Logger.WriteLog("TcpInputServer: client connected " + client.Client.RemoteEndPoint);
                        using (NetworkStream stream = client.GetStream())
                        using (StreamReader reader = new StreamReader(stream, Encoding.ASCII))
                        using (StreamWriter writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true })
                        {
                            writer.WriteLine("DemulShooter TCP input ready");
                            while (_running && client.Connected)
                            {
                                string line = reader.ReadLine();
                                if (line == null)
                                    break;

                                line = line.Trim();
                                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                                    continue;

                                if (TryParseCommand(line, out int playerId, out int? x, out int? y, out bool? fire, out bool? reload, out bool? action))
                                {
                                    _commandHandler(playerId, x, y, fire, reload, action);
                                    writer.WriteLine("OK");
                                }
                                else
                                {
                                    writer.WriteLine("ERR");
                                }
                            }
                        }
                    }
                    catch (SocketException ex)
                    {
                        if (_running)
                            Logger.WriteLog("TcpInputServer socket error: " + ex.Message);
                    }
                    catch (IOException)
                    {
                        // client disconnected
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

        private bool TryParseCommand(string command, out int playerId, out int? x, out int? y, out bool? fire, out bool? reload, out bool? action)
        {
            playerId = 1;
            x = null;
            y = null;
            fire = null;
            reload = null;
            action = null;
            bool anyParsed = false;

            string[] tokens = command.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawToken in tokens)
            {
                string token = rawToken.Trim();
                if (token.Length == 0)
                    continue;

                if (token.StartsWith("p", StringComparison.InvariantCultureIgnoreCase) && token.Length > 1)
                {
                    if (int.TryParse(token.Substring(1), out int pid) && pid >= 1 && pid <= 4)
                    {
                        playerId = pid;
                        anyParsed = true;
                    }
                    continue;
                }

                string[] parts = token.Split('=');
                if (parts.Length != 2)
                    continue;

                string key = parts[0].Trim().ToUpperInvariant();
                string value = parts[1].Trim();

                switch (key)
                {
                    case "PLAYER":
                    case "P":
                        if (int.TryParse(value, out int pid) && pid >= 1 && pid <= 4)
                        {
                            playerId = pid;
                            anyParsed = true;
                        }
                        break;
                    case "X":
                        if (TryParseAxis(value, out int xValue))
                        {
                            x = xValue;
                            anyParsed = true;
                        }
                        break;
                    case "Y":
                        if (TryParseAxis(value, out int yValue))
                        {
                            y = yValue;
                            anyParsed = true;
                        }
                        break;
                    case "FIRE":
                        fire = ParseToggle(value);
                        if (fire.HasValue)
                            anyParsed = true;
                        break;
                    case "RELOAD":
                        reload = ParseToggle(value);
                        if (reload.HasValue)
                            anyParsed = true;
                        break;
                    case "ACTION":
                        action = ParseToggle(value);
                        if (action.HasValue)
                            anyParsed = true;
                        break;
                }
            }

            return anyParsed;
        }

        private bool TryParseAxis(string value, out int axis)
        {
            axis = 0;
            if (string.IsNullOrEmpty(value))
                return false;

            value = value.Trim();
            if (value.EndsWith("%"))
            {
                if (int.TryParse(value.Substring(0, value.Length - 1), out int percent) && percent >= 0 && percent <= 100)
                {
                    axis = (int)((percent / 100.0) * 0xFFFF);
                    return true;
                }
                return false;
            }

            if (int.TryParse(value, out int rawValue) && rawValue >= 0)
            {
                axis = rawValue;
                return true;
            }

            return false;
        }

        private bool? ParseToggle(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            string upperValue = value.Trim().ToUpperInvariant();
            if (upperValue == "1" || upperValue == "ON" || upperValue == "DOWN" || upperValue == "TRUE")
                return true;
            if (upperValue == "0" || upperValue == "OFF" || upperValue == "UP" || upperValue == "FALSE")
                return false;

            return null;
        }
    }
}
