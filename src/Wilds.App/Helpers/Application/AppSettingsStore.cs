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
				// Why (rere P1 #20): 従来は File.Create が先にファイルを 0 バイトに切り詰め、
				// その後 Serialize が例外を投げると 0 バイト残存 → 次回起動時に空設定扱いになっていた。
				// tmp ファイルに書いてから File.Move(overwrite:true) で atomic swap に変更。
				var tmpPath = _filePath + ".tmp";
				try
				{
					var snapshot = new Dictionary<string, object?>(_values);
					using (var stream = SystemIO.File.Create(tmpPath))
					{
						JsonSerializer.Serialize(stream, snapshot);
					}
					// NTFS の File.Move(overwrite=true) は atomic (別 volume では失敗する)
					SystemIO.File.Move(tmpPath, _filePath, overwrite: true);
				}
				catch
				{
					// 永続化失敗はランタイムエラーにしない。tmp ファイルは残存するかもしれないが
					// 次回 Save 成功時に上書きされる。
					try { if (SystemIO.File.Exists(tmpPath)) SystemIO.File.Delete(tmpPath); } catch { }
				}
			}
		}
	}
}
