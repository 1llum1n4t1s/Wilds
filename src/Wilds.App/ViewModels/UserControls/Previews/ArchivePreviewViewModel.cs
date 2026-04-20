// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.App.ViewModels.Properties;
using Cube.FileSystem.SevenZip;
using System.IO;

namespace Wilds.App.ViewModels.Previews
{
	public sealed partial class ArchivePreviewViewModel : BasePreviewModel
	{
		public ArchivePreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();

			using ArchiveReader zipFile = await FilesystemTasks.Wrap(async () =>
			{
				// Why (P2-18): ItemFile や OpenStreamForReadAsync が null を返す経路で NRE になる。
				// null ガードで安全に null 返却し、呼び出し元 (Wrap) がサムネイルへフォールバックする。
				if (Item?.ItemFile is null) return null;
				var stream = await Item.ItemFile.OpenStreamForReadAsync();
				if (stream is null) return null;

				var arch = new ArchiveReader(stream, leaveOpen: false);
				// Items プロパティアクセスで遅延ロードを強制評価する。
				return arch?.Items is null ? null : arch;
			});

			if (zipFile is null)
			{
				_ = await base.LoadPreviewAndDetailsAsync();
				return details;
			}

			var folderCount = 0;
			var fileCount = 0;
			long totalSize = 0;

			foreach (var entry in zipFile.Items)
			{
				if (!entry.IsDirectory)
				{
					++fileCount;
					totalSize += entry.Length;
				}
			}

			folderCount = zipFile.Items.Count - fileCount;

			string propertyItemCount = Strings.DetailsArchiveItems.GetLocalizedFormatResource((uint)zipFile.Items.Count, fileCount, folderCount);
			details.Add(GetFileProperty("PropertyItemCount", propertyItemCount));
			details.Add(GetFileProperty("PropertyUncompressedSize", ((ulong)totalSize).ToLongSizeString()));

			_ = await base.LoadPreviewAndDetailsAsync();
			return details;
		}
	}
}
