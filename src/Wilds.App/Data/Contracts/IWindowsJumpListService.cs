// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Data.Contracts
{
	public interface IWindowsJumpListService
	{
		Task InitializeAsync();

		Task AddFolderAsync(string path);

		Task RefreshPinnedFoldersAsync();

		Task RemoveFolderAsync(string path);

		Task<IEnumerable<string>> GetFoldersAsync();
	}
}
