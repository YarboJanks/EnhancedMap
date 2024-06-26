﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using EnhancedMap.Diagnostic;

namespace EnhancedMap.Core
{
    public enum MSG_RECV
    {
        USELESS_0 = 0x400 + 301,
        USELESS_1,
        LOGIN,
        USELESS_2,
        MANA_MAXMANA,
        USELESS_3,
        USELESS_4,
        LOGOUT,
        HP_MAXHP,
        STAM_MAXSTAM,
        HOUSES_BOATS_INFO,
        DEL_HOUSES_BOATS_INFO,
        FACET_CHANGED,
        USELESS_5,
        USELESS_6,

        // custom
        MOBILE_ADD_STRCT,
        MOBILE_REMOVE_STRCT
    }

    public enum MSG_SEND
    {
        ATTACH_TO_UO = 0x400 + 200,
        USELESS_0,
        GET_LOCATION_INFO,
        USELESS_1,
        GET_STATS_INFO,
        USELESS_2,
        USELESS_3,
        SEND_SYS_MSG,
        GET_HOUSES_BOATS_INFO,
        ADD_CMD,
        GET_PLAYER_SERIAL,
        GET_SHARD_NAME,
        USELESS_4,
        GET_UO_HWND,
        GET_FLAGS,
        USELESS_5,
        USELESS_6,

        // custom
        GET_FACET = 0x400 + 500,
        GET_HP,
        GET_STAM,
        GET_MANA,
        GET_MOBILES
    }


    public static class UOClientManager
    {


        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        private const string CLASS_NAME = "UOASSIST-TP-MSG-WND";
        private const string UO_ENHANCED_CLIENT = "UOSA";
        private const string CLASSIC_UO = "ClassicUO";

        private static IntPtr _maphWnd;
        private static bool _isScanning;
        private static Process _activeUoProcess;

        public static bool IsAttached { get; private set; }
        public static IntPtr hWnd { get; private set; }

        public static void Initialize(IntPtr maphWnd)
        {
            _maphWnd = maphWnd;
        }

        public static void AttachToActiveClient()
        {
            if (_isScanning)
                return;

            _isScanning = true;
            var foregroundProcess = GetForegroundProcess();
            if (foregroundProcess?.ProcessName == CLASSIC_UO && foregroundProcess?.Id != _activeUoProcess?.Id)
            {
                var uoAssistHwnd = GetUoAssistWindowHandle(foregroundProcess.Id);
                if (uoAssistHwnd != IntPtr.Zero)
                {
                    _activeUoProcess = foregroundProcess;

                    Logger.Good("Assistants founds: " + _activeUoProcess.MainWindowTitle);

                    StringBuilder sb = new StringBuilder(510);
                    int f = SendMessageA(uoAssistHwnd, 13, 510, sb);
                    f = SendMessage(uoAssistHwnd, (int)MSG_SEND.ATTACH_TO_UO, _maphWnd.ToInt32(), 1);
                    if (f == 2)
                    {
                        f = SendMessage(uoAssistHwnd, (int)MSG_SEND.ATTACH_TO_UO, _maphWnd.ToInt32(), 1);
                    }
                    if (f == 1)
                    {
                        f = SendMessage(uoAssistHwnd, (int)MSG_SEND.GET_HOUSES_BOATS_INFO, 0, 0);
                        if (f != 1)
                            Logger.Warn("Unable to get Houses and Boats info from assistants. Probably assistants doesn't support this feature.");

                        OEUO_Manager.Attach();
                        IsAttached = true;
                        hWnd = uoAssistHwnd;
                    }
                }
            }
            else
            {
                //if no UO processes running disconnect.
                var uoClassicProcess = Process.GetProcessesByName(CLASSIC_UO);
                if (uoClassicProcess.Length == 0)
                {
                    if (IsAttached)
                        IsAttached = false;
                }
            }

            _isScanning = false;
        }

        public static IEnumerable<IntPtr> FindWindowsWithText(string titleText)
        {
            return FindWindows(delegate (IntPtr wnd, IntPtr param) { return GetWindowText(wnd).Contains(titleText); });
        }


        private static IEnumerable<IntPtr> FindWindows(EnumWindowsProc filter)
        {
            List<IntPtr> windows = new List<IntPtr>();

            EnumWindows(delegate (IntPtr h, IntPtr lParam)
            {
                if (filter(h, lParam))
                {
                    // Logger.Log("ASSISTUO-CLASS: 0x" + h.ToString("X"));
                    windows.Add(h);
                }

                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static string GetWindowText(IntPtr hWnd)
        {
            int size = GetWindowTextLength(hWnd);
            if (size > 0)
            {
                var builder = new StringBuilder(size + 1);
                GetWindowText(hWnd, builder, builder.Capacity);
                return builder.ToString();
            }

            return string.Empty;
        }

        public static Dictionary<IntPtr, string> GetClientsWindowTitles()
        {
            _isScanning = true;

            List<IntPtr> clients = Global.SettingsCollection["clienttype"].ToInt() == 1 ? Process.GetProcessesByName(UO_ENHANCED_CLIENT).Select(s => s.MainWindowHandle).ToList() : FindWindowsWithText(CLASS_NAME).ToList();
            Dictionary<IntPtr, string> titles = new Dictionary<IntPtr, string>();

            foreach (IntPtr ptr in clients)
            {
                GetWindowThreadProcessId(ptr, out uint pid);
                Process p = Process.GetProcessById((int)pid);
                if (p != null) titles[ptr] = p.MainWindowTitle;
            }

            _isScanning = false;

            return titles;
        }

        public static IntPtr GetUoAssistWindowHandle(int queryPid)
        {
            var dict = new Dictionary<IntPtr, Process>();
            var clients = FindWindowsWithText(CLASS_NAME).ToList();
            foreach (IntPtr ptr in clients)
            {
                GetWindowThreadProcessId(ptr, out uint pid);
                if (pid == queryPid)
                {
                    return ptr;
                }
            }
            return IntPtr.Zero;
        }

        public static void SysMessage(string msg, int col = 999)
        {
            if (hWnd != IntPtr.Zero && IsAttached)
                SendMessage(hWnd, (int)MSG_SEND.SEND_SYS_MSG, Get_Hiword_Loword(1, col), GlobalAddAtom(msg));
        }

        private static int Get_Hiword_Loword(int hi, int lo)
        {
            int i = hi * 65536;
            i = i | (lo & 65535);
            return i;
        }

        public static Process GetForegroundProcess()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0)
            {
                return null;
            }
            Process p = Process.GetProcessById((int)pid);
            return p;
        }


        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetForegroundWindow();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern short GlobalAddAtom(string atomName);

        [DllImport("USER32.DLL")]
        private static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr lParam);

        [DllImport("USER32.DLL")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("USER32.DLL")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("USER32.DLL")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern int SendMessageA(IntPtr hWnd, int msg, int wParam, StringBuilder sb);

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public ShowWindowCommands showCmd;
            public Point ptMinPosition;
            public Point ptMaxPosition;
            public Rectangle rcNormalPosition;
        }

        internal enum ShowWindowCommands
        {
            Hide = 0,
            Normal = 1,
            Minimized = 2,
            Maximized = 3
        }
    }
}