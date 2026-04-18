// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Services.DateTimeFormatter
{
	public interface IDateTimeFormatterFactory
	{
		IDateTimeFormatter GetDateTimeFormatter(DateTimeFormats dateTimeFormat);
	}
}
