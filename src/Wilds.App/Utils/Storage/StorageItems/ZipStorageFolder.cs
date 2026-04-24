// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.Shared.Helpers;
using Cube.FileSystem.SevenZip;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using Windows.Win32;
using IO = System.IO;

namespace Wilds.App.Utils.Storage
{
	public sealed partial class ZipStorageFolder : BaseStorageFolder, ICreateFileWithStream, IPasswordProtectedItem
	{
		private readonly string containerPath;
		private BaseStorageFile backingFile;

		public override string Path { get; }
		public override string Name { get; }
		public override string DisplayName => Name;
		public override string DisplayType => Strings.Folder.GetLocalizedResource();
		public override string FolderRelativeId => $"0\\{Name}";

		public override DateTimeOffset DateCreated { get; }
		public override Windows.Storage.FileAttributes Attributes => Windows.Storage.FileAttributes.Directory;
		public override IStorageItemExtraProperties Properties => new BaseBasicStorageItemExtraProperties(this);

		public StorageCredential Credentials { get; set; } = new();

		public Func<IPasswordProtectedItem, Task<StorageCredential>> PasswordRequestedCallback { get; set; }

		public ZipStorageFolder(string path, string containerPath)
		{
			Name = IO.Path.GetFileName(path.TrimEnd('\\', '/'));
			Path = path;
			this.containerPath = containerPath;
		}
		public ZipStorageFolder(string path, string containerPath, BaseStorageFile backingFile) : this(path, containerPath)
			=> this.backingFile = backingFile;
		public ZipStorageFolder(string path, string containerPath, ArchiveEntity entry) : this(path, containerPath)
			=> DateCreated = entry.CreationTime == DateTime.MinValue ? DateTimeOffset.MinValue : entry.CreationTime;
		public ZipStorageFolder(BaseStorageFile backingFile)
		{
			ArgumentException.ThrowIfNullOrEmpty(backingFile.Path);
			Name = IO.Path.GetFileName(backingFile.Path.TrimEnd('\\', '/'));
			Path = backingFile.Path;
			this.containerPath = backingFile.Path;
			this.backingFile = backingFile;
		}
		public ZipStorageFolder(string path, string containerPath, ArchiveEntity entry, BaseStorageFile backingFile) : this(path, containerPath, entry)
			=> this.backingFile = backingFile;

		public static bool IsZipPath(string path, bool includeRoot = true)
		{
			if (!FileExtensionHelpers.IsBrowsableZipFile(path, out var ext))
				return false;

			var marker = path.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
			if (marker is -1) return false;
			marker += ext.Length;
			// IO.Path.Exists が true ならディレクトリ (".zip" を名前に含む通常ディレクトリ)。
			return (marker == path.Length && includeRoot && !IO.Path.Exists(path + "\\"))
				|| (marker < path.Length && path[marker] is '\\' && !IO.Path.Exists(path));
		}

		public async Task<long> GetUncompressedSize()
		{
			long uncompressedSize = 0;
			using ArchiveReader reader = await FilesystemTasks.Wrap(async () =>
			{
				var arch = await OpenArchiveReaderAsync();
				// Items プロパティアクセスで遅延ロードされるエントリ列を強制評価する。
				return arch?.Items is null ? null : arch;
			});

			if (reader is not null)
			{
				foreach (var info in reader.Items.Where(x => !x.IsDirectory))
				{
					uncompressedSize += info.Length;
				}
			}

			return uncompressedSize;
		}

		private static ConcurrentDictionary<string, Task<bool>> defaultAppDict = new();
		public static async Task<bool> CheckDefaultZipApp(string filePath)
		{
			Func<Task<bool>> queryFileAssoc = async () =>
			{
				var assoc = await Win32Helper.GetDefaultFileAssociationAsync(filePath);
				if (assoc is not null)
				{
					return Constants.Distributions.KnownAppNames.Any(x => assoc.StartsWith(x, StringComparison.OrdinalIgnoreCase))
						|| assoc == WildsAppInfo.FamilyName
						|| assoc.EndsWith("Wilds.exe", StringComparison.OrdinalIgnoreCase)
						|| assoc.Equals(IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), StringComparison.OrdinalIgnoreCase);
				}
				return true;
			};
			var ext = IO.Path.GetExtension(filePath)?.ToLowerInvariant();
			return await defaultAppDict.GetAsync(ext ?? "", queryFileAssoc);
		}

