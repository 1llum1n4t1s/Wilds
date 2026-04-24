// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Wilds.Shared.Helpers;

/// <summary>
/// 任意のスレッドから <see cref="Trigger"/> で呼べる非同期デバウンサ。
/// 最後のトリガから <see cref="Interval"/> 経過後に一度だけコールバックを実行する。
/// </summary>
/// <remarks>
/// Why: CommunityToolkit の <c>DispatcherQueueTimer.Debounce</c> は UI スレッド専用。
/// FileSystemWatcher のコールバックや ViewModel の setter など、
/// バックグラウンドスレッドからトリガを受けたい場面で使う。
///
/// Why (rere P1 #31 + 部分的 #23): 元 <c>Wilds.App.Helpers</c> から <c>Wilds.Shared.Helpers</c> に
/// 移動して WinUI 非依存にし単体テスト可能化。<c>App.Logger</c> の static fallback を排除し、
/// ロガーは明示的にコンストラクタで注入する設計に変更。
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
		: this(interval, BuildAsyncFromAction(callback), logger)
	{
	}

	// Action overload で先に null をチェックするためのヘルパ。
	// 直接コンストラクタチェーンで `_ => { callback(); ... }` を渡すと lambda 自体は非 null
	// になってしまい、Func overload の ThrowIfNull(callback) を素通りしてしまう。
	private static Func<CancellationToken, Task> BuildAsyncFromAction(Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		return _ => { callback(); return Task.CompletedTask; };
	}

	/// <summary>非同期コールバック用コンストラクタ。</summary>
	/// <param name="logger">
	/// コールバック内部の例外を記録するロガー (任意)。省略時は例外は静かに無視される。
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
		CancellationToken newToken;

		lock (_lock)
		{
			if (_disposed)
				return;

			oldCts = _cts;
			newCts = new CancellationTokenSource();
			// Why: Token のキャプチャをロック内で済ませる。ロック外で `newCts.Token` にアクセスする間に
			// 別スレッドの Trigger() が `oldCts = _cts` で newCts を拾って Dispose してしまう race を回避。
			newToken = newCts.Token;
			_cts = newCts;
		}

		// ロック外でキャンセルだけ。Dispose は意図的に省略する。
		// Why (rere P1 #31 のテストで顕在化した race): 並列 Trigger では
		//   Thread A: 新規 CTS 作成 → ロック解除 → ... 直前の oldCts を Dispose
		//   Thread B: A の newCts を oldCts として拾い → Dispose
		//   Thread A: RunAfterDelayAsync(newCts.Token) で disposed CTS にアクセス → ObjectDisposedException
		// 対策として oldCts.Dispose() を省略。CTS はマネージドリソースのみ保持しているので
		// GC 任せでメモリ的にも問題ない。Cancel() だけ呼んで保留中タスクを解除する。
		oldCts.Cancel();

		// fire-and-forget。例外は内部で握って Logger に吐く (logger 未注入なら silent)。
		_ = RunAfterDelayAsync(newToken);
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
		// Trigger と同様に Cancel のみ。Dispose は GC 任せ。
		toCancel.Cancel();
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
			_logger?.LogWarning(ex, "AsyncDebouncer callback threw an exception.");
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
