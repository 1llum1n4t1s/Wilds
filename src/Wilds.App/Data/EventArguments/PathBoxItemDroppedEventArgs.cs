// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.Shared.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace Wilds.App.Data.EventArguments
{
	public sealed class PathBoxItemDroppedEventArgs
	{
		public DataPackageView Package { get; set; }

		public string Path { get; set; }

		public DataPackageOperation AcceptedOperation { get; set; }

		public AsyncManualResetEvent SignalEvent { get; set; }
	}
}
