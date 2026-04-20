// Copyright (c) Files Community
// Licensed under the MIT License.

using Cube.FileSystem.SevenZip;
using System.Text;

namespace Wilds.App.Data.Contracts
{
	/// <summary>
	/// Represents a service to manage storage archives, powered by 7zip and its C# wrapper 1llum1n4t1s.Sevenzip (Cube.FileSystem.SevenZip).
	/// </summary>
	public interface IStorageArchiveService
	{
		/// <summary>
		/// Gets the value that indicates whether specified items can be compressed.
		/// </summary>
		bool CanCompress(IReadOnlyList<ListedItem> items);

		/// <summary>
		/// Gets the value that indicates whether specified items can be decompressed.
		/// </summary>
		bool CanDecompress(IReadOnlyList<ListedItem> items);

		/// <summary>
		/// Compresses the specified items.
		/// </summary>
		Task<bool> CompressAsync(ICompressArchiveModel compressionModel);

		/// <summary>
		/// Decompresses the archive file specified by the path to the destination with password and encoding (if applicable).
		/// </summary>
		Task<bool> DecompressAsync(string archiveFilePath, string destinationFolderPath, string password = "", Encoding? encoding = null);

		/// <summary>
		/// Generates the archive file name from item names.
		/// </summary>
		string GenerateArchiveNameFromItems(IReadOnlyList<ListedItem> items);

		/// <summary>
		/// Gets the value that indicates whether the archive file is encrypted.
		/// </summary>
		Task<bool> IsEncryptedAsync(string archiveFilePath);

		/// <summary>
		/// Gets the value that indicates whether the archive file's encoding is undetermined.
		/// </summary>
		Task<bool> IsEncodingUndeterminedAsync(string archiveFilePath);

		/// <summary>
		/// Detect encoding for a zip file whose encoding is undetermined.
		/// </summary>
		Task<Encoding?> DetectEncodingAsync(string archiveFilePath);

		/// <summary>
		/// Gets the <see cref="ArchiveReader"/> instance from the archive file path.
		/// </summary>
		/// <param name="archiveFilePath">The archive file path to generate an instance.</param>
		/// <param name="password">The password to decrypt the archive file if applicable.</param>
		/// <param name="encoding">Optional file-name encoding (for legacy zip).</param>
		/// <returns>An instance of <see cref="ArchiveReader"/> if the specified item is archive; otherwise null.</returns>
		Task<ArchiveReader?> GetArchiveReaderAsync(string archiveFilePath, string password = "", Encoding? encoding = null);
	}
}
