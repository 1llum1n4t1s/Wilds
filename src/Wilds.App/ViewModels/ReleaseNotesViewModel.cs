// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.ViewModels
{
	public sealed partial class ReleaseNotesViewModel : ObservableObject
	{
		public string BlogPostUrl =>
			Constants.ExternalUrl.ReleaseNotesUrl;

		public ReleaseNotesViewModel()
		{
		}
	}
}
