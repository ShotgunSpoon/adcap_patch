using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AdCapUnityMCP
{
    internal static class McpServer
    {
        private static TcpListener _listener;
        private static Thread _thread;
        private static volatile bool _running;

        public static void Start(int port)
        {
            if (_running) return;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _running = true;
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "AdCapUnityMCP" };
            _thread.Start();
        }

        public static void Stop()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
        }

        private static void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    var thread = new Thread(() => ClientLoop(client)) { IsBackground = true };
                    thread.Start();
                }
                catch { if (!_running) return; }
            }
        }

        private static void ClientLoop(TcpClient client)
        {
            using (client)
            using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8))
            using (var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true })
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var request = UnityMCP.Json.ParseObject(line);
                    var id = UnityMCP.Json.GetString(request, "id", "unknown");
                    var command = UnityMCP.Json.GetString(request, "command", "");
                    var parameters = UnityMCP.Json.GetObject(request, "params");
                    try
                    {
                        if (command == "ping")
                        {
                            writer.WriteLine(UnityMCP.Json.Response(id, new Dictionary<string, object> { ["pong"] = true, ["game"] = "AdVenture Capitalist" }));
                            continue;
                        }
                        var result = MainThreadQueue.Run(() => Execute(command, parameters)).Result;
                        writer.WriteLine(UnityMCP.Json.Response(id, new Dictionary<string, object> { ["text"] = result }));
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine(UnityMCP.Json.Error(id, ex.GetBaseException().Message));
                    }
                }
            }
        }

        private static string Execute(string command, Dictionary<string, object> parameters)
        {
            switch (command)
            {
                case "a11y_refresh": return AccessibilityNavigator.Refresh(false);
                case "a11y_current": return AccessibilityNavigator.Current(false);
                case "a11y_next": return AccessibilityNavigator.Next(true);
                case "a11y_previous": return AccessibilityNavigator.Previous(true);
                case "a11y_activate": return AccessibilityNavigator.Activate(true);
                case "a11y_controls": return AccessibilityNavigator.ListControls();
                case "a11y_text": return AccessibilityNavigator.ListVisibleText();
                case "a11y_status": return AccessibilityNavigator.Status();
                case "audio_status": return "Unity audio volume " + AudioListener.volume.ToString("0.00");
                case "a11y_text_mode": return AccessibilityNavigator.ToggleTextMode();
                case "a11y_text_next": return AccessibilityNavigator.ReadText(1);
                case "business_current": return AccessibilityNavigator.CurrentBusinessLabel();
                case "business_left": return AccessibilityNavigator.MoveBusiness(-1, false);
                case "business_right": return AccessibilityNavigator.MoveBusiness(1, false);
                case "business_up": return AccessibilityNavigator.MoveBusinessRow(-1, false);
                case "business_down": return AccessibilityNavigator.MoveBusinessRow(1, false);
                case "business_activate": return AccessibilityNavigator.ActivateBusiness(false);
                case "tab_next": return AccessibilityNavigator.CycleAccessibleTab(1, false);
                case "tab_previous": return AccessibilityNavigator.CycleAccessibleTab(-1, false);
                case "tab_activate": return EventAccessibility.IsActive ? EventAccessibility.Activate(false) : AccessibilityNavigator.ActivateTopTab(false);
                case "money": return AccessibilityNavigator.Money();
                case "panel_current": return AccessibilityNavigator.CurrentPanelLabel();
                case "panel_left": return AccessibilityNavigator.MovePanelItem(-1, false);
                case "panel_right": return AccessibilityNavigator.MovePanelItem(1, false);
                case "panel_up": return AccessibilityNavigator.MovePanelRow(-1, false);
                case "panel_down": return AccessibilityNavigator.MovePanelRow(1, false);
                case "panel_activate": return AccessibilityNavigator.ActivatePanelItem(false);
                case "event_entry_status": return EventAccessibility.EntryStatus();
                case "event_enter": return EventAccessibility.EnterActiveEvent(false);
                case "event_return": return EventAccessibility.ReturnToLastNormalPlanet(false);
                case "event_open": return EventAccessibility.Open(false);
                case "event_close": return EventAccessibility.Exit("Event interface closed. Business navigation", false);
                case "event_current": return EventAccessibility.Current(false);
                case "event_tab_next": return EventAccessibility.CycleTab(1, false);
                case "event_tab_previous": return EventAccessibility.CycleTab(-1, false);
                case "event_left": return EventAccessibility.MoveItem(-1, false);
                case "event_right": return EventAccessibility.MoveItem(1, false);
                case "event_up": return EventAccessibility.MoveRow(-1, false);
                case "event_down": return EventAccessibility.MoveRow(1, false);
                case "event_activate": return EventAccessibility.Activate(false);
                case "event_status": return EventAccessibility.Status(false);
                case "event_list": return EventAccessibility.ListItems();
                case "event_dump": return EventAccessibility.DumpState();
                case "event_leaderboard_refresh": return EventAccessibility.RefreshLeaderboard(false);
                case "speech_recent": return Speech.GetRecent(GetInt(parameters, "count", 30));
                case "speech_clear": Speech.Clear(); return "Speech buffer cleared";
                default: throw new InvalidOperationException("Unknown command: " + command);
            }
        }

        private static int GetInt(Dictionary<string, object> values, string key, int fallback)
        {
            if (values == null || !values.ContainsKey(key)) return fallback;
            try { return Convert.ToInt32(values[key]); } catch { return fallback; }
        }
    }
}
