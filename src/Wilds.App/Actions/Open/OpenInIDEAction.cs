// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Wilds.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class OpenInIDEAction : ObservableObject, IAction
	{
		private readonly IDevToolsSettingsService _devToolsSettingsService;

		private readonly IContentPageContext _context;

		public string Label
			=> string.Format(
				Strings.OpenInIDE.GetLocalizedResource(),
				_devToolsSettingsService.IDEName);

		public string Description
			=> string.Format(
				Strings.OpenInIDEDescription.GetLocalizedResource(),
				_devToolsSettingsService.IDEName);

		public ActionCategory Category
			=> ActionCategory.Open;

		public bool IsExecutable =>
			_context.Folder is not null &&
			!string.IsNullOrWhiteSpace(_devToolsSettingsService.IDEPath);

		public OpenInIDEAction()
		{
			_devToolsSettingsService = Ioc.Default.GetRequiredService<IDevToolsSettingsService>();
			_context = Ioc.Default.GetRequiredService<IContentPageContext>();
			_context.PropertyChanged += Context_PropertyChanged;
			_devToolsSettingsService.PropertyChanged += DevSettings_PropertyChanged;
		}

		public async Task ExecuteAsync(object? parameter = null)
		{
			// Why (rere P0 #1 根治): PowerShell `& 'path' 'arg'` を Process.Start + ArgumentList に置換。
			// WorkingDirectory にパス内 `'` が含まれるケースでもインジェクションしない。
			var idePath = _devToolsSettingsService.IDEPath;
			var workingDirectory = _context.ShellPage?.ShellViewModel.WorkingDirectory;
			bool started = false;
			if (!string.IsNullOrWhiteSpace(idePath) && !string.IsNullOrWhiteSpace(workingDirectory))
			{
				try
				{
					var psi = new System.Diagnostics.ProcessStartInfo
					{
						FileName = idePath,
						UseShellExecute = false,
					};
					psi.ArgumentList.Add(workingDirectory);
					using var proc = System.Diagnostics.Process.Start(psi);
					started = proc is not null;
				}
				catch (Exception ex)
				{
					App.Logger?.LogWarning(ex, "Failed to start IDE {Path}", idePath);
				}
			}

			if (!started)
				await DynamicDialogFactory.ShowFor_IDEErrorDialog(_devToolsSettingsService.IDEName);
		}

		private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(IContentPageContext.Folder))
				OnPropertyChanged(nameof(IsExecutable));
		}

		private void DevSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(IDevToolsSettingsService.IDEPath))
			{
				OnPropertyChanged(nameof(IsExecutable));
			}
			else if (e.PropertyName == nameof(IDevToolsSettingsService.IDEName))
			{
				OnPropertyChanged(nameof(Label));
				OnPropertyChanged(nameof(Description));
			}
		}
	}
}
