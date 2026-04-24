// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Wilds.Shared.Helpers;

namespace Wilds.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class EditInNotepadAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;

		public string Label
			=> Strings.EditInNotepad.GetLocalizedResource();

		public string Description
			=> Strings.EditInNotepadDescription.GetLocalizedResource();

		public ActionCategory Category
			=> ActionCategory.Open;

		public RichGlyph Glyph
			=> new("\uE70F");

		public bool IsExecutable =>
			context.SelectedItems.Any() &&
			context.PageType != ContentPageTypes.RecycleBin &&
			context.PageType != ContentPageTypes.ZipFolder &&
			context.SelectedItems.All(x => FileExtensionHelpers.IsBatchFile(x.FileExtension) || FileExtensionHelpers.IsAhkFile(x.FileExtension) || FileExtensionHelpers.IsCmdFile(x.FileExtension));

		public EditInNotepadAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();

			context.PropertyChanged += Context_PropertyChanged;
		}

		public Task ExecuteAsync(object? parameter = null)
		{
			// Why (rere P0 #1 根治): PowerShell 経由をやめて notepad.exe 直起動 + ArgumentList に変更。
			// ArgumentList 方式は OS 側で argv を分割するため、パスに `'` / スペース / 特殊文字が
			// 含まれてもインジェクションの余地がない。PowerShell のシングルクォート注入経路を排除。
			foreach (var item in context.SelectedItems)
			{
				try
				{
					var psi = new System.Diagnostics.ProcessStartInfo
					{
						FileName = "notepad.exe",
						UseShellExecute = false,
						CreateNoWindow = false,
					};
					psi.ArgumentList.Add(item.ItemPath);
					System.Diagnostics.Process.Start(psi);
				}
				catch (Exception ex)
				{
					App.Logger?.LogWarning(ex, "Failed to open notepad for {Path}", item.ItemPath);
				}
			}
			return Task.CompletedTask;
		}

		private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.SelectedItems):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}
	}
}
