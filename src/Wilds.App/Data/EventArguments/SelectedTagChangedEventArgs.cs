// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Data.EventArguments
{
	public record SelectedTagChangedEventArgs(IEnumerable<(string path, bool isFolder)> Items);
}
