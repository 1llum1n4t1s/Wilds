// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Wilds.Shared.Extensions
{
	public static class FileLoggerExtensions
	{
		public static ILoggerFactory AddFile(this ILoggerFactory factory, string filePath)
		{
			factory.AddProvider(new FileLoggerProvider(filePath));

			return factory;
		}
	}
}
