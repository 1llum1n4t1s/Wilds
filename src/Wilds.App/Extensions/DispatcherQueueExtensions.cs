using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;

namespace Wilds.App.Extensions
{
	// Window.DispatcherQueue seems to be null sometimes.
	// We don't know why, but as a workaround, we invoke the function directly if DispatcherQueue is null.
	public static class DispatcherQueueExtensions
	{
		public static Task EnqueueOrInvokeAsync(this DispatcherQueue? dispatcher, Func<Task> function, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
		{
			return SafetyExtensions.IgnoreExceptions(() =>
			{
				if (dispatcher is not null)
				{
					try
					{
						return dispatcher.EnqueueAsync(function, priority);
					}
					catch (InvalidOperationException ex)
					{
						if (ex.Message is not "Failed to enqueue the operation")
							throw;
					}
				}

				return function();
			}, App.Logger, typeof(COMException));
		}

		public static Task<T?> EnqueueOrInvokeAsync<T>(this DispatcherQueue? dispatcher, Func<Task<T>> function, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
		{
			return SafetyExtensions.IgnoreExceptions(() =>
			{
				if (dispatcher is not null)
				{
					try
					{
						return dispatcher.EnqueueAsync(function, priority);
					}
					catch (InvalidOperationException ex)
					{
						if (ex.Message is not "Failed to enqueue the operation")
							throw;
					}
				}

				return function();
			}, App.Logger, typeof(COMException));
		}

		public static Task EnqueueOrInvokeAsync(this DispatcherQueue? dispatcher, Action function, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
		{
			return SafetyExtensions.IgnoreExceptions(() =>
			{
				if (dispatcher is not null)
				{
					try
					{
						return dispatcher.EnqueueAsync(function, priority);
					}
					catch (InvalidOperationException ex)
					{
						if (ex.Message is not "Failed to enqueue the operation")
							throw;
					}
				}

				function();
				return Task.CompletedTask;
			}, App.Logger, typeof(COMException));
		}

		public static Task<T?> EnqueueOrInvokeAsync<T>(this DispatcherQueue? dispatcher, Func<T> function, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
		{
			return SafetyExtensions.IgnoreExceptions(() =>
			{
				if (dispatcher is not null)
				{
					try
					{
						return dispatcher.EnqueueAsync(function, priority);
					}
					catch (InvalidOperationException ex)
					{
						if (ex.Message is not "Failed to enqueue the operation")
							throw;
					}
				}

				return Task.FromResult(function());
			}, App.Logger, typeof(COMException));
		}

		/// <summary>
		/// 指定した要素群に対するアクションを <see cref="DispatcherQueue"/> にバッチでエンキューする。
		/// 要素 1 件ごとに UI スレッドへホップする代わりに、<paramref name="chunkSize"/> 件単位で
		/// 1 回のエンキューにまとめることでディスパッチコストを削減する。
		/// </summary>
		/// <remarks>
		/// Why: <c>AsParallel().ForAll(async item =&gt; dispatcher.EnqueueOrInvokeAsync(...))</c> の
		/// ように N アイテム × M プロパティ = N*M 回のホッピングが走っていた箇所を、
		/// ceil(N / chunkSize) 回に集約するための helper。
		/// </remarks>
		/// <typeparam name="T">要素型。</typeparam>
		/// <param name="dispatcher">対象 <see cref="DispatcherQueue"/>。null の場合は同期実行にフォールバック。</param>
		/// <param name="items">処理する要素群。</param>
		/// <param name="action">UI スレッドで実行するアクション。バッチ全体に対して 1 回呼ばれる。</param>
		/// <param name="chunkSize">1 回のエンキューで処理する最大要素数。既定 200。</param>
		/// <param name="priority">エンキュー優先度。既定 <see cref="DispatcherQueuePriority.Normal"/>。</param>
		/// <param name="cancellationToken">キャンセルトークン。</param>
		public static async Task EnqueueBatchAsync<T>(
			this DispatcherQueue? dispatcher,
			IReadOnlyList<T> items,
			Action<IReadOnlyList<T>> action,
			int chunkSize = 200,
			DispatcherQueuePriority priority = DispatcherQueuePriority.Normal,
			CancellationToken cancellationToken = default)
		{
			if (items is null || items.Count == 0)
				return;

			var total = items.Count;
			if (total <= chunkSize)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await dispatcher.EnqueueOrInvokeAsync(() => action(items), priority);
				return;
			}

			for (int offset = 0; offset < total; offset += chunkSize)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var count = Math.Min(chunkSize, total - offset);
				var chunk = new ListSegment<T>(items, offset, count);
				await dispatcher.EnqueueOrInvokeAsync(() => action(chunk), priority);
			}
		}

		/// <summary>
		/// <see cref="IReadOnlyList{T}"/> の範囲ビュー。コピーを生成しない。
		/// </summary>
		private readonly struct ListSegment<T> : IReadOnlyList<T>
		{
			private readonly IReadOnlyList<T> _source;
			private readonly int _offset;

			public int Count { get; }

			public ListSegment(IReadOnlyList<T> source, int offset, int count)
			{
				_source = source;
				_offset = offset;
				Count = count;
			}

			public T this[int index] => _source[_offset + index];

			public IEnumerator<T> GetEnumerator()
			{
				for (int i = 0; i < Count; i++)
					yield return _source[_offset + i];
			}

			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
		}
	}
}
