using System.Text.Json;

namespace Sandbox;

/// <summary>
/// Client HTTP pour communiquer avec le sidecar PHP DarkRP (→ MySQL).
/// Utilise l'API Http native de S&Box (whitelist compatible).
/// </summary>
public static class DarkHttpClient
{
	const string BaseUrl = "http://127.0.0.1:9000";

	/// <summary>⚠️ Doit être identique à API_KEY dans darkapi/config.php</summary>
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
			var result = await Http.RequestAsync( $"{BaseUrl}/ping", "GET",
				headers: _headers );
			return !string.IsNullOrEmpty( result );
		}
		catch { return false; }
	}

	// ── GET → désérialise la réponse JSON en T ────────────────────────────
	public static async Task<T> GetAsync<T>( string path ) where T : class
	{
		try
		{
			var json = await Http.RequestAsync( $"{BaseUrl}/{path}", "GET",
				headers: _headers );

			if ( string.IsNullOrEmpty( json ) ) return null;
			return JsonSerializer.Deserialize<T>( json, _jsonOpts );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] GET {path} échoué." );
			return null;
		}
	}

	// ── POST avec body JSON ───────────────────────────────────────────────
	public static async Task<bool> PostAsync( string path, object body )
	{
		try
		{
			var json = JsonSerializer.Serialize( body, _jsonOpts );
			var result = await Http.RequestAsync( $"{BaseUrl}/{path}", "POST",
				body: json, contentType: "application/json", headers: _headers );

			return result != null;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] POST {path} échoué." );
			return false;
		}
	}

	// ── PATCH avec body JSON ──────────────────────────────────────────────
	public static async Task<bool> PatchAsync( string path, object body )
	{
		try
		{
			var json = JsonSerializer.Serialize( body, _jsonOpts );
			var result = await Http.RequestAsync( $"{BaseUrl}/{path}", "PATCH",
				body: json, contentType: "application/json", headers: _headers );

			return result != null;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] PATCH {path} échoué." );
			return false;
		}
	}
}
