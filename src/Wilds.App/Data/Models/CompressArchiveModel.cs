// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.App.Utils.Storage.Operations;
using Microsoft.Extensions.Logging;
using Cube.FileSystem.SevenZip;
using System.IO;

namespace Wilds.App.Data.Models
{
	/// <summary>
	/// Provides an archive creation support.
	/// </summary>
	public sealed class CompressArchiveModel : ICompressArchiveModel
	{
		private StatusCenterItemProgressModel _fileSystemProgress;

		private FileSizeCalculator _sizeCalculator;

		private IThreadingService _threadingService = Ioc.Default.GetRequiredService<IThreadingService>();

		// Why (P1-5): per-file コールバックは高頻度で発火するので UI dispatch を間引く。
		private const long MinUiDispatchIntervalMs = 16;
		private long _lastDispatchMs;

		private string ArchiveExtension => FileFormat switch
		{
			ArchiveFormats.Zip => ".zip",
			ArchiveFormats.SevenZip => ".7z",
			ArchiveFormats.Tar => ".tar",
			ArchiveFormats.GZip => ".gz",
			_ => throw new ArgumentOutOfRangeException(nameof(FileFormat)),
		};

		private Format ResolveFormat => FileFormat switch
		{
			ArchiveFormats.Zip => Format.Zip,
			ArchiveFormats.SevenZip => Format.SevenZip,
			ArchiveFormats.Tar => Format.Tar,
			ArchiveFormats.GZip => Format.GZip,
			_ => throw new ArgumentOutOfRangeException(nameof(FileFormat)),
		};

		private Cube.FileSystem.SevenZip.CompressionLevel ResolveCompressionLevel => CompressionLevel switch
		{
			ArchiveCompressionLevels.Ultra => Cube.FileSystem.SevenZip.CompressionLevel.Ultra,
			ArchiveCompressionLevels.High => Cube.FileSystem.SevenZip.CompressionLevel.High,
			ArchiveCompressionLevels.Normal => Cube.FileSystem.SevenZip.CompressionLevel.Normal,
			ArchiveCompressionLevels.Low => Cube.FileSystem.SevenZip.CompressionLevel.Low,
			ArchiveCompressionLevels.Fast => Cube.FileSystem.SevenZip.CompressionLevel.Fast,
			ArchiveCompressionLevels.None => Cube.FileSystem.SevenZip.CompressionLevel.None,
			_ => throw new ArgumentOutOfRangeException(nameof(CompressionLevel)),
		};

		private long ResolveVolumeSize => SplittingSize switch
		{
			ArchiveSplittingSizes.None => 0L,
			ArchiveSplittingSizes.Mo10 => 10 * 1000 * 1000L,
			ArchiveSplittingSizes.Mo100 => 100 * 1000 * 1000L,
			ArchiveSplittingSizes.Mo1024 => 1024 * 1000 * 1000L,
			ArchiveSplittingSizes.Mo2048 => 2048 * 1000 * 1000L,
			ArchiveSplittingSizes.Mo5120 => 5120 * 1000 * 1000L,
			ArchiveSplittingSizes.Fat4092 => 4092 * 1000 * 1000L,
			ArchiveSplittingSizes.Cd650 => 650 * 1000 * 1000L,
			ArchiveSplittingSizes.Cd700 => 700 * 1000 * 1000L,
			ArchiveSplittingSizes.Dvd4480 => 4480 * 1000 * 1000L,
			ArchiveSplittingSizes.Dvd8128 => 8128 * 1000 * 1000L,
			ArchiveSplittingSizes.Bd23040 => 23040 * 1000 * 1000L,
			_ => throw new ArgumentOutOfRangeException(nameof(SplittingSize)),
		};

		private IProgress<StatusCenterItemProgressModel> _Progress;
		private bool _progressBound;

		public IProgress<StatusCenterItemProgressModel> Progress
		{
			get => _Progress;
			set
			{
				// Why (P2-10): Progress は一度だけ束縛するワンタイム setter。
				// 複数回 set されると _fileSystemProgress が差し替わり、進捗ハンドラが race を起こす。
				if (_progressBound)
					throw new InvalidOperationException("Progress は一度しか設定できません。インスタンスを新規に生成してください。");

				_progressBound = true;
				_Progress = value;
				_fileSystemProgress = new(value, false, FileSystemStatusCode.InProgress);
				_fileSystemProgress.Report(0);
			}
		}

