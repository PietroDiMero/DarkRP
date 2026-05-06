using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Sandbox;

/// <summary>
/// Client HTTP pour communiquer avec le sidecar PHP DarkRP (→ MySQL).
/// Toutes les méthodes sont async et ne doivent être appelées que côté serveur (Networking.IsHost).
///
/// Clé API : doit être identique à API_KEY dans darkapi/config.php
/// </summary>
public static class DarkHttpClient
{
	// ── Configuration ────────────────────────────────────────────────────────
	/// <summary>URL de base du sidecar PHP. 127.0.0.1 = localhost uniquement, non exposé.</summary>
	const string BaseUrl = "http://127.0.0.1:9000";

	/// <summary>
	/// ⚠️ CHANGEZ CETTE VALEUR et mettez la même dans darkapi/config.php → API_KEY
	/// </summary>
	public const string ApiKey = "349c7e8efdb530c7fae5b294be087d43da63fb45d827d621bb4657d07480c5a0";

	// ── Instance HttpClient ─────────────────────────────────────────────────
	// HttpClient est thread-safe et doit être réutilisé (pas recréé à chaque requête)
	static readonly HttpClient _http = CreateClient();

	static HttpClient CreateClient()
	{
		var client = new HttpClient
		{
			BaseAddress = new Uri( BaseUrl + "/" ),
			Timeout     = TimeSpan.FromSeconds( 10 )
		};
		client.DefaultRequestHeaders.Add( "X-Api-Key", ApiKey );
		client.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue( "application/json" ) );
		return client;
	}

	// ── Options JSON ────────────────────────────────────────────────────────
	static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,  // "steam_id" → SteamId
		WriteIndented               = false,
	};

	// ════════════════════════════════════════════════════════════════════════
	// API publique
	// ════════════════════════════════════════════════════════════════════════

	/// <summary>Vérifie que le sidecar PHP est accessible. Appelé au démarrage.</summary>
	public static async Task<bool> PingAsync()
	{
		try
		{
			var resp = await _http.GetAsync( "ping" );
			return resp.IsSuccessStatusCode;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "[DarkHttpClient] Ping échoué." );
			return false;
		}
	}

	/// <summary>GET {path} → désérialise la réponse JSON en T. Retourne null si erreur ou 404.</summary>
	public static async Task<T> GetAsync<T>( string path ) where T : class
	{
		try
		{
			var resp = await _http.GetAsync( path );

			if ( resp.StatusCode == System.Net.HttpStatusCode.NotFound ) return null;

			if ( !resp.IsSuccessStatusCode )
			{
				Log.Warning( $"[DarkHttpClient] GET {path} → HTTP {(int)resp.StatusCode}" );
				return null;
			}

			var json = await resp.Content.ReadAsStringAsync();
			return JsonSerializer.Deserialize<T>( json, _jsonOptions );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] GET {path} échoué." );
			return null;
		}
	}

	/// <summary>POST {path} avec un body JSON sérialisé depuis {body}. Retourne true si succès.</summary>
	public static async Task<bool> PostAsync( string path, object body )
	{
		try
		{
			var json    = JsonSerializer.Serialize( body, _jsonOptions );
			var content = new StringContent( json, Encoding.UTF8, "application/json" );
			var resp    = await _http.PostAsync( path, content );

			if ( !resp.IsSuccessStatusCode )
				Log.Warning( $"[DarkHttpClient] POST {path} → HTTP {(int)resp.StatusCode}" );

			return resp.IsSuccessStatusCode;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] POST {path} échoué." );
			return false;
		}
	}

	/// <summary>PATCH {path} avec un body JSON. Pour les mises à jour partielles.</summary>
	public static async Task<bool> PatchAsync( string path, object body )
	{
		try
		{
			var json    = JsonSerializer.Serialize( body, _jsonOptions );
			var content = new StringContent( json, Encoding.UTF8, "application/json" );
			var req     = new HttpRequestMessage( HttpMethod.Patch, path ) { Content = content };
			var resp    = await _http.SendAsync( req );

			if ( !resp.IsSuccessStatusCode )
				Log.Warning( $"[DarkHttpClient] PATCH {path} → HTTP {(int)resp.StatusCode}" );

			return resp.IsSuccessStatusCode;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkHttpClient] PATCH {path} échoué." );
			return false;
		}
	}
}
