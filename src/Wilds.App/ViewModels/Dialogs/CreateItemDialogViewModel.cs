// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.ViewModels.Dialogs
{
	partial class CreateItemDialogViewModel : ObservableObject
	{
		private bool isNameInvalid;
		public bool IsNameInvalid
		{
			get => isNameInvalid;
			set => SetProperty(ref isNameInvalid, value);
		}
	}
}
