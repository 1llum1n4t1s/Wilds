// Copyright (c) Files Community
// Licensed under the MIT License.

using System;

namespace Wilds.Shared.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class RegistrySerializableAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
public sealed class RegistryIgnoreAttribute : Attribute { }
