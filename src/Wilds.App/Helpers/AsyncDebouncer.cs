// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Wilds.App.Helpers
{
	/// <summary>
	/// 任意のスレッドから <see cref="Trigger"/> で呼べる非同期デバウンサ。
	/// 最後のトリガから <see cref="Interval"/> 経過後に一度だけコールバックを実行する。
	/// </summary>
	/// <remarks>
	/// Why: CommunityToolkit の <c>DispatcherQueueTimer.Debounce</c> は UI スレッド専用。
	/// FileSystemWatcher のコールバックや ViewModel の setter など、
	/// バックグラウンドスレッドからトリガを受けたい場面で使う。
	/// </remarks>
	public sealed class AsyncDebouncer : IDisposable
	{
		private readonly TimeSpan _interval;
		private readonly Func<CancellationToken, Task> _callback;
		private readonly ILogger? _logger;
		private readonly object _lock = new();

		// 保留中デバウンスのキャンセル用。Trigger のたびに差し替える。
		private CancellationTokenSource _cts = new();
		private bool _disposed;

		/// <summary>デバウンス間隔。</summary>
		public TimeSpan Interval => _interval;

		/// <summary>同期コールバック用コンストラクタ。</summary>
		public AsyncDebouncer(TimeSpan interval, Action callback, ILogger? logger = null)
			: this(interval, _ => { callback(); return Task.CompletedTask; }, logger)
		{
		}

		/// <summary>非同期コールバック用コンストラクタ。</summary>
		/// <param name="logger">
		/// コールバック内部の例外を記録するロガー (任意)。省略時は fallback で <see cref="App.Logger"/> を使う。
		/// </param>
		public AsyncDebouncer(TimeSpan interval, Func<CancellationToken, Task> callback, ILogger? logger = null)
		{
			ArgumentNullException.ThrowIfNull(callback);
			_interval = interval;
			_callback = callback;
			_logger = logger;
		}

		/// <summary>
		/// デバウンスをトリガする。前回の保留があればキャンセルして時計をリセット。
		/// 呼び出しはスレッドセーフ。
		/// </summary>
		public void Trigger()
		{
			CancellationTokenSource newCts;
			CancellationTokenSource oldCts;

			lock (_lock)
			{
				if (_disposed)
					return;

				oldCts = _cts;
				newCts = new CancellationTokenSource();
				_cts = newCts;
			}

			// ロック外でキャンセル (コールバックがロックを取る可能性を考慮)
			oldCts.Cancel();
			oldCts.Dispose();

			// fire-and-forget。例外は内部で握って Logger に吐く。
			_ = RunAfterDelayAsync(newCts.Token);
		}

		/// <summary>
		/// 保留中のデバウンスをキャンセル。既に実行中のコールバックは止まらない。
		/// </summary>
		public void Cancel()
		{
			CancellationTokenSource toCancel;
			lock (_lock)
			{
				if (_disposed)
					return;
				toCancel = _cts;
				_cts = new CancellationTokenSource();
			}
			toCancel.Cancel();
			toCancel.Dispose();
		}

		private async Task RunAfterDelayAsync(CancellationToken token)
		{
			try
			{
				await Task.Delay(_interval, token).ConfigureAwait(false);
				await _callback(token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				// 再トリガでキャンセル = 正常系。何もしない。
			}
			catch (Exception ex)
			{
				// 注入された logger を優先、無ければ App.Logger にフォールバック (既存コード慣習)。
				(_logger ?? App.Logger)?.LogWarning(ex, "AsyncDebouncer callback threw an exception.");
			}
		}

		public void Dispose()
		{
			CancellationTokenSource? toCancel;
			lock (_lock)
			{
				if (_disposed)
					return;
				_disposed = true;
				toCancel = _cts;
				_cts = null!;
			}
			toCancel.Cancel();
			toCancel.Dispose();
		}
	}
}
