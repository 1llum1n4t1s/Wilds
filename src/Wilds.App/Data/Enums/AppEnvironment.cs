// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Data.Enums
{
	/// <summary>
	/// Defines constants that specify application distribution type.
	/// </summary>
	/// <remarks>
	/// Store / Sideload フレーバは廃止済み。Velopack 経由でリリースするため Dev / Stable の 2 値のみ保持する。
	/// </remarks>
	public enum AppEnvironment
	{
		/// <summary>
		/// ローカル開発ビルド。
		/// </summary>
		Dev,

		/// <summary>
		/// Velopack で配布されるリリースビルド。
		/// </summary>
		Stable,
	}
}