		public static IAsyncOperation<BaseStorageFolder> FromPathAsync(string path)
		{
			if (!FileExtensionHelpers.IsBrowsableZipFile(path, out var ext))
				return Task.FromResult<BaseStorageFolder>(null).AsAsyncOperation();

			var marker = path.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
			if (marker is not -1)
			{
				var containerPath = path.Substring(0, marker + ext.Length);
				if (CheckAccess(containerPath))
				{
					return Task.FromResult((BaseStorageFolder)new ZipStorageFolder(path, containerPath)).AsAsyncOperation();
				}
			}
			return Task.FromResult<BaseStorageFolder>(null).AsAsyncOperation();
		}

		public static IAsyncOperation<BaseStorageFolder> FromStorageFileAsync(BaseStorageFile file)
			=> AsyncInfo.Run<BaseStorageFolder>(async (cancellationToken) => await CheckAccess(file) ? new ZipStorageFolder(file) : null);

		public override IAsyncOperation<StorageFolder> ToStorageFolderAsync() => throw new NotSupportedException();

		public override bool IsEqual(IStorageItem item) => item?.Path == Path;
		public override bool IsOfType(StorageItemTypes type) => type == StorageItemTypes.Folder;

		public override IAsyncOperation<IndexedState> GetIndexedStateAsync() => Task.FromResult(IndexedState.NotIndexed).AsAsyncOperation();

		public override IAsyncOperation<BaseStorageFolder> GetParentAsync() => throw new NotSupportedException();

		private async Task<BaseBasicProperties> GetBasicProperties()
		{
			using ArchiveReader reader = await OpenArchiveReaderAsync();
			if (reader is null || reader.Items is null)
				return new BaseBasicProperties();

			var entry = reader.Items.FirstOrDefault(x => SystemIO.Path.Combine(containerPath, x.FullName) == Path);
			return entry is null
				? new BaseBasicProperties()
				: new ZipFolderBasicProperties(entry);
		}
		public override IAsyncOperation<BaseBasicProperties> GetBasicPropertiesAsync()
		{
			return AsyncInfo.Run(async (cancellationToken) =>
			{
				if (Path == containerPath)
				{
					var zipFile = new SystemStorageFile(await StorageFile.GetFileFromPathAsync(Path));
					return await zipFile.GetBasicPropertiesAsync();
				}
				return await GetBasicProperties();
			});
		}

