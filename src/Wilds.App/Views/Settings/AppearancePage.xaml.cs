// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.App.ViewModels.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Wilds.App.Views.Settings
{
	public sealed partial class AppearancePage : Page
	{
		private AppearanceViewModel ViewModel => DataContext as AppearanceViewModel;

		public AppearancePage()
		{
			DataContext = Ioc.Default.GetRequiredService<AppearanceViewModel>();

			InitializeComponent();
		}
	}
}
