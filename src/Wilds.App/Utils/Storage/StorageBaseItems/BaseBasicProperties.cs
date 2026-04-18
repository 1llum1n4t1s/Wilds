// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Utils.Storage
{
	public partial class BaseBasicProperties : BaseStorageItemExtraProperties
	{
		public virtual ulong Size
			=> 0;

		public virtual DateTimeOffset DateCreated
			=> DateTimeOffset.Now;

		public virtual DateTimeOffset DateModified
			=> DateTimeOffset.Now;
	}
}
