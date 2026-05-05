using System.Text.Json;

namespace Sandbox;

public sealed partial class DarkDatabase
{
	// ── Enregistrement / mise à jour à la connexion ─────────────────────
	async Task RegisterOrUpdatePlayerAsync( Connection connection )
	{
		await Task.Yield();

		var steamId = (long)connection.SteamId.Value;
		var ip      = connection.Address ?? "";

		if ( !_players.TryGetValue( steamId, out var record ) )
		{
			// Essayer de charger depuis le fichier
			record = LoadPlayer( steamId );
		}

		if ( record is null )
		{
			// Nouveau joueur
			record = new PlayerRecord
			{
				SteamId   = steamId,
				SteamName = connection.DisplayName,
				LastIp    = ip,
				FirstSeen = DateTime.UtcNow,
				LastSeen  = DateTime.UtcNow,
			};
			Log.Info( $"[DarkDatabase] Nouveau joueur enregistré : {connection.DisplayName} ({steamId})" );
		}
		else
		{
			// Mise à jour des infos dynamiques
			record.SteamName = connection.DisplayName;
			record.LastIp    = ip;
			record.LastSeen  = DateTime.UtcNow;
		}

		_players[steamId] = record;
		SavePlayer( record );

		// Synchroniser le rôle avec AdminSystem
		SyncRoleToAdminSystem( steamId, record.StaffRole );
	}

	// ── Getters ─────────────────────────────────────────────────────────
	public PlayerRecord GetPlayer( long steamId )
	{
		if ( _players.TryGetValue( steamId, out var cached ) ) return cached;
		var loaded = LoadPlayer( steamId );
		if ( loaded != null ) _players[steamId] = loaded;
		return loaded;
	}

	public PlayerRecord GetPlayer( Connection connection ) =>
		GetPlayer( (long)connection.SteamId.Value );

	public IReadOnlyList<PlayerRecord> GetAllPlayers() =>
		_players.Values.ToList().AsReadOnly();

	// ── Money ───────────────────────────────────────────────────────────
	public void SetMoney( long steamId, int amount )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.Money = Math.Max( 0, amount );
		SavePlayer( record );

		// Synchroniser avec le composant Player en ligne
		SyncMoneyToPlayer( steamId, record.Money );
	}

	public void GiveMoney( long steamId, int amount )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.Money = Math.Max( 0, record.Money + amount );
		SavePlayer( record );
		SyncMoneyToPlayer( steamId, record.Money );
	}

	public void SyncMoneyFromPlayer( long steamId, int currentMoney )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.Money = currentMoney;
		// Pas de SavePlayer ici — fait par l'auto-save périodique
	}

	// ── VIP ─────────────────────────────────────────────────────────────
	public void SetVip( long steamId, bool isVip )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.IsVip = isVip;
		SavePlayer( record );
		Log.Info( $"[DarkDatabase] VIP {(isVip ? "activé" : "désactivé")} pour {steamId}" );
	}

	public bool IsVip( long steamId ) =>
		_players.TryGetValue( steamId, out var r ) && r.IsVip;

	// ── Inventaire persistant ───────────────────────────────────────────
	public void SetPersistentInventory( long steamId, IEnumerable<string> items )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.PersistentInventory = items.ToList();
		SavePlayer( record );
	}

	public List<string> GetPersistentInventory( long steamId ) =>
		_players.TryGetValue( steamId, out var r ) ? r.PersistentInventory : new();

	// ── Job ─────────────────────────────────────────────────────────────
	public void SaveLastJob( long steamId, string jobPath )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.LastJobPath = jobPath;
	}

	// ── Statistiques ────────────────────────────────────────────────────
	public void AddKill( long steamId )
	{
		if ( !_players.TryGetValue( steamId, out var r ) ) return;
		r.Kills++;
	}

	public void AddDeath( long steamId )
	{
		if ( !_players.TryGetValue( steamId, out var r ) ) return;
		r.Deaths++;
	}

	// ── Persistance fichier ─────────────────────────────────────────────
	void SavePlayer( PlayerRecord record )
	{
		var path = PlayerPath( record.SteamId );
		try
		{
			var json = JsonSerializer.Serialize( record, new JsonSerializerOptions { WriteIndented = true } );
			FileSystem.Data.WriteAllText( path, json );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkDatabase] Impossible de sauvegarder le joueur {record.SteamId}" );
		}
	}

	PlayerRecord LoadPlayer( long steamId )
	{
		var path = PlayerPath( steamId );
		if ( !FileSystem.Data.FileExists( path ) ) return null;
		try
		{
			var json = FileSystem.Data.ReadAllText( path );
			return JsonSerializer.Deserialize<PlayerRecord>( json );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[DarkDatabase] Impossible de charger le joueur {steamId}" );
			return null;
		}
	}

	static string PlayerPath( long steamId ) => $"{PlayersDir}/{steamId}.json";

	// ── Synchro avec composants en ligne ────────────────────────────────
	static void SyncMoneyToPlayer( long steamId, int money )
	{
		var conn = Connection.All.FirstOrDefault( c => (long)c.SteamId.Value == steamId );
		if ( conn is null ) return;

		var player = Player.FindForConnection( conn );
		if ( player is null ) return;

		player.SetMoney( money );
	}
}
