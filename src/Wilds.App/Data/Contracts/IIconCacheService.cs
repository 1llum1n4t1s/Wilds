// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Data.Contracts
{
	internal interface IIconCacheService
	{
		Task<byte[]?> GetIconAsync(string itemPath, string? extension, bool isFolder);

		void Clear();
	}
}
