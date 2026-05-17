using System.Text.Json;

namespace Sandbox;

/// <summary>
/// Client HTTP pour le sidecar PHP DarkRP → MySQL.
/// Signature réelle de Http.RequestAsync :
///   RequestAsync(url, method, HttpContent content, Dictionary headers, CancellationToken)
/// </summary>
public static class DarkHttpClient
{
	public const string BaseUrl = "https://darkrp-pietro.duckdns.org/api";

	/// <summary>⚠️ Identique à API_KEY dans darkapi/config.php</summary>
	public const string ApiKey = "349c7e8efdb530c7fae5b294be087d43da63fb45d827d621bb4657d07480c5a0";

	static readonly Dictionary<string, string> _headers = new()
	{
		{ "X-Api-Key", ApiKey },
		{ "Accept",    "application/json" },
	};

	static readonly JsonSerializerOptions _jsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	// ── Ping ─────────────────────────────────────────────────────────────
	public static async Task<bool> PingAsync()
	{
		try
		{
			Log.Info( $"[DarkHttpClient] Ping vers {BaseUrl}/ping ..." );
			var resp = await Http.RequestAsync( $"{BaseUrl}/ping", "GET", null, _headers );
			Log.Info( $"[DarkHttpClient] Réponse ping : {(resp is null ? "null" : resp.StatusCode.ToString())}" );
			return resp is not null && resp.IsSuccessStatusCode;
		}
		catch ( Exception ex )
		{
			Log.Error( ex, "[DarkHttpClient] Exception lors du ping" );
			return false;
		}
	}

	// ── GET → désérialise le JSON en T ───────────────────────────────────
	public static async Task<T> GetAsync<T>( string path ) where T : class
	{
		try
		{
			var resp = await Http.RequestAsync( $"{BaseUrl}/{path}", "GET", null, _headers );
			if ( resp is null || !resp.IsSuccessStatusCode ) return null;

			var json = await resp.Content.ReadAsStringAsync();
			return JsonSerializer.Deserialize<T>( json, _jsonOpts );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] GET {path} echoue." );
			return null;
		}
	}

	// ── POST avec body JSON ───────────────────────────────────────────────
	public static async Task<bool> PostAsync( string path, object body )
	{
		try
		{
			var json    = JsonSerializer.Serialize( body, _jsonOpts );
			var content = new System.Net.Http.StringContent( json, System.Text.Encoding.UTF8, "application/json" );
			var resp    = await Http.RequestAsync( $"{BaseUrl}/{path}", "POST", content, _headers );
			return resp is not null && resp.IsSuccessStatusCode;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] POST {path} echoue." );
			return false;
		}
	}

	// ── POST avec body JSON, retourne la réponse désérialisée ────────────
	public static async Task<T> PostJsonAsync<T>( string path, object body ) where T : class
	{
		try
		{
			var json    = JsonSerializer.Serialize( body, _jsonOpts );
			var content = new System.Net.Http.StringContent( json, System.Text.Encoding.UTF8, "application/json" );
			var resp    = await Http.RequestAsync( $"{BaseUrl}/{path}", "POST", content, _headers );
			if ( resp is null || !resp.IsSuccessStatusCode ) return null;

			var respBody = await resp.Content.ReadAsStringAsync();
			return JsonSerializer.Deserialize<T>( respBody, _jsonOpts );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] POST {path} echoue." );
			return null;
		}
	}

	// ── DELETE (pas de body) ──────────────────────────────────────────────
	public static async Task<bool> DeleteAsync( string path )
	{
		try
		{
			var resp = await Http.RequestAsync( $"{BaseUrl}/{path}", "DELETE", null, _headers );
			return resp is not null && resp.IsSuccessStatusCode;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] DELETE {path} echoue." );
			return false;
		}
	}

	// ── PATCH avec body JSON ──────────────────────────────────────────────
	public static async Task<bool> PatchAsync( string path, object body )
	{
		try
		{
			var json    = JsonSerializer.Serialize( body, _jsonOpts );
			var content = new System.Net.Http.StringContent( json, System.Text.Encoding.UTF8, "application/json" );
			var resp    = await Http.RequestAsync( $"{BaseUrl}/{path}", "PATCH", content, _headers );
			return resp is not null && resp.IsSuccessStatusCode;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] PATCH {path} echoue." );
			return false;
		}
	}

	// ────────────────────────────────────────────────────────────────────
	//  Helpers spécifiques pour les actions panel ↔ jeu
	// ────────────────────────────────────────────────────────────────────

	/// <summary>Récupère les actions en attente d'exécution (non encore traitées).</summary>
	public static Task<PendingAction[]> GetUnprocessedActionsAsync()
		=> GetAsync<PendingAction[]>( "pending_actions/unprocessed" );

	/// <summary>Marque une action comme exécutée côté serveur.</summary>
	public static Task<bool> MarkActionProcessedAsync( int actionId, string result = "ok" )
		=> PatchAsync( $"pending_actions/{actionId}/processed", new { result } );

	/// <summary>Log une action staff in-game pour qu'elle apparaisse dans /logs du panel.</summary>
	public static Task<bool> LogAdminActionAsync( long adminSteamId, string adminName, long? targetSteamId, string targetName, string action, string details )
		=> PostAsync( "logs", new
		{
			admin_steam_id  = adminSteamId,
			admin_name      = adminName,
			target_steam_id = targetSteamId,
			target_name     = targetName,
			action,
			details,
		} );

	/// <summary>Classe de réponse pour StartSession.</summary>
	public sealed class StartSessionResult
	{
		[System.Text.Json.Serialization.JsonPropertyName( "status" )]
		public string Status { get; set; }

		[System.Text.Json.Serialization.JsonPropertyName( "session_id" )]
		public int SessionId { get; set; }
	}

	/// <summary>Démarre une session de connexion joueur (à l'arrivée du joueur). Retourne le session_id.</summary>
	public static Task<StartSessionResult> StartSessionAsync( long steamId, string ip, string hwid = null, string countryCode = null, string countryName = null, string city = null )
		=> PostJsonAsync<StartSessionResult>( "sessions/start", new
		{
			steam_id     = steamId,
			ip,
			hwid,
			country_code = countryCode,
			country_name = countryName,
			city,
		} );

	/// <summary>Termine une session de connexion joueur (au départ).</summary>
	public static Task<bool> EndSessionAsync( int sessionId )
		=> PatchAsync( $"sessions/{sessionId}/end", new { } );
}
