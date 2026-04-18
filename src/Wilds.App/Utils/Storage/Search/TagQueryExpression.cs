// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Utils.Storage
{
	public sealed class TagQueryExpression
	{
		public List<List<TagTerm>> OrGroups { get; set; } = new();
	}
}
