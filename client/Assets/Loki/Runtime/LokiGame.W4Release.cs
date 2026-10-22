using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4b (w4release): the smoke / test code is compiled only into development builds. Editor/LokiSetup.cs BuildWindows
    /// defines LOKI_SMOKE for a development build; a release build (-lokiRelease, LokiSetup.BuildWindows) is compiled without it
    /// and so has none of: the smokes and their dispatch, the public test hooks (TestXxx / XxxForTest), the -loki* test arguments
    /// (-lokiSmoke, -lokiSmokeOnly, -lokiUser, -lokiPass, -lokiChar, -lokiLogin, -lokiOnline, …), the auto-login with loki.json's
    /// account (username / password / charName), the offline demo, loki.json's startupCommands and debugAutoWalk. A release build
    /// always starts on the login screen. The wrap rule, the proof and what a release build can no longer be smoke-tested for:
    /// design notes
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>One log line at the start of the game: which kind of build this is and, in a release build, which of loki.json's
        /// test fields it found and does not use (never their values).</summary>
        private static void W4ReleaseLogBuild(LokiConfig cfg)
        {
            bool account = !string.IsNullOrEmpty(cfg.username) || !string.IsNullOrEmpty(cfg.password);
            int commands = cfg.startupCommands != null ? cfg.startupCommands.Length : 0;
            Debug.Log($"[Build] release build: no test code; loki.json's test fields are not used (in '{cfg.LoadedFrom}': username / password {(account ? "set" : "empty")}, " +
                      $"charName {(string.IsNullOrEmpty(cfg.charName) ? "empty" : "set")}, debugAutoWalk {cfg.debugAutoWalk}, startupCommands {commands}): no auto-login, no offline demo, the login screen");
        }

        /// <summary>One log line at the end of Begin (the VR rig exists): is the login screen up, which screen, is an auto-login on its
        /// way, the session's state. A release build must always say: login screen shown True, auto-login False, session Idle.</summary>
        private void W4ReleaseLogStart()
        {
            bool shown = Login != null && Login.gameObject.activeSelf;
            Debug.Log($"[Build] start: login screen shown {shown} (screen {(Login != null ? Login.Current.ToString() : "none")}), auto-login {_autoLogin}, " +
                      $"session {(Session != null ? Session.State.ToString() : "none")}, server {_boot.Config.loginHost}:{_boot.Config.loginPort}");
        }
    }
}
