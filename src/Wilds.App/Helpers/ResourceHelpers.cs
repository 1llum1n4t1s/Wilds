// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Markup;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Wilds.App.Helpers
{
	[MarkupExtensionReturnType(ReturnType = typeof(string))]
	public sealed partial class ResourceString : MarkupExtension
	{
		// Why: Unpackaged (WinAppSDK) では UWP 専用の Windows.ApplicationModel.Resources.ResourceLoader が
		// PRI を解決できず MarkupExtension が即死する。WinAppSDK の ResourceManager 経由に統一する。
		private static readonly ResourceMap? resourcesTree =
			new ResourceManager().MainResourceMap.TryGetSubtree("Resources");

		public string Name { get; set; } = string.Empty;

		protected override object ProvideValue()
			=> resourcesTree?.TryGetValue(Name)?.ValueAsString ?? Name;
	}
}
