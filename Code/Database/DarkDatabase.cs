namespace Sandbox;

/// <summary>
/// Système central de persistance du serveur DarkRP.
/// Stocke joueurs, bans, rôles, inventaires et logs via MySQL (via sidecar PHP).
///
/// Architecture :
///   S&Box (C#) → HTTP localhost:9000 → PHP sidecar → MySQL distant
///
/// Les données sont gardées en cache mémoire pour les lectures fréquentes
/// (ban check à la connexion, etc.). Les écritures passent en HTTP de façon
/// asynchrone (fire-and-forget pour les sauvegardes périodiques).
/// </summary>
public sealed partial class DarkDatabase : GameObjectSystem<DarkDatabase>, Component.INetworkListener
{
	// ── Cache en mémoire ────────────────────────────────────────────────────
	readonly Dictionary<long, PlayerRecord> _players  = new();
	readonly Dictionary<long, BanRecord>    _bans     = new();
	readonly Dictionary<long, StaffRole>    _roles    = new();
	readonly List<AdminLog>                 _logs     = new();

	// Sessions actives (steamId → joined_at, pour calculer le playtime)
	readonly Dictionary<long, DateTime> _sessions = new();

	// Flag indiquant que l'init async est terminée (bans chargés = prêt à accepter des connexions)
	bool _initialized = false;

	// ── Init ────────────────────────────────────────────────────────────────
	public DarkDatabase( Scene scene ) : base( scene )
	{
		if ( !Networking.IsHost ) return;

		_ = InitAsync();
		Listen( Stage.StartUpdate, 0, OnTick, "DarkDatabase" );
	}

	async Task InitAsync()
	{
		Log.Info( "[DarkDatabase] Connexion au sidecar PHP MySQL..." );

		// Test de connectivité
		if ( !await DarkHttpClient.PingAsync() )
		{
			Log.Error( "[DarkDatabase] ❌ Sidecar API inaccessible sur http://127.0.0.1:9000" );
			Log.Error( "[DarkDatabase]    → Vérifiez que PHP est installé sur le serveur" );
			return;
		}

		// Chargement dans l'ordre : bans en premier (nécessaire pour AcceptConnection)
		await LoadBansAsync();
		await LoadRolesAsync();
		await LoadLogsAsync();

		_initialized = true;

		Log.Info( $"[DarkDatabase] ✅ MySQL opérationnel — " +
		          $"{_bans.Count} ban(s) actif(s) | {_roles.Count} rôle(s) | {_logs.Count} log(s)" );
	}

	// ── Tick : sauvegarde périodique ────────────────────────────────────────
	TimeSince _lastAutoSave;

	void OnTick()
	{
		if ( !Networking.IsHost || !_initialized ) return;
		if ( _lastAutoSave < 300f ) return; // toutes les 5 min

		_lastAutoSave = 0;
		_ = AutoSavePlayersAsync();
	}

	async Task AutoSavePlayersAsync()
	{
		// Copie des clés pour éviter les modifications pendant l'itération
		var steamIds = _players.Keys.ToList();
		foreach ( var steamId in steamIds )
		{
			UpdatePlaytime( steamId );
			if ( _players.TryGetValue( steamId, out var record ) )
				await SavePlayerAsync( record );
		}
	}

	// ── INetworkListener : vérification ban AVANT connexion ─────────────────
	bool Component.INetworkListener.AcceptConnection( Connection connection, ref string reason )
	{
		// Serveur pas encore prêt (init async en cours)
		if ( !_initialized )
		{
			reason = "Serveur en cours de démarrage, réessayez dans quelques secondes.";
			return false;
		}

		var steamId = (long)connection.SteamId.Value;
		var ip      = connection.Address ?? "";

		// Vérification ban SteamId
		if ( _bans.TryGetValue( steamId, out var banById ) && banById.IsActive )
		{
			reason = FormatBanReason( banById );
			Log.Info( $"[DarkDatabase] Connexion refusée (SteamId ban) : {connection.DisplayName} — {banById.Reason}" );
			return false;
		}

		// Vérification ban IP
		if ( !string.IsNullOrWhiteSpace( ip ) )
		{
			var ipBan = _bans.Values.FirstOrDefault( b => b.IsActive && b.Ip == ip );
			if ( ipBan is not null )
			{
				reason = FormatBanReason( ipBan );
				Log.Info( $"[DarkDatabase] Connexion refusée (IP ban) : {connection.DisplayName} / {ip}" );
				return false;
			}
		}

		return true;
	}

	// ── INetworkListener : connexion acceptée ───────────────────────────────
	void Component.INetworkListener.OnActive( Connection connection )
	{
		if ( !Networking.IsHost ) return;

		var steamId = (long)connection.SteamId.Value;
		_sessions[steamId] = DateTime.UtcNow;

		_ = RegisterOrUpdatePlayerAsync( connection );
	}

	// ── INetworkListener : déconnexion ──────────────────────────────────────
	void Component.INetworkListener.OnDisconnected( Connection connection )
	{
		if ( !Networking.IsHost ) return;

		var steamId = (long)connection.SteamId.Value;
		UpdatePlaytime( steamId );
		_sessions.Remove( steamId );

		if ( _players.TryGetValue( steamId, out var record ) )
		{
			record.LastSeen = DateTime.UtcNow;
			_ = SavePlayerAsync( record );
		}
	}

	// ── Calcul du temps de jeu ──────────────────────────────────────────────
	void UpdatePlaytime( long steamId )
	{
		if ( !_sessions.TryGetValue( steamId, out var joinedAt ) ) return;
		if ( !_players.TryGetValue( steamId, out var record ) ) return;

		var seconds = (long)(DateTime.UtcNow - joinedAt).TotalSeconds;
		record.PlaytimeSeconds += seconds;
		_sessions[steamId] = DateTime.UtcNow; // reset le chrono
	}

	// ── Formatage du message de ban ─────────────────────────────────────────
	static string FormatBanReason( BanRecord ban )
	{
		if ( ban.ExpiresAt.HasValue )
		{
			var remaining = ban.ExpiresAt.Value - DateTime.UtcNow;
			var timeStr   = remaining.TotalDays >= 1
				? $"{(int)remaining.TotalDays}j {remaining.Hours}h"
				: $"{(int)remaining.TotalHours}h {remaining.Minutes}min";

			return $"Banni : {ban.Reason} (expire dans {timeStr})";
		}

		return $"Banni définitivement : {ban.Reason}";
	}

	// ── Accesseur global ────────────────────────────────────────────────────
	public static DarkDatabase Instance => Current;
}
