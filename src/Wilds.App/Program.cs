// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Wilds.Shared.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.ApplicationModel.Activation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Wilds.App
{
	/// <summary>
	/// Represents the base entry point of the Files app.
	/// </summary>
	/// <remarks>
	/// Gets called at the first time when the app launched or activated.
	/// </remarks>
	internal sealed class Program
	{
		public static Semaphore? Pool { get; set; }

		static Program()
		{
			var pool = new Semaphore(0, 1, $"Wilds-{AppLifecycleHelper.AppEnvironment}-Instance", out var isNew);

			if (!isNew)
			{
				// Resume cached instance
				pool.Release();

				// Redirect to the main process
				var activePid = AppSettingsStore.Values.Get("INSTANCE_ACTIVE", -1);
				var instance = AppInstance.FindOrRegisterForKey(activePid.ToString());

				// Why (rere P0 #3): INSTANCE_ACTIVE は JSON 永続化されており、
				// プロセスの異常終了でも死 PID が残存する。FindOrRegisterForKey は存在しないキーで
				// **新インスタンスを生成して IsCurrent=true を返す** ため、自分自身へのリダイレクト
				// → デッドロック or Exit(0) で即死ループになる。IsCurrent なら自分が主インスタンスと
				// みなして通常起動を続行する。
				if (instance.IsCurrent)
				{
					pool.Dispose();
					return;
				}

				RedirectActivationTo(instance, AppInstance.GetCurrent().GetActivatedEventArgs());

				// Kill the current process
				Environment.Exit(0);
			}

			pool.Dispose();
		}

		/// <summary>
		/// Initializes the process; the entry point of the process.
		/// </summary>
		/// <remarks>
		/// <see cref="Main"/> cannot be declared to be async because this prevents Narrator from reading XAML elements in a WinUI app.
		/// </remarks>
		[STAThread]
		private static void Main()
		{
			// Why: VS 18 F5 や外部起動で console が瞬間終了すると何も見えないため、
			// 未ハンドル例外は %LOCALAPPDATA%\Wilds\startup-crash.log に必ず残す。
			// Why (rere P2 #26): 以前は ex.ToString() フルダンプを書き出していたため、StackTrace 内の
			// パスや引数、特に FluentFTP / LibGit2Sharp の URL (user:pass@host) が漏れる恐れがあった。
			// 型名 + 1 行メッセージのみに縮退。完全な StackTrace は Sentry で送信すれば十分。
			AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
			{
				try
				{
					var path = SystemIO.Path.Combine(AppPaths.LocalFolderPath, "startup-crash.log");
					SystemIO.Directory.CreateDirectory(AppPaths.LocalFolderPath);
					var typeName = e.ExceptionObject?.GetType().FullName ?? "(unknown)";
					var firstLine = (e.ExceptionObject as Exception)?.Message?.Split('\n')[0] ?? string.Empty;
					SystemIO.File.AppendAllText(path,
						$"[{DateTime.Now:O}] IsTerminating={e.IsTerminating} {typeName}: {firstLine}\n");
				}
				catch { /* best-effort */ }
			};
			TaskScheduler.UnobservedTaskException += static (_, e) =>
			{
				try
				{
					var path = SystemIO.Path.Combine(AppPaths.LocalFolderPath, "startup-crash.log");
					SystemIO.Directory.CreateDirectory(AppPaths.LocalFolderPath);
					var typeName = e.Exception?.GetType().FullName ?? "(unknown)";
					var firstLine = e.Exception?.Message?.Split('\n')[0] ?? string.Empty;
					SystemIO.File.AppendAllText(path,
						$"[{DateTime.Now:O}] UnobservedTaskException {typeName}: {firstLine}\n");
				}
				catch { /* best-effort */ }
			};

			// Velopack のインストール/アンインストール/初回起動等の特殊引数を最優先で処理する。
			// 他のどの初期化よりも前に呼ぶ必要がある (Velopack 公式の要件)。
			Velopack.VelopackApp.Build().Run();

			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			WinRT.ComWrappersSupport.InitializeComWrappers();

			// 前インスタンスの Wilds.App.Server が残っている場合は kill する。
			// Unpackaged 配布では EffectivePath の代わりに AppContext.BaseDirectory を使う。
			static bool ProcessPathPredicate(Process p)
			{
				try
				{
					return p.MainModule?.FileName
						.StartsWith(WildsAppInfo.InstalledPath, StringComparison.OrdinalIgnoreCase) ?? false;
				}
				catch
				{
					return false;
				}
			}

			var processes = Process.GetProcessesByName("Wilds")
				.Where(ProcessPathPredicate)
				.Where(p => p.Id != Environment.ProcessId);

			if (!processes.Any())
			{
				foreach (var process in Process.GetProcessesByName("Wilds.App.Server").Where(ProcessPathPredicate))
				{
					try
					{
						process.Kill();
					}
					catch
					{
						// ignore any exceptions
					}
					finally
					{
						process.Dispose();
					}
				}
			}

			var OpenTabInExistingInstance = AppSettingsStore.Values.Get("OpenTabInExistingInstance", true);

			AppActivationArguments activatedArgs;
			try
			{
				activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
			}
			catch (COMException ex) when (ex.HResult == unchecked((int)0x80040154))
			{
				Windows.Win32.PInvoke.MessageBox(
					default,
					Constants.Startup.MissingRuntimeMessage,
					Constants.Startup.MissingRuntimeTitle,
					MESSAGEBOX_STYLE.MB_ICONERROR);

				throw new InvalidOperationException(Constants.Startup.MissingRuntimeMessage, ex);
			}

			var commandLineArgs = GetCommandLineArgs(activatedArgs);

			if (commandLineArgs is not null)
			{
				var parsedCommands = CommandLineParser.ParseUntrustedCommands(commandLineArgs);

				if (parsedCommands is not null)
				{
					foreach (var command in parsedCommands)
					{
						switch (command.Type)
						{
							case ParsedCommandType.ExplorerShellCommand:
								if (!Constants.UserEnvironmentPaths.ShellPlaces.ContainsKey(command.Payload.ToUpperInvariant()))
								{
									OpenShellCommandInExplorer(command.Payload, Environment.ProcessId);
									return;
								}
								break;

							default:
								break;
						}
					}
				}

				// Always open a new instance for OpenDialog, never open new instance for "-Tag" command
				if (parsedCommands is null || !parsedCommands.Any(x => x.Type == ParsedCommandType.OutputPath) &&
					(OpenTabInExistingInstance || parsedCommands.Any(x => x.Type == ParsedCommandType.TagFiles)))
				{
					var activePid = AppSettingsStore.Values.Get("INSTANCE_ACTIVE", -1);
					var instance = AppInstance.FindOrRegisterForKey(activePid.ToString());

					if (!instance.IsCurrent)
					{
						RedirectActivationTo(instance, activatedArgs);
						return;
					}
				}
			}
			else if (activatedArgs.Data is ILaunchActivatedEventArgs tileArgs)
			{
				// Why (rere P1 #15): ILaunchActivatedEventArgs.Arguments は外部アプリ / プロトコル経由で
				// 任意パスが渡せる。従来は「拡張子が実行可能 + File.Exists」だけで実行していたが、
				// それだと任意場所の .bat / .cmd を起動可能。WildsAppInfo.InstalledPath 配下に限定する。
				if (tileArgs.Arguments is not null &&
					FileExtensionHelpers.IsExecutableFile(tileArgs.Arguments) &&
					File.Exists(tileArgs.Arguments))
				{
					var installedPath = WildsAppInfo.InstalledPath;
					var fullTilePath = System.IO.Path.GetFullPath(tileArgs.Arguments);
					var installedFull = System.IO.Path.GetFullPath(installedPath).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
					if (fullTilePath.StartsWith(installedFull, StringComparison.OrdinalIgnoreCase))
					{
						OpenFileFromTile(fullTilePath);
						return;
					}
					App.Logger?.LogWarning("Rejected tile activation outside installed path: {Path}", fullTilePath);
				}
			}

			if (OpenTabInExistingInstance && commandLineArgs is null)
			{
				if (activatedArgs.Data is ILaunchActivatedEventArgs launchArgs)
				{
					var activePid = AppSettingsStore.Values.Get("INSTANCE_ACTIVE", -1);
					var instance = AppInstance.FindOrRegisterForKey(activePid.ToString());
					if (!instance.IsCurrent && !string.IsNullOrWhiteSpace(launchArgs.Arguments))
					{
						RedirectActivationTo(instance, activatedArgs);
						return;
					}
				}
				else if (activatedArgs.Data is IProtocolActivatedEventArgs protocolArgs)
				{
					var parsedArgs = protocolArgs.Uri.Query.TrimStart('?').Split('=');
					if (parsedArgs.Length == 1)
					{
						var activePid = AppSettingsStore.Values.Get("INSTANCE_ACTIVE", -1);
						var instance = AppInstance.FindOrRegisterForKey(activePid.ToString());
						if (!instance.IsCurrent)
						{
							RedirectActivationTo(instance, activatedArgs);
							return;
						}
					}
				}
				else if (activatedArgs.Data is IFileActivatedEventArgs)
				{
					var activePid = AppSettingsStore.Values.Get("INSTANCE_ACTIVE", -1);
					var instance = AppInstance.FindOrRegisterForKey(activePid.ToString());
					if (!instance.IsCurrent)
					{
						RedirectActivationTo(instance, activatedArgs);
						return;
					}
				}
			}

			var currentInstance = AppInstance.FindOrRegisterForKey((-Environment.ProcessId).ToString());
			if (currentInstance.IsCurrent)
				currentInstance.Activated += OnActivated;

			AppSettingsStore.Values["INSTANCE_ACTIVE"] = -Environment.ProcessId;
			AppSettingsStore.Save();

			Application.Start((p) =>
			{
				var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
				SynchronizationContext.SetSynchronizationContext(context);

				_ = new App();
			});
		}

		/// <summary>
		/// Gets command line args from AppActivationArguments
		/// Command line args can be ILaunchActivatedEventArgs, ICommandLineActivatedEventArgs or IProtocolActivatedEventArgs
		/// </summary>
		private static string? GetCommandLineArgs(AppActivationArguments activatedArgs)
		{
			// WINUI3: When launching from commandline the argument is not ICommandLineActivatedEventArgs (#10370)
			var cmdLaunchArgs = activatedArgs.Data is ILaunchActivatedEventArgs launchArgs &&
				launchArgs.Arguments is not null &&
				CommandLineParser.SplitArguments(launchArgs.Arguments, true).FirstOrDefault() is string arg0 &&
				(arg0.EndsWith($"Wilds.exe", StringComparison.OrdinalIgnoreCase) ||
				arg0.EndsWith($"Wilds", StringComparison.OrdinalIgnoreCase)) ? launchArgs.Arguments : null;
			var cmdProtocolArgs = activatedArgs.Data is IProtocolActivatedEventArgs protocolArgs &&
				protocolArgs.Uri.Query.TrimStart('?').Split('=') is string[] parsedArgs &&
				parsedArgs.Length == 2 && parsedArgs[0] == "cmd" ? Uri.UnescapeDataString(parsedArgs[1]) : null;
			var cmdLineArgs = activatedArgs.Data is ICommandLineActivatedEventArgs cmdArgs ? cmdArgs.Operation.Arguments : null;

			return cmdLaunchArgs ?? cmdProtocolArgs ?? cmdLineArgs;
		}

		/// <summary>
		/// Gets invoked when the application is activated.
		/// </summary>
		private static async void OnActivated(object? sender, AppActivationArguments args)
		{
			// WINUI3: Verify if needed or OnLaunched is called
			if (App.Current is App thisApp)
				await thisApp.OnActivatedAsync(args);
		}

		/// <summary>
		/// Redirects the activation to the main process.
		/// </summary>
		public static void RedirectActivationTo(AppInstance keyInstance, AppActivationArguments args)
		{
			keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
		}

		public static void OpenShellCommandInExplorer(string shellCommand, int pid)
		{
			Win32Helper.OpenFolderInExistingShellWindow(shellCommand);
		}

		public static void OpenFileFromTile(string filePath)
		{
			LaunchHelper.LaunchAppAsync(filePath, null, null).Wait();
		}
	}
}
