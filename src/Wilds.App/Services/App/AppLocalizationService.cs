// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Services
{
	internal sealed class LocalizationService : ILocalizationService
	{
		public string LocalizeFromResourceKey(string resourceKey)
		{
			return resourceKey.GetLocalizedResource();
		}
	}
}
