// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Concurrent;

namespace Wilds.App.Helpers
{
	/// <summary>
	/// ApplicationData.Current.LocalSettings.Values の最小限の代替。
	/// LocalFolder に JSON で永続化しつつ、読み書きは IDictionary&lt;string, object?&gt; として行う。
	/// 既存コードが使う <see cref="Wilds.Shared.Extensions.LinqExtensions.Get"/> 拡張と互換。
	/// </summary>
	public static class AppSettingsStore
	{
		private static readonly string _filePath = SystemIO.Path.Combine(AppPaths.LocalFolderPath, "local-settings.json");
		private static readonly ConcurrentDictionary<string, object?> _values = Load();
		private static readonly object _writeLock = new();

		public static IDictionary<string, object?> Values => _values;

		private static ConcurrentDictionary<string, object?> Load()
		{
			try
			{
				if (!SystemIO.File.Exists(_filePath))
					return new ConcurrentDictionary<string, object?>();

				using var stream = SystemIO.File.OpenRead(_filePath);
				var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stream);
				if (dict is null)
					return new ConcurrentDictionary<string, object?>();

				// JsonElement を CLR スカラに変換して保持 (LinqExtensions.Get が型マッチできるように)
				var materialized = new ConcurrentDictionary<string, object?>();
				foreach (var kv in dict)
					materialized[kv.Key] = MaterializeJsonElement(kv.Value);
				return materialized;
			}
			catch
			{
				return new ConcurrentDictionary<string, object?>();
			}
		}

		private static object? MaterializeJsonElement(JsonElement element)
		{
			return element.ValueKind switch
			{
				JsonValueKind.String => element.GetString(),
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
				JsonValueKind.Null => null,
				_ => element.GetRawText(),
			};
		}

		public static void Save()
		{
			lock (_writeLock)
			{
				try
				{
					var snapshot = new Dictionary<string, object?>(_values);
					using var stream = SystemIO.File.Create(_filePath);
					JsonSerializer.Serialize(stream, snapshot);
				}
				catch
				{
					// ignore — 永続化失敗はランタイムエラーにしない
				}
			}
		}
	}
}
