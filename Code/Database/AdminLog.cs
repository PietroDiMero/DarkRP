using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Log d'une action d'administration (audit trail).
/// </summary>
public sealed class AdminLog
{
	[JsonPropertyName( "id" )]
	public Guid Id { get; set; } = Guid.NewGuid();

	[JsonPropertyName( "admin_steam_id" )]
	public long AdminSteamId { get; set; }

	[JsonPropertyName( "admin_name" )]
	public string AdminName { get; set; } = "";

	[JsonPropertyName( "target_steam_id" )]
	public long? TargetSteamId { get; set; }

	[JsonPropertyName( "target_name" )]
	public string TargetName { get; set; }

	[JsonPropertyName( "action" )]
	public string Action { get; set; } = "";

	[JsonPropertyName( "details" )]
	public string Details { get; set; }

	[JsonPropertyName( "created_at" )]
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	public string FormatTime() => CreatedAt.ToLocalTime().ToString( "dd/MM HH:mm" );
}
