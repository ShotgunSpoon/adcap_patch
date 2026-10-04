using UnityEngine;

namespace AdCapUnityMCP
{
    public static class Bootstrap
    {
        private static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;
            var host = new GameObject("AdCap Accessibility and UnityMCP");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<RuntimeHost>();
        }
    }

    internal sealed class RuntimeHost : MonoBehaviour
    {
        private bool _muteForTest;

        private void Awake()
        {
            Speech.Initialize();
            _muteForTest = string.Equals(System.Environment.GetEnvironmentVariable("ADCAP_SPEECH_MODE"), "log", System.StringComparison.OrdinalIgnoreCase);
            if (_muteForTest)
            {
                Application.runInBackground = true;
                AudioListener.volume = 0f;
            }
            AccessibilityNavigator.Initialize();
            McpServer.Start(8765);
            Speech.Write("Accessibility mod loaded", false);
        }

        private void Update()
        {
            if (_muteForTest && AudioListener.volume != 0f) AudioListener.volume = 0f;
            MainThreadQueue.Drain();
            AccessibilityNavigator.UpdateKeyboard();
        }

        private void OnApplicationQuit()
        {
            McpServer.Stop();
        }

    }
}
