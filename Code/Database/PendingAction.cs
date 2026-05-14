using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Action en attente d'exécution, créée par le panel web et lue par S&Box.
/// Mapping vers la table `pending_actions` côté MySQL.
/// </summary>
public sealed class PendingAction
{
	[JsonPropertyName( "id" )]
	public int Id { get; set; }

	[JsonPropertyName( "target_steam_id" )]
	public long TargetSteamId { get; set; }

	[JsonPropertyName( "action" )]
	public string Action { get; set; } = "";

	[JsonPropertyName( "payload" )]
	public JsonElement? Payload { get; set; }

	[JsonPropertyName( "created_by" )]
	public long CreatedBy { get; set; }

	[JsonPropertyName( "created_at" )]
	public string CreatedAt { get; set; } = "";

	/// <summary>Lit une string du payload (null si absente).</summary>
	public string GetPayloadString( string key )
	{
		if ( Payload is null ) return null;
		if ( !Payload.Value.TryGetProperty( key, out var prop ) ) return null;
		return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
	}

	/// <summary>Lit un entier du payload (0 par défaut).</summary>
	public long GetPayloadLong( string key )
	{
		if ( Payload is null ) return 0;
		if ( !Payload.Value.TryGetProperty( key, out var prop ) ) return 0;
		return prop.ValueKind switch
		{
			JsonValueKind.Number => prop.GetInt64(),
			JsonValueKind.String => long.TryParse( prop.GetString(), out var n ) ? n : 0,
			_                    => 0,
		};
	}

	/// <summary>Lit un bool du payload.</summary>
	public bool GetPayloadBool( string key )
	{
		if ( Payload is null ) return false;
		if ( !Payload.Value.TryGetProperty( key, out var prop ) ) return false;
		return prop.ValueKind == JsonValueKind.True;
	}
}
