using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LokiHost.Core;

/// <summary>
/// One rAthena server program (login / char / map) run from the host copy, its console output collected.
/// Stopping asks it to shut down the way a console Ctrl+C does, so it saves and closes cleanly: on Windows rAthena's
/// console_handler (common/core.cpp) leaves CTRL_C_EVENT to the C runtime, which raises SIGINT, and sig_proc turns
/// SIGINT into signal_shutdown(); the "server:shutdown" console command is not usable here because rAthena reads its
/// console with _kbhit() (common/cli.cpp), which never sees a redirected input.
/// </summary>
public sealed class ServerProcess
{
    public string Name { get; }
    Process? _p;
    public event Action<string, string>? Output; // (server, line)
    public event Action<string, int>? Exited;     // (server, exit code)

    public ServerProcess(string name) { Name = name; }

    public bool Running => _p is { HasExited: false };

    public void Start(string hostDir)
    {
        if (Running) return;
        var psi = new ProcessStartInfo(Path.Combine(hostDir, RathenaCopy.ExeName(Name)))
        {
            WorkingDirectory = hostDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // its own hidden console (not a new process group: that would ignore Ctrl+C)
            CreateNoWindow = true,
        };
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(Name, Ansi.Strip(e.Data)); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(Name, Ansi.Strip(e.Data)); };
        p.Exited += (_, _) => Exited?.Invoke(Name, SafeExitCode(p));
        if (OperatingSystem.IsWindows())
        {
            // A child inherits its parent's "ignore Ctrl+C" flag (SetConsoleCtrlHandler(NULL, TRUE) state), and Loki Host
            // can start with it set: measured 2026-09-30 on a test PC, every server then ignored the Stop button's Ctrl+C
            // and was ended by force after 30 s (a ping.exe control did the same); with the flag cleared first, ping and
            // login-server.exe stopped at once ("Shutting down... Terminating... Finished.", exit code 0). CtrlLock keeps
            // this from interleaving with Interrupt, which sets the flag for a moment.
            lock (CtrlLock) { SetConsoleCtrlHandler(IntPtr.Zero, false); p.Start(); }
        }
        else p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _p = p;
    }

    static int SafeExitCode(Process p) { try { return p.ExitCode; } catch (InvalidOperationException) { return -1; } }

    /// <summary>
    /// Takes over this server when it still runs from <paramref name="hostDir"/> (same program path) without us: an
    /// earlier Loki Host was killed or crashed, and the server kept running in its hidden console, holding its port.
    /// Its output cannot be shown any more, but Stop works as usual (Ctrl+C goes to that console). A program of the same
    /// name started from another folder (a standalone rAthena) is never taken.
    /// </summary>
    public bool Adopt(string hostDir)
    {
        if (Running) return false;
        var want = Path.GetFullPath(Path.Combine(hostDir, RathenaCopy.ExeName(Name)));
        foreach (var p in Process.GetProcessesByName(Name))
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch (Exception) { /* another user's or an elevated process */ }
            if (path == null || !string.Equals(Path.GetFullPath(path), want, StringComparison.OrdinalIgnoreCase)) { p.Dispose(); continue; }
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => Exited?.Invoke(Name, SafeExitCode(p));
            _p = p;
            return true;
        }
        return false;
    }

    /// <summary>Asks for a clean shutdown and waits; kills it only if it is still running after <paramref name="wait"/>.</summary>
    public async Task<bool> StopAsync(TimeSpan wait)
    {
        var p = _p;
        if (p == null || p.HasExited) return true;
        bool asked = Interrupt(p.Id);
        var done = await Task.Run(() => p.WaitForExit((int)wait.TotalMilliseconds));
        if (done) return true;
        Output?.Invoke(Name, asked ? $"(did not stop within {wait.TotalSeconds:0} s: ended by force)" : "(could not send Ctrl+C: ended by force)");
        try { p.Kill(); } catch (InvalidOperationException) { }
        await Task.Run(() => p.WaitForExit(5000));
        return false;
    }

    static readonly object CtrlLock = new();

    static bool Interrupt(int pid)
    {
        if (OperatingSystem.IsWindows())
        {
            lock (CtrlLock)
            {
                // Attach to the server's console, send Ctrl+C to it (not to ourselves), detach.
                FreeConsole();
                if (!AttachConsole((uint)pid)) return false;
                SetConsoleCtrlHandler(IntPtr.Zero, true);
                bool ok = GenerateConsoleCtrlEvent(0 /* CTRL_C_EVENT */, 0);
                Thread.Sleep(300);
                FreeConsole();
                SetConsoleCtrlHandler(IntPtr.Zero, false);
                return ok;
            }
        }
        return kill(pid, 2 /* SIGINT */) == 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint dwProcessId);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GenerateConsoleCtrlEvent(uint ev, uint group);
    [DllImport("libc", SetLastError = true)] static extern int kill(int pid, int sig);
}

public static class Ansi
{
    static readonly Regex Seq = new(@"\x1B\[[0-9;?]*[A-Za-z]");
    /// <summary>rAthena's console colours (common/showmsg.cpp ANSI sequences) removed.</summary>
    public static string Strip(string s) => Seq.Replace(s, "");
}
