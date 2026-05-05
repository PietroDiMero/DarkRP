using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Entrée complète de bannissement (SteamId et/ou IP, avec expiration).
/// </summary>
public sealed class BanRecord
{
	[JsonPropertyName( "id" )]
	public Guid Id { get; set; } = Guid.NewGuid();

	// Cibles (au moins un doit être défini)
	[JsonPropertyName( "steam_id" )]
	public long? SteamId { get; set; }

	[JsonPropertyName( "ip" )]
	public string Ip { get; set; }

	[JsonPropertyName( "display_name" )]
	public string DisplayName { get; set; } = "Inconnu";

	// Détails du ban
	[JsonPropertyName( "reason" )]
	public string Reason { get; set; } = "Banni";

	[JsonPropertyName( "admin_steam_id" )]
	public long? AdminSteamId { get; set; }

	[JsonPropertyName( "admin_name" )]
	public string AdminName { get; set; }

	[JsonPropertyName( "created_at" )]
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	/// <summary>null = ban permanent.</summary>
	[JsonPropertyName( "expires_at" )]
	public DateTime? ExpiresAt { get; set; }

	[JsonPropertyName( "is_active" )]
	public bool IsActive
	{
		get
		{
			if ( !_isActive ) return false;
			if ( ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value )
			{
				_isActive = false;
				return false;
			}
			return true;
		}
		set => _isActive = value;
	}

	bool _isActive = true;

	public bool IsPermanent => !ExpiresAt.HasValue;

	public string FormatDuration()
	{
		if ( IsPermanent ) return "Permanent";
		var remaining = ExpiresAt!.Value - DateTime.UtcNow;
		if ( remaining <= TimeSpan.Zero ) return "Expiré";
		if ( remaining.TotalDays >= 1 ) return $"{(int)remaining.TotalDays}j {remaining.Hours}h";
		if ( remaining.TotalHours >= 1 ) return $"{(int)remaining.TotalHours}h {remaining.Minutes}min";
		return $"{(int)remaining.TotalMinutes}min";
	}
}
