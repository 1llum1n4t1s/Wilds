// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.ViewModels.Dialogs
{
	public interface IDialog<TViewModel>
		where TViewModel : class, INotifyPropertyChanged
	{
		TViewModel ViewModel { get; set; }

		Task<DialogResult> ShowAsync();

		void Hide();
	}
}
