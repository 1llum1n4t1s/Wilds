// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Data.Contexts
{
	interface ITagsContext : INotifyPropertyChanged
	{
		IEnumerable<(string path, bool isFolder)> TaggedItems { get; }
	}
}
