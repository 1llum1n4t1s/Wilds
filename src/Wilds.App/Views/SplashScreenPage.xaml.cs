// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wilds.App.Views
{
	/// <summary>
	/// Display the app splash screen.
	/// </summary>
	public sealed partial class SplashScreenPage : Page
	{
		private string BranchLabel =>
			AppLifecycleHelper.AppEnvironment switch
			{
				AppEnvironment.Dev => "Dev",
				_ => string.Empty,
			};

		public SplashScreenPage()
		{
			InitializeComponent();
		}

		private void Image_ImageOpened(object sender, RoutedEventArgs e)
		{
			App.SplashScreenLoadingTCS?.TrySetResult();
		}

		private void Image_ImageFailed(object sender, RoutedEventArgs e)
		{
			App.SplashScreenLoadingTCS?.TrySetResult();
		}
	}
}
