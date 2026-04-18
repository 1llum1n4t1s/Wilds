// Copyright (c) Files Community
// Licensed under the MIT License.

using Wilds.Shared.Utils;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Wilds.App.Data.Models
{
	/// <inheritdoc cref="IImage"/>
	internal sealed class BitmapImageModel : IImage
	{
		public BitmapImage Image { get; }

		public BitmapImageModel(BitmapImage image)
		{
			Image = image;
		}
	}
}
