// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Wilds.Shared.Helpers
{
	/// <summary>
	/// 最大エントリ数付きの LRU (Least Recently Used) キャッシュ。
	/// 容量超過時、最後にアクセスされたのが最も古いエントリが自動破棄される。
	/// </summary>
	/// <remarks>
	/// Why: Wilds にはサイズ無制限の <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>
	/// ベースのアイコン / リソースキャッシュが複数箇所に散在しており、長時間稼働や多数フォルダ閲覧で
	/// メモリが漸増する。LRU でバウンドを付けることで安定稼働を担保する。
	///
	/// 実装: <see cref="LinkedList{T}"/> + <see cref="Dictionary{TKey, TValue}"/> の古典的 LRU。
	/// スレッドセーフ性は単一 <see cref="object"/> ロックで担保する (LRU の全操作がほぼ書き込みを伴うため
	/// <see cref="ReaderWriterLockSlim"/> のメリットがない)。
	/// </remarks>
	/// <typeparam name="TKey">キーの型。</typeparam>
	/// <typeparam name="TValue">値の型。</typeparam>
	public sealed class LruCache<TKey, TValue> where TKey : notnull
	{
		private readonly int _capacity;
		private readonly Action<TKey, TValue>? _onEvicted;
		private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
		private readonly LinkedList<Entry> _lruList = new();
		private readonly object _lock = new();

		private readonly struct Entry
		{
			public readonly TKey Key;
			public readonly TValue Value;
			public Entry(TKey key, TValue value) { Key = key; Value = value; }
		}

		/// <summary>
		/// キャッシュを初期化する。
		/// </summary>
		/// <param name="capacity">最大エントリ数。1 以上。</param>
		/// <param name="onEvicted">エントリ破棄時に呼ばれるコールバック (ロック外で実行)。</param>
		/// <param name="comparer">キー比較子。</param>
		public LruCache(int capacity, Action<TKey, TValue>? onEvicted = null, IEqualityComparer<TKey>? comparer = null)
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
			_capacity = capacity;
			_onEvicted = onEvicted;
			_map = new Dictionary<TKey, LinkedListNode<Entry>>(capacity, comparer);
		}

		/// <summary>現在のエントリ数。</summary>
		public int Count
		{
			get { lock (_lock) { return _lruList.Count; } }
		}

		/// <summary>指定キーの値を取得。ヒット時は MRU 位置に昇格。</summary>
		public bool TryGetValue(TKey key, out TValue? value)
		{
			lock (_lock)
			{
				if (_map.TryGetValue(key, out var node))
				{
					// MRU (先頭) に移動
					_lruList.Remove(node);
					_lruList.AddFirst(node);
					value = node.Value.Value;
					return true;
				}
			}
			value = default;
			return false;
		}

		/// <summary>
		/// キーに対応する値が既にあればそれを返し、なければ <paramref name="valueFactory"/> で生成して格納。
		/// </summary>
		/// <remarks>
		/// valueFactory はロック外で呼ばれるため、同一キーに対し複数スレッドが同時に生成する可能性がある。
		/// その場合、後勝ちで最新の生成物を捨てる (ConcurrentDictionary.GetOrAdd と同じセマンティクス)。
		/// </remarks>
		public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
		{
			if (TryGetValue(key, out var existing))
				return existing!;

			// ロック外で factory 実行 (コストの高い処理を lock 内に入れない)
			var newValue = valueFactory(key);

			TValue? evictedValue = default;
			TKey? evictedKey = default;
			bool didEvict = false;
			Action<TKey, TValue>? onEvicted = _onEvicted;

			lock (_lock)
			{
				// ダブルチェック: 競合で既に入ってたらそれを返す
				if (_map.TryGetValue(key, out var raceNode))
				{
					_lruList.Remove(raceNode);
					_lruList.AddFirst(raceNode);
					return raceNode.Value.Value;
				}

				// 容量超過時は LRU を 1 件 evict
				if (_lruList.Count >= _capacity)
				{
					var last = _lruList.Last!;
					_lruList.RemoveLast();
					_map.Remove(last.Value.Key);
					evictedKey = last.Value.Key;
					evictedValue = last.Value.Value;
					didEvict = true;
				}

				var node = _lruList.AddFirst(new Entry(key, newValue));
				_map[key] = node;
			}

			// ロック外で eviction コールバック (再帰キャッシュアクセスのデッドロック回避)
			if (didEvict && onEvicted is not null)
				onEvicted(evictedKey!, evictedValue!);

			return newValue;
		}

		/// <summary>キーと値を追加または更新。容量超過時は LRU を破棄。</summary>
		public void AddOrUpdate(TKey key, TValue value)
		{
			TValue? evictedValue = default;
			TKey? evictedKey = default;
			bool didEvict = false;
			var onEvicted = _onEvicted;

			lock (_lock)
			{
				if (_map.TryGetValue(key, out var existing))
				{
					// 更新: 既存ノード削除
					_lruList.Remove(existing);
					_map.Remove(key);
				}
				else if (_lruList.Count >= _capacity)
				{
					var last = _lruList.Last!;
					_lruList.RemoveLast();
					_map.Remove(last.Value.Key);
					evictedKey = last.Value.Key;
					evictedValue = last.Value.Value;
					didEvict = true;
				}

				var node = _lruList.AddFirst(new Entry(key, value));
				_map[key] = node;
			}

			if (didEvict && onEvicted is not null)
				onEvicted(evictedKey!, evictedValue!);
		}

		/// <summary>全エントリ削除。</summary>
		public void Clear(bool invokeEvictionCallbacks = false)
		{
			List<Entry>? toEvict = null;
			var onEvicted = _onEvicted;

			lock (_lock)
			{
				if (invokeEvictionCallbacks && onEvicted is not null)
					toEvict = new List<Entry>(_lruList);
				_lruList.Clear();
				_map.Clear();
			}

			if (toEvict is not null)
			{
				foreach (var e in toEvict)
					onEvicted!(e.Key, e.Value);
			}
		}
	}
}
