// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.App.ViewModels.Properties;

namespace Wilds.App.ViewModels.Previews
{
	public sealed partial class MarkdownPreviewViewModel : BasePreviewModel
	{
		private string textValue;
		public string TextValue
		{
			get => textValue;
			private set => SetProperty(ref textValue, value);
		}

		public MarkdownPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var text = await ReadFileAsTextAsync(Item.ItemFile);
			TextValue = text.Left(Constants.PreviewPane.TextCharacterLimit);

			return [];
		}
	}
}
