// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.Shared;
using Wilds.Shared.Helpers;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.WinRT;

namespace Wilds.App.Server;

class Program
{
	// Why (rere P2 #32): 旧来は "Wilds" を直書きしており Wilds.App 側の WildsAppInfo.PackageName と
	// 重複していた。Wilds.Shared.AppConstants 経由で単一定義参照に統一。
	// Unpackaged 配布では ApplicationData.Current が使えないので LOCALAPPDATA 直下に出す。
	private static readonly string LogDirectory = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.PackageName, "Local");
	internal static readonly AsyncManualResetEvent ExitSignalEvent = new();
	private static readonly CancellationTokenSource cancellationTokenSource = new();
	private static readonly StreamWriter logWriter = CreateLogWriter();

	private static StreamWriter CreateLogWriter()
	{
		Directory.CreateDirectory(LogDirectory);
		return new StreamWriter(Path.Combine(LogDirectory, "debug_server.log"), append: true) { AutoFlush = true };
	}

	static async Task Main()
	{
		AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

		RO_REGISTRATION_COOKIE cookie = default;

		_ = PInvoke.RoInitialize(RO_INIT_TYPE.RO_INIT_MULTITHREADED);

		var classIds = typeof(Program).Assembly.GetTypes()
			.Where(t => t.IsSealed && t.IsPublic && t.IsClass)
			.Select(t => t.FullName!)
			.Where(name => name.StartsWith("Wilds.App.Server.", StringComparison.Ordinal))
			.Select(name =>
			{
				if (PInvoke.WindowsCreateString(name, (uint)name.Length, out var classId) is HRESULT hr && hr.Value is not 0)
				{
					Marshal.ThrowExceptionForHR(hr);
				}

				return new HSTRING(classId.DangerousGetHandle());
			})
			.ToArray();

		unsafe
		{
			delegate* unmanaged[Stdcall]<HSTRING, IActivationFactory**, HRESULT>[] callbacks = new delegate* unmanaged[Stdcall]<HSTRING, IActivationFactory**, HRESULT>[classIds.Length];
			for (int i = 0; i < callbacks.Length; i++)
			{
				callbacks[i] = &Helpers.GetActivationFactory;
			}

			fixed (delegate* unmanaged[Stdcall]<HSTRING, IActivationFactory**, HRESULT>* pCallbacks = callbacks)
			{
				if (PInvoke.RoRegisterActivationFactories(classIds, pCallbacks, out cookie) is HRESULT hr && hr.Value != 0)
				{
					Marshal.ThrowExceptionForHR(hr);
				}
			}
		}

		foreach (var str in classIds)
		{
			_ = PInvoke.WindowsDeleteString(str);
		}

		AppDomain.CurrentDomain.ProcessExit += (_, _) => cancellationTokenSource.Cancel();

		try
		{
			ExitSignalEvent.Reset();
			await ExitSignalEvent.WaitAsync(cancellationTokenSource.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			if (cookie != 0)
			{
				PInvoke.RoRevokeActivationFactories(cookie);
			}
		}
	}

	private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
	{
		logWriter.WriteLine($"{DateTime.Now}|{e.Exception}");
	}
}
