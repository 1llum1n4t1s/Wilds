// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.Shared.Helpers;
using Microsoft.Extensions.Logging;
using Cube.FileSystem.SevenZip;
using System.IO;
using System.Text;
using UtfUnknown;
using Windows.Storage;
using Windows.Win32;

namespace Wilds.App.Services
{
	/// <inheritdoc cref="IStorageArchiveService"/>
	public class StorageArchiveService : IStorageArchiveService
	{
		private StatusCenterViewModel StatusCenterViewModel { get; } = Ioc.Default.GetRequiredService<StatusCenterViewModel>();
		private IThreadingService ThreadingService { get; } = Ioc.Default.GetRequiredService<IThreadingService>();

		// Why (P1-5/P1-10): per-file コールバックは高頻度で発火するので UI dispatch を 16ms スロットルで間引く。
		private const long MinUiDispatchIntervalMs = 16;

		/// <inheritdoc/>
		public bool CanCompress(IReadOnlyList<ListedItem> items)
		{
			return CanDecompress(items) is false || items.Count > 1;
		}

		/// <inheritdoc/>
		public bool CanDecompress(IReadOnlyList<ListedItem> items)
		{
			return items.Any() &&
				(items.All(x => x.IsArchive) ||
				items.All(x =>
					x.PrimaryItemAttribute == StorageItemTypes.File &&
					FileExtensionHelpers.IsZipFile(x.FileExtension)));
		}

		/// <inheritdoc/>
		public async Task<bool> CompressAsync(ICompressArchiveModel compressionModel)
		{
			var archivePath = compressionModel.GetArchivePath();

			int index = 1;
			while (SystemIO.File.Exists(archivePath) || SystemIO.Directory.Exists(archivePath))
				archivePath = compressionModel.GetArchivePath($" ({++index})");

			compressionModel.ArchivePath = archivePath;

			var banner = StatusCenterHelper.AddCard_Compress(
				compressionModel.Sources,
				archivePath.CreateEnumerable(),
				ReturnResult.InProgress,
				compressionModel.Sources.Count());

			compressionModel.Progress = banner.ProgressEventSource;
			compressionModel.CancellationToken = banner.CancellationToken;

			bool isSuccess = await compressionModel.RunCreationAsync();

			StatusCenterViewModel.RemoveItem(banner);

			if (isSuccess)
			{
				StatusCenterHelper.AddCard_Compress(
					compressionModel.Sources,
					archivePath.CreateEnumerable(),
					ReturnResult.Success,
					compressionModel.Sources.Count());
			}
			else
			{
				PInvoke.DeleteFileFromApp(archivePath);

				StatusCenterHelper.AddCard_Compress(
					compressionModel.Sources,
					archivePath.CreateEnumerable(),
					compressionModel.CancellationToken.IsCancellationRequested
						? ReturnResult.Cancelled
						: ReturnResult.Failed,
					compressionModel.Sources.Count());
			}

			return isSuccess;
		}

