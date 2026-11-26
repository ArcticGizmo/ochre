using System.Diagnostics;
using Ochre.Core;
using Ochre.Core.Hosting;
using Ochre.Core.Sessions;

namespace Ochre.Cli;

internal static class Program
{
    private const int NotRunning = 3;

    private static int Main(string[] args)
    {
        var profile = OchreProfile.Current;
        return args switch
        {
            [] or ["help"] or ["-h"] or ["--help"] => Usage(Console.Out, 0),
            ["status"] => Status(profile),
            ["open"] => Open(profile),
            ["open", var task] => OpenTask(profile, task, null),
            ["open", var task, "--bring"] => OpenTask(profile, task, "bring"),
            ["open", var task, "--new"] => OpenTask(profile, task, "new"),
            ["close"] => Send(profile, "close", null),
            ["quit"] => Quit(profile),
            ["dev", "eval", var script] => Send(profile, "dev-eval", script, print: true),
            ["dev", "eval", "--window", var window, var script] => Send(profile, "dev-eval", $"{window}\n{script}", print: true),
            ["dev", "capture", var path] => Send(profile, "dev-capture", Path.GetFullPath(path), print: true),
            ["dev", "capture", "--window", var window, var path] => Send(profile, "dev-capture", $"{window}\n{Path.GetFullPath(path)}", print: true),
            ["dev", "windows"] => Send(profile, "dev-windows", null, print: true),
            ["dev", "jump"] => Send(profile, "dev-jump", null, print: true),
            ["dev", "move-window", var window, var desktop] => Send(profile, "dev-move-window", $"{window}\n{desktop}", print: true),
            ["dev", "session-ending"] => Send(profile, "dev-session-ending", null, print: true),
            ["dev", "notifications"] => Send(profile, "dev-notifications", null, print: true),
            ["dev", "notify", var tag, var action, .. var inputs] => Send(profile, "dev-notify", string.Join("\n", new[] { tag, action }.Concat(inputs)), print: true),
            ["task", var op, .. var rest] => Task(profile, op, rest),
            _ => Usage(Console.Error, 2),
        };
    }

    // The session and task come from the environment Ochre gave the agent; the host checks the binding.
    private static int Task(OchreProfile profile, string op, string[] args)
    {
        var request = new AgentRequest(
            Environment.GetEnvironmentVariable("OCHRE_SESSION"),
            Environment.GetEnvironmentVariable("OCHRE_TASK"),
            op,
            args);
        return Send(profile, "agent", request.ToJson(), print: true);
    }

    private static int Usage(TextWriter to, int code)
    {
        // "ochre" installed (ochre.com), "ochre-cli" in a dev checkout.
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "ochre";
        to.WriteLine($$"""
            Usage: {{name}} <command>

              status                 whether Ochre is running, and its version and profile
              open                   open a window here, starting Ochre if it isn't running
              open <task> [--bring|--new]
                                     show a task, e.g. T-0042, in a window on this desktop; if only a window on
                                     another desktop shows it, --bring moves that window here and --new opens
                                     another here (asked when neither is given)
              close                  close every window; Ochre keeps running in the tray
              quit                   quit Ochre and wait for it to exit

            Dev builds of Ochre also accept:
              dev eval [--window <id>] "<js>"
                                     run script in a window (default: the last used) and print the result
              dev capture [--window <id>] <file.png>
                                     save a picture of a window
              dev windows            list the open windows: id, task, desktop, bounds
              dev jump               do what the jump-to-task hotkey does
              dev move-window <id> <desktop-guid>
                                     move a window to another virtual desktop
              dev session-ending     act as if Windows were shutting down (dev builds)

            Inside an agent Ochre started (OCHRE_SESSION and OCHRE_TASK set):
              task current                       the task id this session works on
              task show [--plan]                 title, state, plan, notes and park note
              task check <item> / uncheck <item> tick or untick a plan item, e.g. p3
              task add-step "<text>" [--under <item>]
                                                 add a plan item
              task note "<text>"                 append to the task's notes
              task park-note "<text>"            while the user parks the task: where you are and the next step
            """);
        return code;
    }

