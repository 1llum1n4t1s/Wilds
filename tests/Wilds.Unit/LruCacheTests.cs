// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wilds.Shared.Helpers;

namespace Wilds.Unit;

[TestClass]
public sealed class LruCacheTests
{
	[TestMethod]
	public void TryGetValue_Miss_ReturnsFalse()
	{
		var cache = new LruCache<string, int>(capacity: 4);
		Assert.IsFalse(cache.TryGetValue("missing", out var value));
		Assert.AreEqual(default, value);
	}

	[TestMethod]
	public void AddOrUpdate_NewKey_CountIncrements()
	{
		var cache = new LruCache<string, int>(capacity: 4);
		cache.AddOrUpdate("a", 1);
		cache.AddOrUpdate("b", 2);
		Assert.AreEqual(2, cache.Count);
	}

	[TestMethod]
	public void AddOrUpdate_ExistingKey_DoesNotIncrementCount()
	{
		var cache = new LruCache<string, int>(capacity: 4);
		cache.AddOrUpdate("a", 1);
		cache.AddOrUpdate("a", 99);
		Assert.AreEqual(1, cache.Count);
		Assert.IsTrue(cache.TryGetValue("a", out var v));
		Assert.AreEqual(99, v);
	}

	[TestMethod]
	public void Capacity_Eviction_RemovesLeastRecentlyUsed()
	{
		var cache = new LruCache<string, int>(capacity: 2);
		cache.AddOrUpdate("a", 1);
		cache.AddOrUpdate("b", 2);
		// "a" を access して MRU に移動
		_ = cache.TryGetValue("a", out _);
		// ここで "c" を追加 → LRU は "b"
		cache.AddOrUpdate("c", 3);

		Assert.IsTrue(cache.TryGetValue("a", out var a));
		Assert.AreEqual(1, a);
		Assert.IsFalse(cache.TryGetValue("b", out _), "b should have been evicted");
		Assert.IsTrue(cache.TryGetValue("c", out var c));
		Assert.AreEqual(3, c);
	}

	[TestMethod]
	public void Eviction_InvokesCallbackOnce()
	{
		var evicted = new List<(string, int)>();
		var cache = new LruCache<string, int>(capacity: 2, onEvicted: (k, v) => evicted.Add((k, v)));

		cache.AddOrUpdate("a", 1);
		cache.AddOrUpdate("b", 2);
		cache.AddOrUpdate("c", 3); // evicts "a"

		Assert.AreEqual(1, evicted.Count);
		Assert.AreEqual(("a", 1), evicted[0]);
	}

	[TestMethod]
	public void GetOrAdd_Miss_CallsFactoryOnce()
	{
		var cache = new LruCache<string, int>(capacity: 4);
		int factoryCalls = 0;
		var v1 = cache.GetOrAdd("a", _ => { factoryCalls++; return 42; });
		var v2 = cache.GetOrAdd("a", _ => { factoryCalls++; return 99; });

		Assert.AreEqual(42, v1);
		Assert.AreEqual(42, v2);
		Assert.AreEqual(1, factoryCalls);
	}

	[TestMethod]
	public void GetOrAdd_Race_SecondInsertsAreDiscarded()
	{
		// 並列に同一キーで GetOrAdd しても最終的にキャッシュは 1 エントリ
		var cache = new LruCache<string, int>(capacity: 4);
		int factoryCalls = 0;
		var barrier = new ManualResetEventSlim(false);
		const int threads = 8;
		var results = new int[threads];

		var tasks = new Task[threads];
		for (int i = 0; i < threads; i++)
		{
			int idx = i;
			tasks[idx] = Task.Run(() =>
			{
				barrier.Wait();
				results[idx] = cache.GetOrAdd("k", _ =>
				{
					Interlocked.Increment(ref factoryCalls);
					Thread.SpinWait(1000);
					return idx;
				});
			});
		}

		barrier.Set();
		Task.WaitAll(tasks);

		Assert.AreEqual(1, cache.Count);
		// 全スレッドが同じ値を観測する (後勝ちで最終的に統一)
		var first = results[0];
		Assert.IsTrue(cache.TryGetValue("k", out var stored));
		Assert.AreEqual(stored, first);
		// 注: factoryCalls は 1 とは限らない (race で複数走ることが仕様化されている)
		// ConcurrentDictionary.GetOrAdd と同じセマンティクス
		Assert.IsTrue(factoryCalls >= 1);
	}

	[TestMethod]
	public void Clear_RemovesAll()
	{
		var cache = new LruCache<string, int>(capacity: 4);
		cache.AddOrUpdate("a", 1);
		cache.AddOrUpdate("b", 2);
		cache.Clear();
		Assert.AreEqual(0, cache.Count);
		Assert.IsFalse(cache.TryGetValue("a", out _));
	}

	[TestMethod]
	public void Clear_WithCallbacks_InvokesEachOnce()
	{
		var evicted = new List<(string, int)>();
		var cache = new LruCache<string, int>(capacity: 4, onEvicted: (k, v) => evicted.Add((k, v)));
		cache.AddOrUpdate("a", 1);
		cache.AddOrUpdate("b", 2);
		cache.Clear(invokeEvictionCallbacks: true);
		CollectionAssert.AreEquivalent(new[] { ("a", 1), ("b", 2) }, evicted);
	}

	[TestMethod]
	public void Constructor_RejectsZeroCapacity()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new LruCache<string, int>(capacity: 0));
	}
}
