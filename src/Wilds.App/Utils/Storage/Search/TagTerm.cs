// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Utils.Storage
{
	public class TagTerm
	{
		public HashSet<string> TagUids { get; set; } = new();

		public bool IsExclude { get; set; }
	}
}