    private static int Status(OchreProfile profile)
    {
        if (!InstanceChannel.IsRunning(profile))
        {
            Console.WriteLine($"not running (profile {Describe(profile)})");
            return NotRunning;
        }
        return Send(profile, "ping", null, print: true);
    }

    private static int Open(OchreProfile profile)
    {
        if (InstanceChannel.IsRunning(profile)) return Send(profile, "open", null);
        if (!StartHost(null)) return NotRunning;
        Console.WriteLine("starting");
        return 0;
    }

    private static int OpenTask(OchreProfile profile, string task, string? choice)
    {
        if (!InstanceChannel.IsRunning(profile))
        {
            // In the tray, so the only window that opens is the task's (the saved windows reopen with it).
            if (!StartHost("--tray")) return NotRunning;
            var deadline = Stopwatch.StartNew();
            while (!InstanceChannel.IsRunning(profile) || InstanceChannel.Send(profile, "ping", null, TimeSpan.FromSeconds(2)) is null)
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(20))
                {
                    Console.Error.WriteLine("Ochre was started but did not answer.");
                    return 1;
                }
                Thread.Sleep(200);
            }
        }

        var reply = InstanceChannel.Send(profile, "open-task", choice is null ? task : $"{task} {choice}", TimeSpan.FromSeconds(30));
        if (reply != "elsewhere") return Report(reply, print: true);

        // Never switch desktops for the user, and never pick for them: ask, or say how to choose.
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine($"{task} is open in a window on another desktop. Run again with --bring to move that window here, or --new to open another window here.");
            return 1;
        }
        Console.Write($"{task} is open in a window on another desktop. [b] bring that window here, [n] open another window here, anything else cancels: ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        var picked = answer is "b" or "bring" ? "bring" : answer is "n" or "new" ? "new" : null;
        if (picked is null)
        {
            Console.WriteLine("cancelled");
            return 0;
        }
        return Report(InstanceChannel.Send(profile, "open-task", $"{task} {picked}", TimeSpan.FromSeconds(30)), print: true);
    }

    private static bool StartHost(string? argument)
    {
        // OCHRE_HOST_EXE lets a dev checkout point at its own build; installed, ochre.exe sits beside this file.
        var exe = Environment.GetEnvironmentVariable("OCHRE_HOST_EXE") is { Length: > 0 } fromEnv ? fromEnv : Path.Combine(AppContext.BaseDirectory, "ochre.exe");
        if (!File.Exists(exe))
        {
            Console.Error.WriteLine($"Ochre is not running and {exe} was not found.");
            return false;
        }
        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        if (argument is not null) start.ArgumentList.Add(argument);
        Process.Start(start)?.Dispose();
        return true;
    }

    private static int Quit(OchreProfile profile)
    {
        if (!InstanceChannel.IsRunning(profile))
        {
            Console.WriteLine("not running");
            return 0;
        }
        var code = Send(profile, "quit", null);
        if (code != 0) return code;
        var deadline = Stopwatch.StartNew();
        while (InstanceChannel.IsRunning(profile))
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(15))
            {
                Console.Error.WriteLine("Ochre was asked to quit but is still running.");
                return 1;
            }
            Thread.Sleep(100);
        }
        Console.WriteLine("quit");
        return 0;
    }

    private static int Send(OchreProfile profile, string command, string? argument, bool print = false)
    {
        if (!InstanceChannel.IsRunning(profile))
        {
            Console.Error.WriteLine($"Ochre is not running (profile {Describe(profile)}).");
            return NotRunning;
        }
        return Report(InstanceChannel.Send(profile, command, argument, TimeSpan.FromSeconds(30)), print);
    }

    private static int Report(string? reply, bool print)
    {
        if (reply is null)
        {
            Console.Error.WriteLine("Ochre is running but did not answer.");
            return 1;
        }
        if (reply.StartsWith("error:", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(reply);
            return 1;
        }
        if (print) Console.WriteLine(reply);
        return 0;
    }

    private static string Describe(OchreProfile profile) => profile.IsDefault ? "default" : profile.Name;
}
