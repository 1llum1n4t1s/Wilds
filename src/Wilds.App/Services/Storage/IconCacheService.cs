// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using Wilds.Shared.Helpers;

namespace Wilds.App.Services
{
	internal sealed class IconCacheService : IIconCacheService
	{
		// Dummy path to generate generic icons for folders, executables, and shortcuts.
		private static readonly string _dummyPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "x46696c6573");

		// Why (P2 #18): 従来は ConcurrentDictionary で無制限に増加する設計だった。
		// 拡張子は現実的には数百で収束するが、攻撃的な使われ方・長時間稼働での安全弁として
		// LruCache で上限を設ける。512 は upstream Files の他ファイルマネージャより保守的な値。
		private const int CacheCapacity = 512;
		private readonly LruCache<string, byte[]?> _cache = new(CacheCapacity);

		public async Task<byte[]?> GetIconAsync(string itemPath, string? extension, bool isFolder)
		{
			var key = isFolder ? ":folder:" : (extension?.ToLowerInvariant() ?? ":noext:");

			if (_cache.TryGetValue(key, out var cached))
				return cached;

			// Always use the dummy path so the shell resolves the generic type icon from the
			// extension alone. This works correctly for all path types (local, MTP, FTP, network,
			// cloud, etc.) because the cache is keyed by extension anyway, not by item identity.
			var iconPath = isFolder || string.IsNullOrEmpty(extension) ? _dummyPath : _dummyPath + extension;

			var icon = await FileThumbnailHelper.GetIconAsync(
				iconPath,
				Constants.ShellIconSizes.Jumbo,
				isFolder,
				IconOptions.ReturnIconOnly);

			_cache.AddOrUpdate(key, icon);
			return icon;
		}

		public void Clear()
		{
			_cache.Clear();
		}
	}
}
