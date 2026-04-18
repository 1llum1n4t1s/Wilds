// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.App.ViewModels.Properties;
using Windows.Storage.Streams;

namespace Wilds.App.ViewModels.Previews
{
	public sealed partial class RichTextPreviewViewModel : BasePreviewModel
	{
		public IRandomAccessStream Stream { get; set; }

		public RichTextPreviewViewModel(ListedItem item) : base(item) { }

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			Stream = await Item.ItemFile.OpenReadAsync();

			return [];
		}
	}
}
