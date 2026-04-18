// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.App.Controls;

namespace Wilds.App.Data.Models
{
	internal record OmnibarPathModeSuggestionModel(string Path, string DisplayName) : IOmnibarTextMemberPathProvider
	{
		public string GetTextMemberPath(string textMemberPath)
		{
			return textMemberPath switch
			{
				nameof(Path) => Path,
				nameof(DisplayName) => DisplayName,
				_ => string.Empty
			};
		}
	}
}