		/// <inheritdoc/>
		public async Task<bool> DecompressAsync(string archiveFilePath, string destinationFolderPath, string password = "", Encoding? encoding = null)
		{
			if (string.IsNullOrEmpty(archiveFilePath) || string.IsNullOrEmpty(destinationFolderPath))
				return false;

			using var reader = await GetArchiveReaderAsync(archiveFilePath, password ?? string.Empty, encoding);
			if (reader is null)
				return false;

			var statusCard = StatusCenterHelper.AddCard_Decompress(
				archiveFilePath.CreateEnumerable(),
				destinationFolderPath.CreateEnumerable(),
				ReturnResult.InProgress);

			if (statusCard.CancellationToken.IsCancellationRequested)
				return false;

			// Why (P0-2 Zip Slip): Cube/7z 側の防御に依存せず、展開先がユーザーの指示フォルダを
			// 逸脱するエントリを事前検知する。".."/絶対パス/UNC path を正規化して判定。
			var normalizedDestination = Path.GetFullPath(destinationFolderPath);
			var destinationWithSep = normalizedDestination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			foreach (var entry in reader.Items)
			{
				var name = entry.FullName ?? string.Empty;
				if (string.IsNullOrEmpty(name)) continue;

				// パス区切り統一 + 正規化
				var normalizedName = name.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
				string combined;
				try
				{
					combined = Path.GetFullPath(Path.Combine(normalizedDestination, normalizedName));
				}
				catch
				{
					// ドライブ指定等で Combine が例外を出したら不正と判定
					App.Logger?.LogError("Zip Slip suspicious entry (invalid path): {Entry}", name);
					StatusCenterViewModel.RemoveItem(statusCard);
					StatusCenterHelper.AddCard_Decompress(
						archiveFilePath.CreateEnumerable(),
						destinationFolderPath.CreateEnumerable(),
						ReturnResult.Failed);
					return false;
				}

				if (!combined.StartsWith(destinationWithSep, StringComparison.OrdinalIgnoreCase)
					&& !string.Equals(combined, normalizedDestination, StringComparison.OrdinalIgnoreCase))
				{
					App.Logger?.LogError("Zip Slip suspicious entry rejected: {Entry}", name);
					StatusCenterViewModel.RemoveItem(statusCard);
					StatusCenterHelper.AddCard_Decompress(
						archiveFilePath.CreateEnumerable(),
						destinationFolderPath.CreateEnumerable(),
						ReturnResult.Failed);
					return false;
				}
			}

			// Why (P2-9): 1-pass で file count と total size を集計する (旧実装は 2 回走査していた)。
			long totalSize = 0;
			int fileCount = 0;
			foreach (var e in reader.Items)
			{
				if (!e.IsDirectory)
				{
					fileCount++;
					totalSize += e.Length;
				}
			}

			StatusCenterItemProgressModel fsProgress = new(
				statusCard.ProgressEventSource,
				enumerationCompleted: true,
				FileSystemStatusCode.InProgress,
				fileCount);
			fsProgress.TotalSize = totalSize;
			fsProgress.Report();

			long lastDispatchMs = 0;

			reader.FileExtracting += (s, e) =>
			{
				if (statusCard.CancellationToken.IsCancellationRequested)
					e.Cancel = true;

				if (e.Target is Cube.FileSystem.Entity ent && !ent.IsDirectory)
				{
					var nowMs = Environment.TickCount64;
					if (nowMs - lastDispatchMs < MinUiDispatchIntervalMs) return;
					lastDispatchMs = nowMs;

					ThreadingService.ExecuteOnUiThreadAsync(() =>
					{
						fsProgress.FileName = ent.FullName;
						fsProgress.Report();
					});
				}
			};

			reader.FileExtracted += (s, e) =>
			{
				if (e.Target is Cube.FileSystem.Entity ent && !ent.IsDirectory)
				{
					fsProgress.AddProcessedItemsCount(1);
					fsProgress.Report();
				}
			};

			var progress = new Progress<Report>(r =>
			{
				if (fsProgress.TotalSize > 0 && r.TotalBytes > 0)
					fsProgress.Report((double)r.Bytes / (long)r.TotalBytes * 100);
			});

			bool isSuccess = false;

			try
			{
				await Task.Run(() => reader.Save(destinationFolderPath, progress));

				if (!statusCard.CancellationToken.IsCancellationRequested)
					isSuccess = true;
			}
			catch (EncryptionException)
			{
				// Why (P1-8/P2-12): password 要求エラーは型のみログ。平文は残さない。
				App.Logger?.LogError("Archive requires a valid password.");
				isSuccess = false;
			}
			catch (Exception ex)
			{
				// Why (P2-12): ex.Message が秘密値を含む可能性があるので型名のみ。
				App.Logger?.LogError("Extraction failed: {ExceptionType}", ex.GetType().Name);
				isSuccess = false;
			}
			finally
			{
				StatusCenterViewModel.RemoveItem(statusCard);

				// Why (P1-4): 失敗/キャンセル時は展開途中ファイルをクリーンアップする。
				if (!isSuccess)
				{
					try
					{
						if (Directory.Exists(destinationFolderPath))
						{
							foreach (var entry in reader.Items)
							{
								if (entry.IsDirectory) continue;
								var extracted = Path.Combine(destinationFolderPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
								if (File.Exists(extracted))
								{
									try { File.Delete(extracted); } catch { /* ignore */ }
								}
							}
						}
					}
					catch (Exception cleanupEx)
					{
						App.Logger?.LogWarning("Partial extract cleanup failed: {ExceptionType}", cleanupEx.GetType().Name);
					}
				}

				if (isSuccess)
				{
					StatusCenterHelper.AddCard_Decompress(
						archiveFilePath.CreateEnumerable(),
						destinationFolderPath.CreateEnumerable(),
						ReturnResult.Success);
				}
				else
				{
					StatusCenterHelper.AddCard_Decompress(
						archiveFilePath.CreateEnumerable(),
						destinationFolderPath.CreateEnumerable(),
						statusCard.CancellationToken.IsCancellationRequested
							? ReturnResult.Cancelled
							: ReturnResult.Failed);
				}
			}

			return isSuccess;
		}

		/// <inheritdoc/>
		public string GenerateArchiveNameFromItems(IReadOnlyList<ListedItem> items)
		{
			if (!items.Any())
				return string.Empty;

			return SystemIO.Path.GetFileName(
				items.Count is 1
					? items[0].ItemPath
					: SystemIO.Path.GetDirectoryName(items[0].ItemPath))
				?? string.Empty;
		}

		/// <inheritdoc/>
		public async Task<bool> IsEncryptedAsync(string archiveFilePath)
		{
			using ArchiveReader? reader = await GetArchiveReaderAsync(archiveFilePath);
			if (reader is null)
				return true;

			return reader.Items.Any(file => file.Encrypted);
		}

		/// <inheritdoc/>
		public async Task<bool> IsEncodingUndeterminedAsync(string archiveFilePath)
		{
			if (archiveFilePath is null) return false;
			if (Path.GetExtension(archiveFilePath) != ".zip") return false;
			try
			{
				// Why: 同期処理なので BG スレッドへ退避する (UI から呼ばれると凍結するため)。
				return await Task.Run(() =>
				{
					using var reader = new ArchiveReader(archiveFilePath);
					return !reader.Items.All(entry => entry.IsUnicodeText);
				});
			}
			catch (Exception ex)
			{
				App.Logger?.LogError("Encoding check failed: {ExceptionType}", ex.GetType().Name);
				return true;
			}
		}

		/// <inheritdoc/>
		public async Task<Encoding?> DetectEncodingAsync(string archiveFilePath)
		{
			// cp437 は全バイトを文字として保持できるため、生バイト列の回収に使う。
			var cp437 = Encoding.GetEncoding(437);
			try
			{
				return await Task.Run(() =>
				{
					using var reader = new ArchiveReader(archiveFilePath, string.Empty, new ArchiveOption { Encoding = cp437 });

					// Why (P2-17): "\n" 結合は改行入りのエントリ名で CharsetDetector の統計を汚染する。
					// 改行類は空白に置換し、セパレータは非印字の NUL にする。
					var sanitized = reader.Items
						.Where(e => !e.IsUnicodeText)
						.Select(e => (e.FullName ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Replace('\0', ' '));
					var fileNameBytes = cp437.GetBytes(string.Join("\0", sanitized));

					var detectionResult = CharsetDetector.DetectFromBytes(fileNameBytes);
					if (detectionResult.Detected != null && detectionResult.Detected.Confidence > 0.5)
						return detectionResult.Detected.Encoding;
					return null;
				});
			}
			catch (Exception ex)
			{
				App.Logger?.LogError("Encoding detection failed: {ExceptionType}", ex.GetType().Name);
				return null;
			}
		}

		/// <inheritdoc/>
		public async Task<ArchiveReader?> GetArchiveReaderAsync(string archiveFilePath, string password = "", Encoding? encoding = null)
		{
			return await FilesystemTasks.Wrap(async () =>
			{
				BaseStorageFile archive = await StorageHelpers.ToStorageItem<BaseStorageFile>(archiveFilePath);
				if (archive is null)
					return null;

				var options = new ArchiveOption { Encoding = encoding };
				var reader = new ArchiveReader(archive.Path, password ?? string.Empty, options);
				return reader.Items is null ? null : reader;
			});
		}
	}
}
