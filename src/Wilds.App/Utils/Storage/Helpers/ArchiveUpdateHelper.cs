// Copyright (c) Files Community
// Licensed under the MIT License.

using Cube.FileSystem.SevenZip;
using System.IO;
using Windows.Storage;
using IO = System.IO;

namespace Wilds.App.Utils.Storage
{
	/// <summary>
	/// 既存アーカイブに対する追加・削除・リネーム等の更新操作を atomic に行うヘルパー。
	/// </summary>
	/// <remarks>
	/// Why (P0-1 / P0-8):
	/// 旧実装は `using var ms = new MemoryStream()` でアーカイブ全体をメモリに展開し、
	/// CopyToAsync で書き戻していた。これには 2 つの致命的欠陥があった:
	/// 1. 大容量アーカイブ (1GB+) で OOM / LOH 断片化
	/// 2. CopyToAsync 途中でプロセス異常終了すると元アーカイブが永続破損
	///
	/// 本ヘルパーは以下で両者を解決する:
	/// - 出力は同一ディレクトリ上の一時ファイル (.tmp) に書き出す (RAM はバッファ分のみ)
	/// - 成功したら File.Move(..., overwrite: true) で atomic 置換
	/// - 失敗したら一時ファイルを削除し元ファイルは温存
	///
	/// backingFile 経路 (WinRT StorageFile ベース) は atomic rename API が限定的なので、
	/// 互換維持のため従来通り MemoryStream 経由で書き戻す。
	/// </remarks>
	internal static class ArchiveUpdateHelper
	{
		/// <summary>
		/// 既存アーカイブ (containerPath または backingFile) に対して <paramref name="configureWriter"/> で追加アイテムを設定し、
		/// <paramref name="renameMap"/> で既存エントリのリネーム/削除を適用した上で atomic 書き戻しを行う。
		/// </summary>
		public static async Task CommitUpdateAsync(
			string containerPath,
			BaseStorageFile? backingFile,
			string? password,
			Action<ArchiveWriter>? configureWriter,
			IReadOnlyDictionary<int, string>? renameMap = null,
			CancellationToken cancellationToken = default)
		{
			var sourceStream = await OpenSourceAsync(containerPath, backingFile);
			if (sourceStream is null)
				throw new IOException($"Failed to open archive: {containerPath}");

			string? tmpPath = null;
			Stream? outStream = null;
			bool outDisposed = false;

			try
			{
				await using (sourceStream)
				{
					sourceStream.Position = 0;
					var format = FormatFactory.From(sourceStream);
					sourceStream.Position = 0;

					if (backingFile is null)
					{
						var dir = IO.Path.GetDirectoryName(containerPath) ?? IO.Path.GetTempPath();
						tmpPath = IO.Path.Combine(dir, $"{IO.Path.GetFileName(containerPath)}.{Guid.NewGuid():N}.tmp");
						outStream = new FileStream(tmpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true);
					}
					else
					{
						// backingFile 経路: WinRT 由来の StorageFile は atomic replace が難しいので互換優先で MemoryStream
						outStream = new MemoryStream();
					}

					var options = new CompressionOption
					{
						CustomParameters = new Dictionary<string, string> { ["cu"] = "on" },
						Password = password ?? string.Empty,
					};

					using var writer = new ArchiveWriter(format, options);
					configureWriter?.Invoke(writer);

					// Cube の renameMap は value が null/empty で削除扱い (cf. UpdatePlan.cs)。
					// そのまま渡せば rename と delete の両方に対応できる。
					await Task.Run(() =>
						writer.Update(
							sourceStream, outStream, renameMap!,
							sourcePassword: password,
							progress: null,
							leaveSourceOpen: true,
							leaveDestOpen: true),
						cancellationToken);
				}

				if (backingFile is null)
				{
					// tmp FileStream は close してから rename する (Windows のロック対策)
					await outStream!.FlushAsync(cancellationToken);
					outStream.Dispose();
					outDisposed = true;

					// atomic rename: 同一ドライブなら NTFS 的に atomic
					File.Move(tmpPath!, containerPath, overwrite: true);
					tmpPath = null; // rename 成功なので cleanup 対象から外す
				}
				else
				{
					var ms = (MemoryStream)outStream!;
					ms.Position = 0;
					using var writeHandle = await backingFile.OpenAsync(FileAccessMode.ReadWrite);
					using var writeStream = writeHandle.AsStream();
					writeStream.Position = 0;
					await ms.CopyToAsync(writeStream, cancellationToken);
					await writeStream.FlushAsync(cancellationToken);
					writeStream.SetLength(writeStream.Position);
				}
			}
			finally
			{
				if (!outDisposed)
				{
					try { outStream?.Dispose(); } catch { /* 秘匿: Dispose 失敗は伝搬させない */ }
				}
				if (tmpPath is not null)
				{
					try { File.Delete(tmpPath); } catch { /* 一時ファイル削除失敗は無視 */ }
				}
			}
		}

		/// <summary>
		/// 新規アーカイブを atomic に初期化する (ZipStorageFolder.InitArchive の core)。
		/// Why (P1-6): 旧実装は SetLength(0) 後に Save で例外が出ると 0 byte ファイルが残った。
		/// 本実装は一時ファイルに Save → atomic rename で置換する。
		/// </summary>
		public static async Task InitNewArchiveAsync(
			string containerPath,
			BaseStorageFile? backingFile,
			Format format,
			CancellationToken cancellationToken = default)
		{
			var options = new CompressionOption
			{
				CustomParameters = new Dictionary<string, string> { ["cu"] = "on" },
			};

			if (backingFile is null)
			{
				var dir = IO.Path.GetDirectoryName(containerPath) ?? IO.Path.GetTempPath();
				var tmpPath = IO.Path.Combine(dir, $"{IO.Path.GetFileName(containerPath)}.{Guid.NewGuid():N}.tmp");
				try
				{
					await using (var tmpStream = new FileStream(tmpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
					{
						using var writer = new ArchiveWriter(format, options);
						await Task.Run(() => writer.Save(tmpStream, leaveOpen: true), cancellationToken);
						await tmpStream.FlushAsync(cancellationToken);
					}
					File.Move(tmpPath, containerPath, overwrite: true);
				}
				catch
				{
					try { File.Delete(tmpPath); } catch { }
					throw;
				}
			}
			else
			{
				using var ms = new MemoryStream();
				using (var writer = new ArchiveWriter(format, options))
				{
					await Task.Run(() => writer.Save(ms, leaveOpen: true), cancellationToken);
				}
				ms.Position = 0;
				using var writeHandle = await backingFile.OpenAsync(FileAccessMode.ReadWrite);
				using var writeStream = writeHandle.AsStream();
				writeStream.SetLength(0);
				await ms.CopyToAsync(writeStream, cancellationToken);
				await writeStream.FlushAsync(cancellationToken);
			}
		}

		private static async Task<Stream?> OpenSourceAsync(string containerPath, BaseStorageFile? backingFile)
		{
			if (backingFile is not null)
			{
				var rh = await backingFile.OpenAsync(FileAccessMode.Read);
				return rh.AsStream();
			}
			var hFile = Win32Helper.OpenFileForRead(containerPath);
			return hFile.IsInvalid ? null : new FileStream(hFile, FileAccess.Read);
		}
	}
}
