// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Data.Items
{
	public record BranchItem(string Name, bool IsHead, bool IsRemote, int? AheadBy, int? BehindBy);
}
