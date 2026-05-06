using System.Text.Json;

namespace Sandbox;

/// <summary>
/// Client HTTP pour le sidecar PHP DarkRP → MySQL.
/// Utilise Sandbox.Http (whitelisté). Http.RequestAsync retourne HttpResponseMessage.
/// </summary>
public static class DarkHttpClient
{
	const string BaseUrl = "http://127.0.0.1:9000";

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
			// Http.RequestAsync retourne HttpResponseMessage (pas une string)
			var resp = await Http.RequestAsync( $"{BaseUrl}/ping", "GET", null, null, _headers );
			return resp is not null && resp.IsSuccessStatusCode;
		}
		catch { return false; }
	}

	// ── GET → désérialise le JSON en T ───────────────────────────────────
	public static async Task<T> GetAsync<T>( string path ) where T : class
	{
		try
		{
			var resp = await Http.RequestAsync( $"{BaseUrl}/{path}", "GET", null, null, _headers );
			if ( resp is null || !resp.IsSuccessStatusCode ) return null;

			var json = await resp.Content.ReadAsStringAsync();
			return JsonSerializer.Deserialize<T>( json, _jsonOpts );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] GET {path} échoué." );
			return null;
		}
	}

	// ── POST avec body JSON (arguments positionnels, pas de named 'body') ─
	public static async Task<bool> PostAsync( string path, object body )
	{
		try
		{
			var json = JsonSerializer.Serialize( body, _jsonOpts );
			// Signature réelle : RequestAsync(url, method, content, contentType, headers)
			var resp = await Http.RequestAsync( $"{BaseUrl}/{path}", "POST", json, "application/json", _headers );
			return resp is not null && resp.IsSuccessStatusCode;
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
			var resp = await Http.RequestAsync( $"{BaseUrl}/{path}", "PATCH", json, "application/json", _headers );
			return resp is not null && resp.IsSuccessStatusCode;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] PATCH {path} échoué." );
			return false;
		}
	}
}