		/// <inheritdoc/>
		public string ArchivePath { get; set; }

		/// <inheritdoc/>
		public string Directory { get; init; }

		/// <inheritdoc/>
		public string FileName { get; init; }

		/// <inheritdoc/>
		public string Password { get; init; }

		/// <inheritdoc/>
		public IEnumerable<string> Sources { get; init; }

		/// <inheritdoc/>
		public ArchiveFormats FileFormat { get; init; }

		/// <inheritdoc/>
		public ArchiveCompressionLevels CompressionLevel { get; init; }

		/// <inheritdoc/>
		public ArchiveSplittingSizes SplittingSize { get; init; }

		/// <inheritdoc/>
		public ArchiveDictionarySizes DictionarySize { get; init; }

		/// <inheritdoc/>
		public ArchiveWordSizes WordSize { get; init; }

		/// <inheritdoc/>
		public CancellationToken CancellationToken { get; set; }

		/// <inheritdoc/>
		public int CPUThreads { get; set; }

		public CompressArchiveModel(
			string[] source,
			string directory,
			string fileName,
			int cpuThreads,
			string? password = null,
			ArchiveFormats fileFormat = ArchiveFormats.Zip,
			ArchiveCompressionLevels compressionLevel = ArchiveCompressionLevels.Normal,
			ArchiveSplittingSizes splittingSize = ArchiveSplittingSizes.None,
			ArchiveDictionarySizes dictionarySize = ArchiveDictionarySizes.Auto,
			ArchiveWordSizes wordSize = ArchiveWordSizes.Auto)
		{
			_Progress = new Progress<StatusCenterItemProgressModel>();

			Sources = source;
			Directory = directory;
			FileName = fileName;
			Password = password ?? string.Empty;
			ArchivePath = string.Empty;
			FileFormat = fileFormat;
			CompressionLevel = compressionLevel;
			SplittingSize = splittingSize;
			DictionarySize = dictionarySize;
			WordSize = wordSize;
			CPUThreads = cpuThreads;
		}

		/// <inheritdoc/>
		public string GetArchivePath(string suffix = "")
		{
			return Path.Combine(Directory, $"{FileName}{suffix}{ArchiveExtension}");
		}

		/// <inheritdoc/>
		public async Task<bool> RunCreationAsync()
		{
			string[] sources = Sources.ToArray();

			var customParams = new Dictionary<string, string>
			{
				["mt"] = CPUThreads.ToString(),
			};
			// UTF-8 ファイル名: 7z では付けない (1llum1n4t1s/Wilds issues 参照)
			if (FileFormat != ArchiveFormats.SevenZip)
				customParams["cu"] = "on";

			if (FileFormat is ArchiveFormats.SevenZip)
			{
				var dictParam = GetDictionarySizeParam();
				if (dictParam is not null)
					customParams["d"] = dictParam;

				var wordParam = GetWordSizeParam();
				if (wordParam is not null)
					customParams["fb"] = wordParam;
			}

			// Why (P0-5): Zip 形式で password ありなら明示的に AES-256 を指定する。
			// 既定 (EncryptionMethod.Default) は弱い ZipCrypto にフォールバックする可能性がある。
			var encryptionMethod = (FileFormat == ArchiveFormats.Zip && !string.IsNullOrEmpty(Password))
				? EncryptionMethod.Aes256
				: EncryptionMethod.Default;

			var options = new CompressionOption
			{
				CompressionLevel = ResolveCompressionLevel,
				VolumeSize = FileFormat is ArchiveFormats.SevenZip ? ResolveVolumeSize : 0,
				IncludeEmptyDirectories = true,
				Password = Password ?? string.Empty,
				EncryptionMethod = encryptionMethod,
				CustomParameters = customParams,
				ThreadCount = CPUThreads,
			};

			using var compressor = new ArchiveWriter(ResolveFormat, options);
			compressor.FileCompressing += Compressor_FileCompressionStarted;
			compressor.FileCompressed += Compressor_FileCompressionFinished;

			var cts = new CancellationTokenSource();

			try
			{
				var files = sources.Where(File.Exists).ToArray();
				var directories = sources.Where(SystemIO.Directory.Exists).ToArray();

				_sizeCalculator = new FileSizeCalculator([.. files, .. directories]);
				var sizeTask = _sizeCalculator.ComputeSizeAsync(cts.Token);
				_ = sizeTask.ContinueWith(_ =>
				{
					_fileSystemProgress.TotalSize = _sizeCalculator.Size;
					_fileSystemProgress.ItemsCount = _sizeCalculator.ItemsCount;
					_fileSystemProgress.EnumerationCompleted = true;
					_fileSystemProgress.Report();
				});

				foreach (var directory in directories)
					compressor.Add(directory);

				foreach (var file in files)
					compressor.Add(file);

				var progress = new Progress<Report>(Compressor_Compressing);
				await Task.Run(() => compressor.Save(ArchivePath, progress));

				cts.Cancel();
				return true;
			}
			catch (Exception ex)
			{
				var logger = Ioc.Default.GetRequiredService<ILogger<App>>();
				// Why (P2-12): ex.Message は password を含む可能性があるので型名のみ。
				logger?.LogWarning("Error compressing folder: {ArchivePath} ({ExceptionType})", ArchivePath, ex.GetType().Name);
				cts.Cancel();
				return false;
			}
		}

