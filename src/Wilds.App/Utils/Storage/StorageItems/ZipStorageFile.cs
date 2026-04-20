// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.Shared.Helpers;
using Cube.FileSystem.SevenZip;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.Win32;
using IO = System.IO;

namespace Wilds.App.Utils.Storage
{
	public sealed partial class ZipStorageFile : BaseStorageFile, IPasswordProtectedItem
	{
		private readonly string containerPath;
		private readonly BaseStorageFile backingFile;

		// Why (P1-1): コンストラクタで ArchiveEntity を受け取ったときに Index をキャッシュすれば、
		// OpenAsync/RenameAsync/DeleteAsync 等で毎回 ArchiveReader を開いて Items 全走査する
		// 必要がなくなる (7 箇所で O(N) アロケーションしていた)。
		private int _cachedIndex = -1;

		public override string Path { get; }
		public override string Name { get; }
		public override string DisplayName => Name;
		public override string ContentType => "application/octet-stream";
		public override string FileType => IO.Path.GetExtension(Name);
		public override string FolderRelativeId => $"0\\{Name}";

		public override string DisplayType
		{
			get
			{
				var itemType = Strings.File.GetLocalizedResource();
				if (Name.Contains('.', StringComparison.Ordinal))
				{
					itemType = FileType.Trim('.') + " " + itemType;
				}
				return itemType;
			}
		}

		public override DateTimeOffset DateCreated { get; }
		public override Windows.Storage.FileAttributes Attributes => Windows.Storage.FileAttributes.Normal | Windows.Storage.FileAttributes.ReadOnly;

		private IStorageItemExtraProperties properties;
		public override IStorageItemExtraProperties Properties => properties ??= new BaseBasicStorageItemExtraProperties(this);

		public StorageCredential Credentials { get; set; } = new();

		public Func<IPasswordProtectedItem, Task<StorageCredential>> PasswordRequestedCallback { get; set; }

		public ZipStorageFile(string path, string containerPath)
		{
			Name = IO.Path.GetFileName(path.TrimEnd('\\', '/'));
			Path = path;
			this.containerPath = containerPath;
		}
		public ZipStorageFile(string path, string containerPath, BaseStorageFile backingFile) : this(path, containerPath)
			=> this.backingFile = backingFile;
		public ZipStorageFile(string path, string containerPath, ArchiveEntity entry) : this(path, containerPath)
		{
			DateCreated = entry.CreationTime == DateTime.MinValue ? DateTimeOffset.MinValue : entry.CreationTime;
			_cachedIndex = entry.Index;
		}
		public ZipStorageFile(string path, string containerPath, ArchiveEntity entry, BaseStorageFile backingFile) : this(path, containerPath, entry)
			=> this.backingFile = backingFile;

		public override IAsyncOperation<StorageFile> ToStorageFileAsync()
			=> StorageFile.CreateStreamedFileAsync(Name, ZipDataStreamingHandler(Path), null);

		public static IAsyncOperation<BaseStorageFile> FromPathAsync(string path)
		{
			if (!FileExtensionHelpers.IsBrowsableZipFile(path, out var ext))
			{
				return Task.FromResult<BaseStorageFile>(null).AsAsyncOperation();
			}
			var marker = path.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
			if (marker is not -1)
			{
				var containerPath = path.Substring(0, marker + ext.Length);
				if (path == containerPath)
				{
					return Task.FromResult<BaseStorageFile>(null).AsAsyncOperation(); // Root
				}
				if (CheckAccess(containerPath))
				{
					return Task.FromResult<BaseStorageFile>(new ZipStorageFile(path, containerPath)).AsAsyncOperation();
				}
			}
			return Task.FromResult<BaseStorageFile>(null).AsAsyncOperation();
		}

		public override bool IsEqual(IStorageItem item) => item?.Path == Path;
		public override bool IsOfType(StorageItemTypes type) => type is StorageItemTypes.File;

		public override IAsyncOperation<BaseStorageFolder> GetParentAsync() => throw new NotSupportedException();
		public override IAsyncOperation<BaseBasicProperties> GetBasicPropertiesAsync() => GetBasicProperties().AsAsyncOperation();

