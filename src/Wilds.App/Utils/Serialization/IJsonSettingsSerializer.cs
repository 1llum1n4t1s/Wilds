// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Utils.Serialization
{
	internal interface IJsonSettingsSerializer
	{
		string? SerializeToJson(object? obj);

		T? DeserializeFromJson<T>(string json);
	}
}