		private void Compressor_FileCompressionStarted(object? sender, ArchiveFileEventArgs e)
		{
			if (CancellationToken.IsCancellationRequested)
			{
				e.Cancel = true;
				return;
			}

			var fullName = e.Target?.FullName;
			if (!string.IsNullOrEmpty(fullName))
				_sizeCalculator.ForceComputeFileSize(fullName);

			// Why (P1-5): 高頻度 dispatch を 16ms でスロットル。数万ファイルで UI キュー飽和を防ぐ。
			var nowMs = Environment.TickCount64;
			if (nowMs - _lastDispatchMs < MinUiDispatchIntervalMs) return;
			_lastDispatchMs = nowMs;

			_threadingService.ExecuteOnUiThreadAsync(() =>
			{
				_fileSystemProgress.FileName = e.Target?.Name ?? string.Empty;
				_fileSystemProgress.Report();
			});
		}

		private void Compressor_FileCompressionFinished(object? sender, ArchiveFileEventArgs e)
		{
			_fileSystemProgress.AddProcessedItemsCount(1);
			_fileSystemProgress.Report();
		}

		private void Compressor_Compressing(Report r)
		{
			if (_fileSystemProgress.TotalSize > 0 && r.TotalBytes > 0)
				_fileSystemProgress.Report((double)r.Bytes / r.TotalBytes * 100);
		}

		private string? GetDictionarySizeParam() => DictionarySize switch
		{
			ArchiveDictionarySizes.Auto => null,
			ArchiveDictionarySizes.Kb64 => "64k",
			ArchiveDictionarySizes.Kb256 => "256k",
			ArchiveDictionarySizes.Mb1 => "1m",
			ArchiveDictionarySizes.Mb2 => "2m",
			ArchiveDictionarySizes.Mb4 => "4m",
			ArchiveDictionarySizes.Mb8 => "8m",
			ArchiveDictionarySizes.Mb16 => "16m",
			ArchiveDictionarySizes.Mb32 => "32m",
			ArchiveDictionarySizes.Mb64 => "64m",
			ArchiveDictionarySizes.Mb128 => "128m",
			ArchiveDictionarySizes.Mb256 => "256m",
			ArchiveDictionarySizes.Mb512 => "512m",
			ArchiveDictionarySizes.Mb1024 => "1024m",
			_ => null,
		};

		private string? GetWordSizeParam() => WordSize switch
		{
			ArchiveWordSizes.Auto => null,
			ArchiveWordSizes.Fb8 => "8",
			ArchiveWordSizes.Fb16 => "16",
			ArchiveWordSizes.Fb32 => "32",
			ArchiveWordSizes.Fb64 => "64",
			ArchiveWordSizes.Fb128 => "128",
			ArchiveWordSizes.Fb256 => "256",
			ArchiveWordSizes.Fb273 => "273",
			_ => null,
		};
	}
}
