// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Utils.Cloud
{
	public interface ICloudDetector
	{
		Task<IEnumerable<ICloudProvider>> DetectCloudProvidersAsync();
	}
}
