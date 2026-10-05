using System.Text.Json.Serialization;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Infrastructure.Settings;

/// <summary>
/// 設定ファイル用の System.Text.Json ソース生成コンテキスト。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
