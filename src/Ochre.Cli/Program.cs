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
            ["close"] => Send(profile, "close", null),
            ["quit"] => Quit(profile),
            ["dev", "eval", var script] => Send(profile, "dev-eval", script, print: true),
            ["dev", "capture", var path] => Send(profile, "dev-capture", Path.GetFullPath(path), print: true),
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
        to.WriteLine("""
            Usage: ochre-cli <command>

              status                 whether Ochre is running, and its version and profile
              open                   open the window, starting Ochre if it isn't running
              close                  close the window; Ochre keeps running in the tray
              quit                   quit Ochre and wait for it to exit

            Dev builds of Ochre also accept:
              dev eval "<js>"        run script in the open window and print the result
              dev capture <file.png> save a picture of the open window

            Inside an agent Ochre started (OCHRE_SESSION and OCHRE_TASK set):
              task current                       the task id this session works on
              task show [--plan]                 title, state, plan, notes and park note
              task check <item> / uncheck <item> tick or untick a plan item, e.g. p3
              task add-step "<text>" [--under <item>]
                                                 add a plan item
              task note "<text>"                 append to the task's notes
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

        // OCHRE_HOST_EXE lets a dev checkout point at its own build; installed, ochre.exe sits beside this file.
        var exe = Environment.GetEnvironmentVariable("OCHRE_HOST_EXE") is { Length: > 0 } fromEnv ? fromEnv : Path.Combine(AppContext.BaseDirectory, "ochre.exe");
        if (!File.Exists(exe))
        {
            Console.Error.WriteLine($"Ochre is not running and {exe} was not found.");
            return NotRunning;
        }
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })?.Dispose();
        Console.WriteLine("starting");
        return 0;
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
        var reply = InstanceChannel.Send(profile, command, argument, TimeSpan.FromSeconds(30));
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
