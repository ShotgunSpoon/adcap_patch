using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace AdCapUnityMCP
{
    internal static class Speech
    {
        private static readonly List<string> Recent = new List<string>();
        private static string _logPath;
        private static bool _logOnly;

        [DllImport("nvdaControllerClient32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int nvdaController_speakText(string text);

        [DllImport("nvdaControllerClient32.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvdaController_cancelSpeech();

        public static void Initialize()
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdCapAccess");
            Directory.CreateDirectory(directory);
            _logPath = Path.Combine(directory, "speech.log");
            _logOnly = string.Equals(Environment.GetEnvironmentVariable("ADCAP_SPEECH_MODE"), "log", StringComparison.OrdinalIgnoreCase);
            File.AppendAllText(_logPath, Environment.NewLine + "=== session " + DateTime.Now.ToString("O") + " ===" + Environment.NewLine);
        }

        public static void Write(string text, bool speak = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var entry = DateTime.Now.ToString("HH:mm:ss.fff") + " " + text.Trim();
            lock (Recent)
            {
                Recent.Add(entry);
                if (Recent.Count > 200) Recent.RemoveAt(0);
            }
            try { File.AppendAllText(_logPath, entry + Environment.NewLine); } catch { }
            if (!speak || _logOnly) return;
            try
            {
                nvdaController_cancelSpeech();
                nvdaController_speakText(text.Trim());
            }
            catch { }
        }

        public static string GetRecent(int count)
        {
            lock (Recent)
            {
                var start = Math.Max(0, Recent.Count - Math.Max(1, count));
                return string.Join("\n", Recent.GetRange(start, Recent.Count - start).ToArray());
            }
        }

        public static void Clear()
        {
            lock (Recent) Recent.Clear();
        }
    }
}
