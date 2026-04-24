// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.Shared;

/// <summary>
/// Wilds.App / Wilds.App.Server 双方から参照する共通定数。
/// </summary>
/// <remarks>
/// Why (rere P2 #32): 旧来は <c>Wilds.App/Helpers/Application/AppInfo.cs</c> の
/// <c>WildsAppInfo.PackageName</c> に "Wilds" 文字列が定義されていたが、
/// COM サーバープロセス (<c>Wilds.App.Server</c>) は <c>Wilds.App</c> を参照できないため
/// パスを別途マジック文字列で組み立てており DRY 違反 + 将来のリブランド漏れリスクが
/// あった。<c>Wilds.Shared</c> に移すことで両プロジェクトから単一の定数を参照可能にする。
/// </remarks>
public static class AppConstants
{
	/// <summary>
	/// アプリ識別子。レジストリキー / セマフォ名 / LocalAppData サブフォルダ名等で使う。
	/// 旧 <see cref="Wilds.App.Helpers.WildsAppInfo.PackageName"/> と同一値。
	/// </summary>
	public const string PackageName = "Wilds";
}