		public override IAsyncOperation<IRandomAccessStream> OpenAsync(FileAccessMode accessMode)
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.Wrap<IRandomAccessStream>(async () =>
			{
				bool rw = accessMode is FileAccessMode.ReadWrite;
				if (Path == containerPath)
				{
					if (backingFile is not null)
					{
						return await backingFile.OpenAsync(accessMode);
					}

					var file = Win32Helper.OpenFileForRead(containerPath, rw);
					return file.IsInvalid ? null : new FileStream(file, rw ? FileAccess.ReadWrite : FileAccess.Read).AsRandomAccessStream();
				}

				if (!rw)
				{
					ArchiveReader reader = await OpenArchiveReaderAsync();
					if (reader is null || reader.Items is null)
						return null;

					var entry = ResolveEntry(reader);
					if (entry is null)
						return null;

					var ms = new MemoryStream();
					await Task.Run(() => reader.Extract(entry.Index, ms));
					ms.Position = 0;
					return new NonSeekableRandomAccessStreamForRead(ms, (ulong)entry.Length)
					{
						DisposeCallback = () => reader.Dispose()
					};
				}

				throw new NotSupportedException("Can't open zip file as RW");
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}
		public override IAsyncOperation<IRandomAccessStream> OpenAsync(FileAccessMode accessMode, StorageOpenOptions options)
			=> OpenAsync(accessMode);

		public override IAsyncOperation<IRandomAccessStreamWithContentType> OpenReadAsync()
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.Wrap<IRandomAccessStreamWithContentType>(async () =>
			{
				if (Path == containerPath)
				{
					if (backingFile is not null)
						return await backingFile.OpenReadAsync();

					var hFile = Win32Helper.OpenFileForRead(containerPath);
					return hFile.IsInvalid ? null : new StreamWithContentType(new FileStream(hFile, FileAccess.Read).AsRandomAccessStream());
				}

				ArchiveReader reader = await OpenArchiveReaderAsync();
				if (reader is null || reader.Items is null)
					return null;

				var entry = ResolveEntry(reader);
				if (entry is null)
					return null;

				var ms = new MemoryStream();
				await Task.Run(() => reader.Extract(entry.Index, ms));
				ms.Position = 0;
				var nsStream = new NonSeekableRandomAccessStreamForRead(ms, (ulong)entry.Length)
				{
					DisposeCallback = () => reader.Dispose()
				};
				return new StreamWithContentType(nsStream);
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}

		public override IAsyncOperation<IInputStream> OpenSequentialReadAsync()
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.Wrap<IInputStream>(async () =>
			{
				if (Path == containerPath)
				{
					if (backingFile is not null)
						return await backingFile.OpenSequentialReadAsync();

					var hFile = Win32Helper.OpenFileForRead(containerPath);
					return hFile.IsInvalid ? null : new FileStream(hFile, FileAccess.Read).AsInputStream();
				}

				ArchiveReader reader = await OpenArchiveReaderAsync();
				if (reader is null || reader.Items is null)
					return null;

				var entry = ResolveEntry(reader);
				if (entry is null)
					return null;

				var ms = new MemoryStream();
				await Task.Run(() => reader.Extract(entry.Index, ms));
				ms.Position = 0;
				return new NonSeekableRandomAccessStreamForRead(ms, (ulong)entry.Length)
				{
					DisposeCallback = () => reader.Dispose()
				};
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}

		public override IAsyncOperation<StorageStreamTransaction> OpenTransactedWriteAsync()
			=> throw new NotSupportedException();
		public override IAsyncOperation<StorageStreamTransaction> OpenTransactedWriteAsync(StorageOpenOptions options)
			=> throw new NotSupportedException();

		public override IAsyncOperation<BaseStorageFile> CopyAsync(IStorageFolder destinationFolder)
			=> CopyAsync(destinationFolder, Name, NameCollisionOption.FailIfExists);
		public override IAsyncOperation<BaseStorageFile> CopyAsync(IStorageFolder destinationFolder, string desiredNewName)
			=> CopyAsync(destinationFolder, desiredNewName, NameCollisionOption.FailIfExists);
		public override IAsyncOperation<BaseStorageFile> CopyAsync(IStorageFolder destinationFolder, string desiredNewName, NameCollisionOption option)
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.Wrap<BaseStorageFile>(async () =>
			{
				using ArchiveReader reader = await OpenArchiveReaderAsync();
				if (reader is null || reader.Items is null)
					return null;

				var entry = ResolveEntry(reader);
				if (entry is null)
					return null;

				var destFolder = destinationFolder.AsBaseStorageFolder();

				if (destFolder is ICreateFileWithStream cwsf)
				{
					var ms = new MemoryStream();
					await Task.Run(() => reader.Extract(entry.Index, ms));
					ms.Position = 0;
					using var inStream = new NonSeekableRandomAccessStreamForRead(ms, (ulong)entry.Length);
					return await cwsf.CreateFileAsync(inStream.AsStreamForRead(), desiredNewName, option.Convert());
				}
				else
				{
					var destFile = await destFolder.CreateFileAsync(desiredNewName, option.Convert());
					await using var outStream = await destFile.OpenStreamForWriteAsync();
					await SafetyExtensions.WrapAsync(() => Task.Run(() => reader.Extract(entry.Index, outStream)), async (_, exception) =>
					{
						await destFile.DeleteAsync();
						throw exception;
					});
					return destFile;
				}
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}
		public override IAsyncAction CopyAndReplaceAsync(IStorageFile fileToReplace)
		{
			return AsyncInfo.Run((cancellationToken) => SafetyExtensions.WrapAsync(async () =>
			{
				using ArchiveReader reader = await OpenArchiveReaderAsync();
				if (reader is null || reader.Items is null)
					return;

				var entry = ResolveEntry(reader);
				if (entry is null)
					return;

				using var hDestFile = fileToReplace.CreateSafeFileHandle(FileAccess.ReadWrite);
				await using (var outStream = new FileStream(hDestFile, FileAccess.Write))
				{
					await Task.Run(() => reader.Extract(entry.Index, outStream));
				}
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}

		public override IAsyncAction MoveAsync(IStorageFolder destinationFolder)
			=> throw new NotSupportedException();
		public override IAsyncAction MoveAsync(IStorageFolder destinationFolder, string desiredNewName)
			=> throw new NotSupportedException();
		public override IAsyncAction MoveAsync(IStorageFolder destinationFolder, string desiredNewName, NameCollisionOption option)
			=> throw new NotSupportedException();
		public override IAsyncAction MoveAndReplaceAsync(IStorageFile fileToReplace)
			=> throw new NotSupportedException();

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
					if (index < 0) return;

					var fileName = IO.Path.GetRelativePath(containerPath, IO.Path.Combine(IO.Path.GetDirectoryName(Path), desiredName));
					var renameMap = new Dictionary<int, string> { [index] = fileName };

					// Why (P0-1/P0-8): atomic rename + DRY 抽出されたヘルパー経由で更新する。
					await ArchiveUpdateHelper.CommitUpdateAsync(
						containerPath,
						backingFile,
						Credentials.Password,
						configureWriter: null,
						renameMap: renameMap);
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
					if (index < 0) return;

					// value=null → Cube 側で削除扱い (UpdatePlan.cs 仕様)
					var renameMap = new Dictionary<int, string> { [index] = null! };

					await ArchiveUpdateHelper.CommitUpdateAsync(
						containerPath,
						backingFile,
						Credentials.Password,
						configureWriter: null,
						renameMap: renameMap);
				}
			}, ((IPasswordProtectedItem)this).RetryWithCredentialsAsync));
		}

		public override IAsyncOperation<StorageItemThumbnail> GetThumbnailAsync(ThumbnailMode mode)
			=> Task.FromResult<StorageItemThumbnail>(null).AsAsyncOperation();
		public override IAsyncOperation<StorageItemThumbnail> GetThumbnailAsync(ThumbnailMode mode, uint requestedSize)
			=> Task.FromResult<StorageItemThumbnail>(null).AsAsyncOperation();
		public override IAsyncOperation<StorageItemThumbnail> GetThumbnailAsync(ThumbnailMode mode, uint requestedSize, ThumbnailOptions options)
			=> Task.FromResult<StorageItemThumbnail>(null).AsAsyncOperation();

		private static bool CheckAccess(string path)
		{
			try
			{
				var hFile = Win32Helper.OpenFileForRead(path);
				if (hFile.IsInvalid)
					return false;

				using (ArchiveReader reader = new ArchiveReader(new FileStream(hFile, FileAccess.Read), leaveOpen: false))
				{
					return reader.Items is not null;
				}
			}
			catch (EncryptionException)
			{
				// Why (P2-2): 暗号化アーカイブは「認識可能 + 後でパスワード要求」として true を返す。
				return true;
			}
			catch (FileNotFoundException) { return false; }
			catch (UnauthorizedAccessException) { return false; }
			catch { return false; }
		}

		private async Task<int> FetchZipIndex()
		{
			// Why (P1-1): キャッシュ済みなら再 Open 不要。
			if (_cachedIndex >= 0) return _cachedIndex;

			using (ArchiveReader reader = await OpenArchiveReaderAsync())
			{
				if (reader is null || reader.Items is null) return -1;
				var entry = ResolveEntry(reader);
				return entry is not null ? entry.Index : -1;
			}
		}

		/// <summary>
		/// 現在の Path に対応する ArchiveEntity を解決する。キャッシュ済み Index があればそれを優先。
		/// </summary>
		private ArchiveEntity? ResolveEntry(ArchiveReader reader)
		{
			if (_cachedIndex >= 0 && _cachedIndex < reader.Items.Count)
				return reader.Items[_cachedIndex];

			var entry = reader.Items.FirstOrDefault(x => System.IO.Path.Combine(containerPath, x.FullName) == Path);
			if (entry is not null) _cachedIndex = entry.Index;
			return entry;
		}

		private async Task<BaseBasicProperties> GetBasicProperties()
		{
			using ArchiveReader reader = await OpenArchiveReaderAsync();
			if (reader is null || reader.Items is null) return null;

			var entry = ResolveEntry(reader);
			return entry is null
				? new BaseBasicProperties()
				: new ZipFileBasicProperties(entry);
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
			return AsyncInfo.Run<Stream>(async (cancellationToken) =>
			{
				bool readWrite = accessMode == FileAccessMode.ReadWrite;
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

		private StreamedFileDataRequestedHandler ZipDataStreamingHandler(string name)
		{
			return async request =>
			{
				try
				{
					using ArchiveReader reader = await OpenArchiveReaderAsync();
					if (reader is null || reader.Items is null)
					{
						request.FailAndClose(StreamedFileFailureMode.CurrentlyUnavailable);
						return;
					}

					var entry = ResolveEntry(reader);
					if (entry is null)
					{
						request.FailAndClose(StreamedFileFailureMode.CurrentlyUnavailable);
					}
					else
					{
						await using (var outStream = request.AsStreamForWrite())
						{
							await Task.Run(() => reader.Extract(entry.Index, outStream));
						}
						request.Dispose();
					}
				}
				catch
				{
					request.FailAndClose(StreamedFileFailureMode.Failed);
				}
			};
		}

		private sealed partial class ZipFileBasicProperties : BaseBasicProperties
		{
			private ArchiveEntity entry;

			public ZipFileBasicProperties(ArchiveEntity entry) => this.entry = entry;

			public override DateTimeOffset DateModified => entry.LastWriteTime == DateTime.MinValue ? DateTimeOffset.MinValue : entry.LastWriteTime;

			public override DateTimeOffset DateCreated => entry.CreationTime == DateTime.MinValue ? DateTimeOffset.MinValue : entry.CreationTime;

			public override ulong Size => (ulong)entry.Length;
		}
	}
}