		public override IAsyncOperation<IStorageItem> GetItemAsync(string name)
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.Wrap<IStorageItem>(async () =>
			{
				using ArchiveReader reader = await OpenArchiveReaderAsync();
				if (reader is null || reader.Items is null) return null;

				var filePath = SystemIO.Path.Combine(Path, name);
				var entry = reader.Items.FirstOrDefault(x => SystemIO.Path.Combine(containerPath, x.FullName) == filePath);
				if (entry is null) return null;

				if (entry.IsDirectory)
				{
					var folder = new ZipStorageFolder(filePath, containerPath, entry, backingFile);
					((IPasswordProtectedItem)folder).CopyFrom(this);
					return folder;
				}

				var file = new ZipStorageFile(filePath, containerPath, entry, backingFile);
				((IPasswordProtectedItem)file).CopyFrom(this);
				return file;
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}

		public override IAsyncOperation<IStorageItem> TryGetItemAsync(string name)
		{
			return AsyncInfo.Run(async (cancellationToken) =>
			{
				try
				{
					return await GetItemAsync(name);
				}
				catch
				{
					return null;
				}
			});
		}
		public override IAsyncOperation<IReadOnlyList<IStorageItem>> GetItemsAsync()
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.Wrap<IReadOnlyList<IStorageItem>>(async () =>
			{
				using ArchiveReader reader = await OpenArchiveReaderAsync();
				if (reader is null || reader.Items is null) return null;

				// Why (P0-9): 既出パス判定を List.Any (O(N)) から Dictionary (O(1)) に切り替え、
				// 総計 O(N²) → O(N) にする。10 万エントリ zip で UI フリーズしていた箇所。
				var items = new Dictionary<string, IStorageItem>(StringComparer.OrdinalIgnoreCase);
				foreach (var entry in reader.Items)
				{
					string winPath = SystemIO.Path.Combine(SystemIO.Path.GetFullPath(containerPath), entry.FullName);
					if (!winPath.StartsWith(Path.WithEnding("\\"), StringComparison.Ordinal))
						continue;

					var split = winPath.Substring(Path.Length).Split('\\', StringSplitOptions.RemoveEmptyEntries);
					if (split.Length == 0) continue;

					if (entry.IsDirectory || split.Length > 1)
					{
						var itemPath = SystemIO.Path.Combine(Path, split[0]);
						if (!items.ContainsKey(itemPath))
						{
							var folder = new ZipStorageFolder(itemPath, containerPath, entry, backingFile);
							((IPasswordProtectedItem)folder).CopyFrom(this);
							items[itemPath] = folder;
						}
					}
					else
					{
						if (!items.ContainsKey(winPath))
						{
							var file = new ZipStorageFile(winPath, containerPath, entry, backingFile);
							((IPasswordProtectedItem)file).CopyFrom(this);
							items[winPath] = file;
						}
					}
				}
				return items.Values.ToList();
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}
		public override IAsyncOperation<IReadOnlyList<IStorageItem>> GetItemsAsync(uint startIndex, uint maxItemsToRetrieve)
			=> AsyncInfo.Run<IReadOnlyList<IStorageItem>>(async (cancellationToken)
				=> (await GetItemsAsync()).Skip((int)startIndex).Take((int)maxItemsToRetrieve).ToList());

		public override IAsyncOperation<BaseStorageFile> GetFileAsync(string name)
			=> AsyncInfo.Run<BaseStorageFile>(async (cancellationToken) => await GetItemAsync(name) as ZipStorageFile);
		public override IAsyncOperation<IReadOnlyList<BaseStorageFile>> GetFilesAsync()
			=> AsyncInfo.Run<IReadOnlyList<BaseStorageFile>>(async (cancellationToken) => (await GetItemsAsync())?.OfType<ZipStorageFile>().ToList());
		public override IAsyncOperation<IReadOnlyList<BaseStorageFile>> GetFilesAsync(CommonFileQuery query)
			=> AsyncInfo.Run(async (cancellationToken) => await GetFilesAsync());
		public override IAsyncOperation<IReadOnlyList<BaseStorageFile>> GetFilesAsync(CommonFileQuery query, uint startIndex, uint maxItemsToRetrieve)
			=> AsyncInfo.Run<IReadOnlyList<BaseStorageFile>>(async (cancellationToken)
				=> (await GetFilesAsync()).Skip((int)startIndex).Take((int)maxItemsToRetrieve).ToList());

		public override IAsyncOperation<BaseStorageFolder> GetFolderAsync(string name)
			=> AsyncInfo.Run<BaseStorageFolder>(async (cancellationToken) => await GetItemAsync(name) as ZipStorageFolder);
		public override IAsyncOperation<IReadOnlyList<BaseStorageFolder>> GetFoldersAsync()
			=> AsyncInfo.Run<IReadOnlyList<BaseStorageFolder>>(async (cancellationToken) => (await GetItemsAsync())?.OfType<ZipStorageFolder>().ToList());
		public override IAsyncOperation<IReadOnlyList<BaseStorageFolder>> GetFoldersAsync(CommonFolderQuery query)
			=> AsyncInfo.Run(async (cancellationToken) => await GetFoldersAsync());
		public override IAsyncOperation<IReadOnlyList<BaseStorageFolder>> GetFoldersAsync(CommonFolderQuery query, uint startIndex, uint maxItemsToRetrieve)
		{
			return AsyncInfo.Run<IReadOnlyList<BaseStorageFolder>>(async (cancellationToken) =>
			{
				var items = await GetFoldersAsync();
				return items.Skip((int)startIndex).Take((int)maxItemsToRetrieve).ToList();
			});
		}

		public override IAsyncOperation<BaseStorageFile> CreateFileAsync(string desiredName)
			=> CreateFileAsync(desiredName, CreationCollisionOption.FailIfExists);
		public override IAsyncOperation<BaseStorageFile> CreateFileAsync(string desiredName, CreationCollisionOption options)
			=> CreateFileAsync(new MemoryStream(), desiredName, options);

		public override IAsyncOperation<BaseStorageFolder> CreateFolderAsync(string desiredName)
			=> CreateFolderAsync(desiredName, CreationCollisionOption.FailIfExists);
		public override IAsyncOperation<BaseStorageFolder> CreateFolderAsync(string desiredName, CreationCollisionOption options)
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.Wrap<BaseStorageFolder>(async () =>
			{
				var zipDesiredName = SystemIO.Path.Combine(Path, desiredName);
				var item = await GetItemAsync(desiredName);
				if (item is not null)
				{
					if (options != CreationCollisionOption.ReplaceExisting)
						return null;
					await item.DeleteAsync();
				}

				var fileName = IO.Path.GetRelativePath(containerPath, zipDesiredName).Replace('\\', '/') + "/";

				// Why (P0-1/P0-8/P1-6): atomic rename + DRY ヘルパー経由で更新する。
				await ArchiveUpdateHelper.CommitUpdateAsync(
					containerPath,
					backingFile,
					Credentials.Password,
					configureWriter: writer => writer.Add(new MemoryStream(), fileName));

				var folder = new ZipStorageFolder(zipDesiredName, containerPath, backingFile);
				((IPasswordProtectedItem)folder).CopyFrom(this);
				return folder;
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}

		public override IAsyncOperation<BaseStorageFolder> MoveAsync(IStorageFolder destinationFolder) => throw new NotSupportedException();
		public override IAsyncOperation<BaseStorageFolder> MoveAsync(IStorageFolder destinationFolder, NameCollisionOption option) => throw new NotSupportedException();

		public override IAsyncAction RenameAsync(string desiredName) => RenameAsync(desiredName, NameCollisionOption.FailIfExists);
		public override IAsyncAction RenameAsync(string desiredName, NameCollisionOption option)
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.WrapAsync(async () =>
			{
				if (Path == containerPath)
				{
					if (backingFile is not null)
					{
						await backingFile.RenameAsync(desiredName, option);
					}
					else
					{
						var fileName = IO.Path.Combine(IO.Path.GetDirectoryName(Path), desiredName);
						PInvoke.MoveFileFromApp(Path, fileName);
					}
				}
				else
				{
					var index = await FetchZipIndex();
					if (index.IsEmpty()) return;

					var folderKey = IO.Path.GetRelativePath(containerPath, Path);
					var folderDes = IO.Path.Combine(IO.Path.GetDirectoryName(folderKey), desiredName);
					var entriesMap = new Dictionary<int, string>(index.Select(x => new KeyValuePair<int, string>(
						x.Index,
						IO.Path.Combine(folderDes, IO.Path.GetRelativePath(folderKey, x.Key)))));

					await ArchiveUpdateHelper.CommitUpdateAsync(
						containerPath, backingFile, Credentials.Password,
						configureWriter: null, renameMap: entriesMap);
				}
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}

		public override IAsyncAction DeleteAsync() => DeleteAsync(StorageDeleteOption.Default);
		public override IAsyncAction DeleteAsync(StorageDeleteOption option)
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.WrapAsync(async () =>
			{
				if (Path == containerPath)
				{
					if (backingFile is not null)
					{
						await backingFile.DeleteAsync();
					}
					else if (option == StorageDeleteOption.PermanentDelete)
					{
						PInvoke.DeleteFileFromApp(Path);
					}
					else
					{
						throw new NotSupportedException("Moving to recycle bin is not supported.");
					}
				}
				else
				{
					var index = await FetchZipIndex();
					if (index.IsEmpty()) return;

					// value=null で Cube 側の削除扱い (UpdatePlan.cs 仕様)
					var entriesMap = new Dictionary<int, string>(index.Select(x => new KeyValuePair<int, string>(x.Index, (string)null)));

					await ArchiveUpdateHelper.CommitUpdateAsync(
						containerPath, backingFile, Credentials.Password,
						configureWriter: null, renameMap: entriesMap);
				}
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}

		public override bool AreQueryOptionsSupported(QueryOptions queryOptions) => false;
		public override bool IsCommonFileQuerySupported(CommonFileQuery query) => false;
		public override bool IsCommonFolderQuerySupported(CommonFolderQuery query) => false;

		public override StorageItemQueryResult CreateItemQuery() => throw new NotSupportedException();
		public override BaseStorageItemQueryResult CreateItemQueryWithOptions(QueryOptions queryOptions) => new(this, queryOptions);

		public override StorageFileQueryResult CreateFileQuery() => throw new NotSupportedException();
		public override StorageFileQueryResult CreateFileQuery(CommonFileQuery query) => throw new NotSupportedException();
		public override BaseStorageFileQueryResult CreateFileQueryWithOptions(QueryOptions queryOptions) => new(this, queryOptions);

		public override StorageFolderQueryResult CreateFolderQuery() => throw new NotSupportedException();
		public override StorageFolderQueryResult CreateFolderQuery(CommonFolderQuery query) => throw new NotSupportedException();
		public override BaseStorageFolderQueryResult CreateFolderQueryWithOptions(QueryOptions queryOptions) => new(this, queryOptions);

		public override IAsyncOperation<StorageItemThumbnail> GetThumbnailAsync(ThumbnailMode mode)
		{
			return AsyncInfo.Run(async (cancellationToken) =>
			{
				if (Path != containerPath) return null;
				var zipFile = await StorageFile.GetFileFromPathAsync(Path);
				return await zipFile.GetThumbnailAsync(mode);
			});
		}
		public override IAsyncOperation<StorageItemThumbnail> GetThumbnailAsync(ThumbnailMode mode, uint requestedSize)
		{
			return AsyncInfo.Run(async (cancellationToken) =>
			{
				if (Path != containerPath) return null;
				var zipFile = await StorageFile.GetFileFromPathAsync(Path);
				return await zipFile.GetThumbnailAsync(mode, requestedSize);
			});
		}
		public override IAsyncOperation<StorageItemThumbnail> GetThumbnailAsync(ThumbnailMode mode, uint requestedSize, ThumbnailOptions options)
		{
			return AsyncInfo.Run(async (cancellationToken) =>
			{
				if (Path != containerPath) return null;
				var zipFile = await StorageFile.GetFileFromPathAsync(Path);
				return await zipFile.GetThumbnailAsync(mode, requestedSize, options);
			});
		}

		private static bool CheckAccess(string path)
		{
			return SafetyExtensions.IgnoreExceptions(() =>
			{
				var hFile = Win32Helper.OpenFileForRead(path);
				if (hFile.IsInvalid) return false;
				using var stream = new FileStream(hFile, FileAccess.Read);
				return CheckAccess(stream);
			});
		}
		private static bool CheckAccess(Stream stream)
		{
			try
			{
				using (ArchiveReader reader = new ArchiveReader(stream, leaveOpen: true))
				{
					return reader.Items is not null;
				}
			}
			catch (EncryptionException)
			{
				// Why (P2-2): 暗号化アーカイブは「認識可能」として true を返す (後続でパスワード要求)。
				return true;
			}
			catch (FileNotFoundException) { return false; }
			catch (UnauthorizedAccessException) { return false; }
			catch { return false; }
		}
		private static async Task<bool> CheckAccess(BaseStorageFile file)
		{
			return await SafetyExtensions.IgnoreExceptions(async () =>
			{
				using var stream = await file.OpenReadAsync();
				return CheckAccess(stream.AsStream());
			});
		}

		public static Task<bool> InitArchive(string path, Format format)
		{
			return SafetyExtensions.IgnoreExceptions(async () =>
			{
				// Why (P1-6): atomic rename で 0 byte ファイル残存を防ぐ。
				await ArchiveUpdateHelper.InitNewArchiveAsync(path, null, format);
				return true;
			});
		}
		public static Task<bool> InitArchive(IStorageFile file, Format format)
		{
			return SafetyExtensions.IgnoreExceptions(async () =>
			{
				var baseFile = file as BaseStorageFile;
				if (baseFile is null && !string.IsNullOrEmpty(file.Path))
					baseFile = await StorageHelpers.ToStorageItem<BaseStorageFile>(file.Path);

				await ArchiveUpdateHelper.InitNewArchiveAsync(file.Path ?? string.Empty, baseFile, format);
				return true;
			});
		}

		private IAsyncOperation<ArchiveReader> OpenArchiveReaderAsync()
		{
			return AsyncInfo.Run<ArchiveReader>(async (cancellationToken) =>
			{
				var stream = await OpenZipFileAsync(FileAccessMode.Read);
				return stream is not null ? new ArchiveReader(stream, Credentials.Password ?? string.Empty, leaveOpen: false) : null;
			});
		}

		private IAsyncOperation<Stream> OpenZipFileAsync(FileAccessMode accessMode)
		{
			return AsyncInfo.Run(async (cancellationToken) =>
			{
				bool readWrite = accessMode is FileAccessMode.ReadWrite;
				if (backingFile is not null)
				{
					return (await backingFile.OpenAsync(accessMode)).AsStream();
				}
				else
				{
					var hFile = Win32Helper.OpenFileForRead(containerPath, readWrite);
					if (hFile.IsInvalid) return null;
					return new FileStream(hFile, readWrite ? FileAccess.ReadWrite : FileAccess.Read);
				}
			});
		}

		private async Task<IEnumerable<(int Index, string Key)>> FetchZipIndex()
		{
			using (ArchiveReader reader = await OpenArchiveReaderAsync())
			{
				if (reader is null || reader.Items is null) return null;
				return reader.Items
					.Where(x => SystemIO.Path.Combine(containerPath, x.FullName).IsSubPathOf(Path))
					.Select(e => (e.Index, e.FullName))
					.ToList();
			}
		}

		public IAsyncOperation<BaseStorageFile> CreateFileAsync(Stream contents, string desiredName)
			=> CreateFileAsync(new MemoryStream(), desiredName, CreationCollisionOption.FailIfExists);

		public IAsyncOperation<BaseStorageFile> CreateFileAsync(Stream contents, string desiredName, CreationCollisionOption options)
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.Wrap<BaseStorageFile>(async () =>
			{
				var zipDesiredName = SystemIO.Path.Combine(Path, desiredName);
				var item = await GetItemAsync(desiredName);
				if (item is not null)
				{
					if (options != CreationCollisionOption.ReplaceExisting) return null;
					await item.DeleteAsync();
				}

				var fileName = IO.Path.GetRelativePath(containerPath, zipDesiredName).Replace('\\', '/');
				var stream = contents ?? new MemoryStream();

				await ArchiveUpdateHelper.CommitUpdateAsync(
					containerPath,
					backingFile,
					Credentials.Password,
					configureWriter: writer => writer.Add(stream, fileName));

				var file = new ZipStorageFile(zipDesiredName, containerPath, backingFile);
				((IPasswordProtectedItem)file).CopyFrom(this);
				return file;
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}

		private sealed partial class ZipFolderBasicProperties : BaseBasicProperties
		{
			private ArchiveEntity entry;

			public ZipFolderBasicProperties(ArchiveEntity entry) => this.entry = entry;

			public override DateTimeOffset DateModified => entry.LastWriteTime == DateTime.MinValue ? DateTimeOffset.MinValue : entry.LastWriteTime;

			public override DateTimeOffset DateCreated => entry.CreationTime == DateTime.MinValue ? DateTimeOffset.MinValue : entry.CreationTime;

			public override ulong Size => (ulong)entry.Length;
		}
	}
}
