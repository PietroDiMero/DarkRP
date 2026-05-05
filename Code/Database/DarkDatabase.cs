using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Système central de persistance du serveur DarkRP.
/// Stocke joueurs, bans, rôles, inventaires et logs via le système de fichiers S&amp;Box.
/// Architecture prête pour une migration MySQL via sidecar HTTP si besoin.
/// </summary>
public sealed partial class DarkDatabase : GameObjectSystem<DarkDatabase>, Component.INetworkListener
{
	// ── Chemins de stockage ─────────────────────────────────────────────
	const string PlayersDir  = "darkrp2/db/players";
	const string BansFile    = "darkrp2/db/bans.json";
	const string RolesFile   = "darkrp2/db/roles.json";
	const string LogsFile    = "darkrp2/db/admin_logs.json";

	// ── Cache en mémoire ────────────────────────────────────────────────
	readonly Dictionary<long, PlayerRecord>   _players  = new();
	readonly Dictionary<long, BanRecord>      _bans     = new();
	readonly Dictionary<long, StaffRole>      _roles    = new();
	readonly List<AdminLog>                   _logs     = new();

	// Sessions actives (steamId → joined_at, pour calculer playtime)
	readonly Dictionary<long, DateTime>       _sessions = new();

	// ── Init ────────────────────────────────────────────────────────────
	public DarkDatabase( Scene scene ) : base( scene )
	{
		if ( !Networking.IsHost ) return;

		EnsureDirectories();
		LoadBans();
		LoadRoles();
		LoadLogs();

		Listen( Stage.StartUpdate, 0, OnTick, "DarkDatabase" );
		Log.Info( "[DarkDatabase] Système initialisé." );
	}

	// ── Tick : sauvegarde périodique ────────────────────────────────────
	TimeSince _lastAutoSave;

	void OnTick()
	{
		if ( !Networking.IsHost ) return;
		if ( _lastAutoSave < 300f ) return; // toutes les 5 min

		_lastAutoSave = 0;
		AutoSavePlayers();
	}

	void AutoSavePlayers()
	{
		foreach ( var (steamId, record) in _players )
		{
			UpdatePlaytime( steamId );
			SavePlayer( record );
		}
	}

	// ── Répertoires ─────────────────────────────────────────────────────
	static void EnsureDirectories()
	{
		foreach ( var dir in new[] { "darkrp2/db", "darkrp2/db/players" } )
		{
			if ( !FileSystem.Data.DirectoryExists( dir ) )
				FileSystem.Data.CreateDirectory( dir );
		}
	}

	// ── INetworkListener : vérification ban AVANT connexion ─────────────
	bool Component.INetworkListener.AcceptConnection( Connection connection, ref string reason )
	{
		var steamId = (long)connection.SteamId.Value;
		var ip      = connection.Address ?? "";

		// Vérifier ban SteamId
		if ( _bans.TryGetValue( steamId, out var banById ) && banById.IsActive )
		{
			reason = FormatBanReason( banById );
			Log.Info( $"[DarkDatabase] Connexion refusée (SteamId) : {connection.DisplayName} — {banById.Reason}" );
			return false;
		}

		// Vérifier ban IP
		if ( !string.IsNullOrWhiteSpace( ip ) )
		{
			var ipBan = _bans.Values.FirstOrDefault( b => b.IsActive && b.Ip == ip );
			if ( ipBan is not null )
			{
				reason = FormatBanReason( ipBan );
				Log.Info( $"[DarkDatabase] Connexion refusée (IP) : {connection.DisplayName} / {ip} — {ipBan.Reason}" );
				return false;
			}
		}

		return true;
	}

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

	// ── INetworkListener : connexion acceptée ───────────────────────────
	void Component.INetworkListener.OnActive( Connection connection )
	{
		if ( !Networking.IsHost ) return;

		var steamId = (long)connection.SteamId.Value;
		var now     = DateTime.UtcNow;

		_sessions[steamId] = now;
		_ = RegisterOrUpdatePlayerAsync( connection );
	}

	// ── INetworkListener : déconnexion ──────────────────────────────────
	void Component.INetworkListener.OnDisconnected( Connection connection )
	{
		if ( !Networking.IsHost ) return;

		var steamId = (long)connection.SteamId.Value;
		UpdatePlaytime( steamId );
		_sessions.Remove( steamId );

		if ( _players.TryGetValue( steamId, out var record ) )
		{
			record.LastSeen = DateTime.UtcNow;
			SavePlayer( record );
		}
	}

	void UpdatePlaytime( long steamId )
	{
		if ( !_sessions.TryGetValue( steamId, out var joinedAt ) ) return;
		if ( !_players.TryGetValue( steamId, out var record ) ) return;

		var seconds = (long)(DateTime.UtcNow - joinedAt).TotalSeconds;
		record.PlaytimeSeconds += seconds;
		_sessions[steamId] = DateTime.UtcNow; // reset pour la prochaine mise à jour
	}

	// ── Accesseur global ────────────────────────────────────────────────
	public static DarkDatabase Instance => Current;
}
