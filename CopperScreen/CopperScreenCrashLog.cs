using System.Diagnostics;
using System.Text;
using Avalonia.Threading;

namespace CopperScreen;

internal static class CopperScreenCrashLog
{
	private static readonly object Sync = new object();
	private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);
	private static string? _logPath;
	private static string? _latestLogPath;
	private static DateTimeOffset _lastHeartbeat;
	private static bool _installed;
	private static bool _fatalLogged;

	public static string? CurrentLogPath => _logPath;

	public static void Install(string[] args)
	{
		lock (Sync)
		{
			if (_installed)
			{
				return;
			}

			var logDirectory = GetDefaultLogDirectory();
			Directory.CreateDirectory(logDirectory);
			_logPath = Path.Combine(logDirectory, CreateLogFileName(DateTimeOffset.Now, Environment.ProcessId));
			_latestLogPath = Path.Combine(logDirectory, "latest.log");
			File.WriteAllText(_logPath, string.Empty, Encoding.UTF8);
			File.WriteAllText(_latestLogPath, string.Empty, Encoding.UTF8);
			_installed = true;
		}

		Trace.AutoFlush = true;
		Trace.Listeners.Add(new CrashTraceListener());

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			WriteException("AppDomain.UnhandledException", e.ExceptionObject as Exception, e.ExceptionObject);
		TaskScheduler.UnobservedTaskException += (_, e) =>
			WriteException("TaskScheduler.UnobservedTaskException", e.Exception, e.Exception);
		Dispatcher.UIThread.UnhandledException += (_, e) =>
			WriteException("Dispatcher.UIThread.UnhandledException", e.Exception, e.Exception);

		WriteLine("start", $"CopperScreen started. PID={Environment.ProcessId}, Args={FormatArgs(args)}");
		WriteLine("start", $"BaseDirectory={AppContext.BaseDirectory}");
		WriteLine("start", $"LogPath={_logPath}");
	}

	public static void Shutdown()
	{
		WriteLine("stop", _fatalLogged
			? "CopperScreen process is exiting after a fatal condition."
			: "CopperScreen process is exiting normally.");
	}

	public static void WriteException(string source, Exception? exception, object? rawException)
	{
		_fatalLogged = true;
		var builder = new StringBuilder();
		if (exception != null)
		{
			builder.Append(exception);
		}
		else if (rawException != null)
		{
			builder.Append(rawException);
		}
		else
		{
			builder.Append("Unknown exception object.");
		}

		WriteLine("fatal", $"{source}: {builder}");
	}

	public static void Heartbeat(string state)
		=> Heartbeat(() => state);

	public static void Heartbeat(Func<string> stateFactory)
	{
		var now = DateTimeOffset.Now;
		if (now - _lastHeartbeat < HeartbeatInterval)
		{
			return;
		}

		_lastHeartbeat = now;
		WriteLine("state", stateFactory());
	}

	internal static string GetDefaultLogDirectory()
	{
		var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(localAppData))
		{
			localAppData = AppContext.BaseDirectory;
		}

		return Path.Combine(localAppData, "CopperScreen", "Logs");
	}

	internal static string CreateLogFileName(DateTimeOffset timestamp, int processId)
	{
		return $"CopperScreen-{timestamp:yyyyMMdd-HHmmss}-{processId}.log";
	}

	private static void WriteLine(string category, string message)
	{
		var line = $"{DateTimeOffset.Now:O} [{category}] {message}{Environment.NewLine}";
		lock (Sync)
		{
			if (!_installed || _logPath == null)
			{
				return;
			}

			File.AppendAllText(_logPath, line, Encoding.UTF8);
			if (_latestLogPath != null)
			{
				File.AppendAllText(_latestLogPath, line, Encoding.UTF8);
			}
		}
	}

	private static string FormatArgs(string[]? args)
	{
		if (args == null || args.Length == 0)
		{
			return "<none>";
		}

		return string.Join(" ", args.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument));
	}

	private sealed class CrashTraceListener : TraceListener
	{
		public override void Write(string? message)
		{
			if (!string.IsNullOrEmpty(message))
			{
				CopperScreenCrashLog.WriteLine("trace", message);
			}
		}

		public override void WriteLine(string? message)
		{
			if (!string.IsNullOrEmpty(message))
			{
				CopperScreenCrashLog.WriteLine("trace", message);
			}
		}
	}
}
