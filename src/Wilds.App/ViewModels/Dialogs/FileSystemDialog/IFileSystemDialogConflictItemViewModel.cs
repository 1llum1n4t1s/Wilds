// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.ViewModels.Dialogs.FileSystemDialog
{
	public interface IFileSystemDialogConflictItemViewModel
	{
		string? SourcePath { get; }

		string? DestinationPath { get; }

		string? CustomName { get; }

		FileNameConflictResolveOptionType ConflictResolveOption { get; }
	}
}
