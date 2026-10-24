using System.Text.Json.Serialization;

namespace WallpaperChanger.Infrastructure.History;

/// <summary>
/// 履歴ファイル用の System.Text.Json ソース生成コンテキスト。
/// 手で編集するファイルではなく、切り替えのたびに書き込むため、インデントせずに小さく保つ。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WallpaperHistoryFile))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext
{
}
