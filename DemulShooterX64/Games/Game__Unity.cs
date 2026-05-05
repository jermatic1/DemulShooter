using System;
using System.Diagnostics;
using System.Windows.Forms;
using DsCore;
using DsCore.Config;
using DsCore.IPC;

namespace DemulShooterX64.Games
{
    public class Game__Unity : Game
    {
        protected class Base_InputData : DsTcpData
        {
            public byte HideCrosshairs = 0;
            public byte EnableInputsHack = 0;

            public Base_InputData(int PlayerNumber) : base(PlayerNumber)
            {
            }
        }

        protected class Base_OutputData : DsTcpData
        {
            public Base_OutputData(int PlayerNumber) : base(PlayerNumber)
            {
            }
        }

        protected string _MainWindowTitle = string.Empty;

        protected DsTcp_Client _Tcpclient;
        protected Base_OutputData _OutputData;
        protected Base_InputData _InputData;

        /// <summary>
        /// Constructor
        /// </summary>
        public Game__Unity(String RomName, String TargetProcessName, String MainWindowTitle)
            : base(RomName, TargetProcessName)
        {
            _MainWindowTitle = MainWindowTitle;
        }

        /// <summary>
        /// Timer event when looking for Process (auto-Hook and auto-close)
        /// </summary>
        protected override void tProcess_Elapsed(Object Sender, EventArgs e)
        {
            if (!_ProcessHooked)
            {
                try
                {
                    Logger.WriteLog("Process.GetProcessesByName");
                    Process[] processes = Process.GetProcessesByName(_Target_Process_Name);
                    if (processes.Length > 0)
                    {
                        Logger.WriteLog("processes.Length > 0");

                        _TargetProcess = processes[0];
                        _ProcessHandle = _TargetProcess.Handle;
                        _TargetProcess_MemoryBaseAddress = _TargetProcess.MainModule.BaseAddress;

                        //Looking for the game's window based on it's Title
                        _GameWindowHandle = IntPtr.Zero;
                        if (_TargetProcess_MemoryBaseAddress != IntPtr.Zero)
                        {
                            Logger.WriteLog("_TargetProcess_MemoryBaseAddress != IntPtr.Zero");

                            // The game may start with other Windows than the main one (BepInEx console, other stuff.....) so we need to filter
                            // the displayed window according to the Title, if DemulShooter is started before the game,  to hook the correct one
                            if (FindGameWindow_Equals(_MainWindowTitle))
                            {
                                String AssemblyDllPath = _TargetProcess.MainModule.FileName.Replace(_Target_Process_Name + ".exe", _Target_Process_Name + @"_Data\Managed\Assembly-CSharp.dll");
                                CheckMd5(AssemblyDllPath);

                                //Start TcpClient to dial with Unity Game
                                _Tcpclient = new DsTcp_Client("127.0.0.1", DsTcp_Client.DS_TCP_CLIENT_PORT);
                                _Tcpclient.PacketReceived += DsTcp_Client_PacketReceived;
                                _Tcpclient.TcpConnected += DsTcp_client_TcpConnected;
                                _Tcpclient.Connect();

                                if (_DisableInputHack)
                                    Logger.WriteLog("Input Hack disabled");

                                _ProcessHooked = true;
                                RaiseGameHookedEvent();
                            }
                            else
                            {
                                Logger.WriteLog("Game Window not found");
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    Logger.WriteLog("Error trying to hook " + _Target_Process_Name + ".exe");
                }
            }
            else
            {
                Process[] processes = Process.GetProcessesByName(_Target_Process_Name);
                if (processes.Length <= 0)
                {
                    _ProcessHooked = false;
                    _TargetProcess = null;
                    _ProcessHandle = IntPtr.Zero;
                    _TargetProcess_MemoryBaseAddress = IntPtr.Zero;
                    Logger.WriteLog(_Target_Process_Name + ".exe closed");
                    Application.Exit();
                }
            }
        }

        ~Game__Unity()
        {
            if (_Tcpclient != null)
                _Tcpclient.Disconnect();
        }

        #region Screen

        /// <summary>
        /// Default GameScale functio, just sending coordinate in a similar way Unity Input.MousePosition() would work
        /// </summary>
        /// <param name="PlayerData"></param>
        /// <returns></returns>
        public override bool GameScale(PlayerSettings PlayerData)
        {
            if (_ProcessHandle != IntPtr.Zero)
            {
                try
                {
                    double TotalResX = _ClientRect.Right - _ClientRect.Left;
                    double TotalResY = _ClientRect.Bottom - _ClientRect.Top;
                    Logger.WriteLog("Game Window Rect (Px) = [ " + TotalResX + "x" + TotalResY + " ]");

                    //Coordinates are Window size range, but inverted Y
                    int X_Value = PlayerData.RIController.Computed_X;
                    int Y_Value = (int)TotalResY - PlayerData.RIController.Computed_Y;

                    if (X_Value < 0)
                        X_Value = 0;
                    if (Y_Value < 0)
                        Y_Value = 0;
                    if (X_Value > (int)TotalResX)
                        X_Value = (int)TotalResX;
                    if (Y_Value > (int)TotalResY)
                        Y_Value = (int)TotalResY;

                    PlayerData.RIController.Computed_X = X_Value;
                    PlayerData.RIController.Computed_Y = Y_Value;
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.WriteLog("Error scaling mouse coordonates to GameFormat : " + ex.Message.ToString());
                }
            }
            return false;
        }

        #endregion

        /// <summary>
        /// Sending a TCP message on connection
        /// </summary>
        protected virtual void DsTcp_client_TcpConnected(Object Sender, EventArgs e)
        {
            if (_HideCrosshair)
                _InputData.HideCrosshairs = 1;
            else
                _InputData.HideCrosshairs = 0;

            if (_DisableInputHack)
                _InputData.EnableInputsHack = 0;
            else
                _InputData.EnableInputsHack = 1;

            _Tcpclient.SendMessage(_InputData.ToByteArray());
        }

        protected virtual void DsTcp_Client_PacketReceived(Object Sender, DsTcp_Client.PacketReceivedEventArgs e)
        { }
    }
}
