// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Storage;
using Windows.System;

namespace Wilds.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class OpenLogFileLocationAction : IAction
	{
		public string Label
			=> Strings.OpenLogLocation.GetLocalizedResource();

		public string Description
			=> Strings.OpenLogFileLocationDescription.GetLocalizedResource();

		public ActionCategory Category
			=> ActionCategory.Open;

		public HotKey HotKey
			=> new(Keys.OemPeriod, KeyModifiers.CtrlShift);

		public Task ExecuteAsync(object? parameter = null)
		{
			Process.Start(new ProcessStartInfo(AppPaths.LocalFolderPath) { UseShellExecute = true })?.Dispose();
			return Task.CompletedTask;
		}
	}
}
