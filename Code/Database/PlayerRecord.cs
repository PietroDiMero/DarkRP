using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Données complètes d'un joueur enregistré en base.
/// </summary>
public sealed class PlayerRecord
{
	// ── Identité ────────────────────────────────────────────────────────
	[JsonPropertyName( "steam_id" )]
	public long SteamId { get; set; }

	[JsonPropertyName( "steam_name" )]
	public string SteamName { get; set; } = "";

	[JsonPropertyName( "rp_name" )]
	public string RpName { get; set; }

	[JsonPropertyName( "last_ip" )]
	public string LastIp { get; set; }

	// ── Économie ────────────────────────────────────────────────────────
	[JsonPropertyName( "money" )]
	public int Money { get; set; } = 2500;

	[JsonPropertyName( "is_vip" )]
	public bool IsVip { get; set; } = false;

	// ── Staff ───────────────────────────────────────────────────────────
	[JsonPropertyName( "staff_role" )]
	public StaffRole StaffRole { get; set; } = StaffRole.Player;

	// ── Modération ──────────────────────────────────────────────────────
	[JsonPropertyName( "warnings" )]
	public int Warnings { get; set; } = 0;

	[JsonPropertyName( "is_jailed" )]
	public bool IsJailed { get; set; } = false;

	[JsonPropertyName( "jail_until" )]
	public DateTime? JailUntil { get; set; }

	// ── Statistiques ────────────────────────────────────────────────────
	[JsonPropertyName( "playtime_seconds" )]
	public long PlaytimeSeconds { get; set; } = 0;

	[JsonPropertyName( "kills" )]
	public int Kills { get; set; } = 0;

	[JsonPropertyName( "deaths" )]
	public int Deaths { get; set; } = 0;

	// ── Dates ───────────────────────────────────────────────────────────
	[JsonPropertyName( "first_seen" )]
	public DateTime FirstSeen { get; set; } = DateTime.UtcNow;

	[JsonPropertyName( "last_seen" )]
	public DateTime LastSeen { get; set; } = DateTime.UtcNow;

	[JsonPropertyName( "last_job" )]
	public string LastJobPath { get; set; }

	// ── Inventaire persistant ───────────────────────────────────────────
	/// <summary>Armes/items conservés après déconnexion (chemins de prefab).</summary>
	[JsonPropertyName( "persistent_inventory" )]
	public List<string> PersistentInventory { get; set; } = new();

	// ── Helpers ─────────────────────────────────────────────────────────
	public string FormattedPlaytime()
	{
		var ts = TimeSpan.FromSeconds( PlaytimeSeconds );
		if ( ts.TotalDays >= 1 ) return $"{(int)ts.TotalDays}j {ts.Hours}h";
		if ( ts.TotalHours >= 1 ) return $"{(int)ts.TotalHours}h {ts.Minutes}min";
		return $"{(int)ts.TotalMinutes}min";
	}

	public string DisplayRole() => StaffRole.GetLabel();
}
