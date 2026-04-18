// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.App.ViewModels.Properties;
using Microsoft.UI.Xaml.Navigation;

namespace Wilds.App.Views.Properties
{
	public sealed partial class CustomizationPage : BasePropertiesPage
	{
		private CustomizationViewModel CustomizationViewModel { get; set; }

		public CustomizationPage()
		{
			InitializeComponent();
		}

		protected override void OnNavigatedTo(NavigationEventArgs e)
		{
			var parameter = (PropertiesPageNavigationParameter)e.Parameter;

			base.OnNavigatedTo(e);

			CustomizationViewModel = new(AppInstance, BaseProperties, parameter.Window.AppWindow);
		}

		public override async Task<bool> SaveChangesAsync()
			=> await CustomizationViewModel.UpdateIcon();

		public override void Dispose()
		{
		}
	}
}
