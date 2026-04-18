// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Extensions
{
	public static class GroupOptionExtensions
	{
		public static bool IsGroupByDate(this GroupOption groupOption)
			=> groupOption is GroupOption.DateModified or GroupOption.DateCreated or GroupOption.DateDeleted;
	}
}
