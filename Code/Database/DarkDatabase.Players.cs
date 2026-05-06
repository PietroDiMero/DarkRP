namespace Sandbox;

public sealed partial class DarkDatabase
{
	// ── Enregistrement / mise à jour à la connexion ─────────────────────────
	async Task RegisterOrUpdatePlayerAsync( Connection connection )
	{
		await Task.Yield(); // Évite de bloquer le thread principal

		var steamId = (long)connection.SteamId.Value;
		var ip      = connection.Address ?? "";

		// Essayer de charger depuis MySQL si pas en cache
		if ( !_players.TryGetValue( steamId, out var record ) )
			record = await LoadPlayerAsync( steamId );

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
			Log.Info( $"[DarkDatabase] Nouveau joueur : {connection.DisplayName} ({steamId})" );
		}
		else
		{
			// Mise à jour des infos dynamiques
			record.SteamName = connection.DisplayName;
			record.LastIp    = ip;
			record.LastSeen  = DateTime.UtcNow;
		}

		_players[steamId] = record;
		await SavePlayerAsync( record );

		// Synchroniser rôle + argent vers le composant Player.
		// On utilise un retry car le Player component peut ne pas être encore spawn
		// au moment où RegisterOrUpdatePlayerAsync s'exécute (timing async).
		_ = SyncPlayerDataOnConnectAsync( connection, record );
	}

	/// <summary>
	/// Applique les données MySQL (rôle staff + argent) au composant Player côté serveur.
	/// Réessaie toutes les 250ms pendant 5s max, car le Player component peut spawn
	/// quelques frames après la connexion réseau.
	/// </summary>
	async Task SyncPlayerDataOnConnectAsync( Connection conn, PlayerRecord record )
	{
		var adminRole = record.StaffRole.ToAdminRole();
		var steamId64 = (SteamId)record.SteamId;

		// AdminSystem disponible immédiatement (indépendant du Player component)
		AdminSystem.Current?.SetRole( steamId64, adminRole, conn.DisplayName );

		// Retry jusqu'à ce que le composant Player soit spawn
		for ( int i = 0; i < 20; i++ )
		{
			await Task.Delay( 250 );
			await GameTask.MainThread();

			var player = Player.FindForConnection( conn );
			if ( !player.IsValid() ) continue;

			player.SetAdminRole( adminRole );
			player.SetMoney( record.Money );

			Log.Info( $"[DarkDatabase] ✅ Sync {conn.DisplayName} — " +
			          $"argent: ${record.Money} | rôle: {record.StaffRole.GetLabel()} ({i * 250}ms)" );
			return;
		}

		Log.Warning( $"[DarkDatabase] ⚠️ Timeout sync {conn.DisplayName} — " +
		             "Player component introuvable après 5s. Rôle et argent non appliqués." );
	}

	// ── Getters ─────────────────────────────────────────────────────────────
	public PlayerRecord GetPlayer( long steamId )
	{
		// Cache en premier
		if ( _players.TryGetValue( steamId, out var cached ) ) return cached;

		// Chargement synchrone depuis MySQL n'est pas possible ici sans bloquer.
		// On retourne null — le chargement async se fait via RegisterOrUpdatePlayerAsync.
		// Pour les besoins du panel admin, utiliser GetAllPlayersAsync().
		return null;
	}

	public PlayerRecord GetPlayer( Connection connection ) =>
		GetPlayer( (long)connection.SteamId.Value );

	/// <summary>Retourne les joueurs actuellement en cache (joueurs connectés + récemment chargés).</summary>
	public IReadOnlyList<PlayerRecord> GetAllPlayers() =>
		_players.Values.ToList().AsReadOnly();

	/// <summary>
	/// Charge TOUS les joueurs depuis MySQL (pour le panneau admin).
	/// À appeler via RPC depuis le serveur, pas directement côté client.
	/// </summary>
	public async Task<List<PlayerRecord>> GetAllPlayersFromDbAsync() =>
		await DarkHttpClient.GetAsync<List<PlayerRecord>>( "players" ) ?? new();

	// ── Money ───────────────────────────────────────────────────────────────
	public void SetMoney( long steamId, int amount )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.Money = Math.Max( 0, amount );
		_ = DarkHttpClient.PatchAsync( $"players/{steamId}/money", new { money = record.Money } );

		SyncMoneyToPlayer( steamId, record.Money );
	}

	public void GiveMoney( long steamId, int amount )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.Money = Math.Max( 0, record.Money + amount );
		_ = DarkHttpClient.PatchAsync( $"players/{steamId}/money", new { money = record.Money } );

		SyncMoneyToPlayer( steamId, record.Money );
	}

	/// <summary>Synchronise l'argent depuis le composant Player vers le cache (sans sauvegarder immédiatement).</summary>
	public void SyncMoneyFromPlayer( long steamId, int currentMoney )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.Money = currentMoney;
		// Pas de sauvegarde ici — l'auto-save périodique le fera
	}

	// ── VIP ─────────────────────────────────────────────────────────────────
	public void SetVip( long steamId, bool isVip )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.IsVip = isVip;
		_ = DarkHttpClient.PatchAsync( $"players/{steamId}/vip", new { is_vip = isVip } );

		Log.Info( $"[DarkDatabase] VIP {(isVip ? "activé" : "désactivé")} pour {steamId}" );
	}

	public bool IsVip( long steamId ) =>
		_players.TryGetValue( steamId, out var r ) && r.IsVip;

	// ── Inventaire persistant ───────────────────────────────────────────────
	public void SetPersistentInventory( long steamId, IEnumerable<string> items )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.PersistentInventory = items.ToList();
		_ = SavePlayerAsync( record ); // Sauvegarde complète pour l'inventaire
	}

	public List<string> GetPersistentInventory( long steamId ) =>
		_players.TryGetValue( steamId, out var r ) ? r.PersistentInventory : new();

	// ── Job ─────────────────────────────────────────────────────────────────
	public void SaveLastJob( long steamId, string jobPath )
	{
		if ( !_players.TryGetValue( steamId, out var record ) ) return;
		record.LastJobPath = jobPath;
		// Pas de sauvegarde immédiate (fréquent) — auto-save périodique
	}

	// ── Statistiques ────────────────────────────────────────────────────────
	public void AddKill( long steamId )
	{
		if ( _players.TryGetValue( steamId, out var r ) ) r.Kills++;
	}

	public void AddDeath( long steamId )
	{
		if ( _players.TryGetValue( steamId, out var r ) ) r.Deaths++;
	}

	// ── Warnings ────────────────────────────────────────────────────────────
	public void AddWarning( long steamId )
	{
		if ( !_players.TryGetValue( steamId, out var r ) ) return;
		r.Warnings++;
		_ = DarkHttpClient.PatchAsync( $"players/{steamId}/warnings", new { warnings = r.Warnings } );
	}

	// ── Jail ────────────────────────────────────────────────────────────────
	public void SetJail( long steamId, bool isJailed, DateTime? until = null )
	{
		if ( !_players.TryGetValue( steamId, out var r ) ) return;
		r.IsJailed  = isJailed;
		r.JailUntil = until;
		_ = DarkHttpClient.PatchAsync( $"players/{steamId}/jail", new
		{
			is_jailed  = isJailed,
			jail_until = until?.ToString( "o" ) // ISO 8601
		} );
	}

	// ── Persistance HTTP ────────────────────────────────────────────────────

	/// <summary>Sauvegarde un PlayerRecord complet dans MySQL via le sidecar.</summary>
	async Task SavePlayerAsync( PlayerRecord record )
	{
		// On sérialise en un objet anonyme avec les bons noms JSON attendus par le PHP
		var payload = new
		{
			steam_id             = record.SteamId,
			steam_name           = record.SteamName,
			rp_name              = record.RpName,
			last_ip              = record.LastIp,
			money                = record.Money,
			is_vip               = record.IsVip,
			staff_role           = (int)record.StaffRole,
			warnings             = record.Warnings,
			is_jailed            = record.IsJailed,
			jail_until           = record.JailUntil?.ToString( "o" ),
			playtime_seconds     = record.PlaytimeSeconds,
			kills                = record.Kills,
			deaths               = record.Deaths,
			first_seen           = record.FirstSeen.ToString( "o" ),
			last_seen            = record.LastSeen.ToString( "o" ),
			last_job             = record.LastJobPath,
			persistent_inventory = record.PersistentInventory,
		};

		var ok = await DarkHttpClient.PostAsync( "players", payload );
		if ( !ok )
			Log.Warning( $"[DarkDatabase] Impossible de sauvegarder le joueur {record.SteamId}" );
	}

	/// <summary>Charge un PlayerRecord depuis MySQL. Retourne null si inconnu.</summary>
	async Task<PlayerRecord> LoadPlayerAsync( long steamId )
	{
		return await DarkHttpClient.GetAsync<PlayerRecord>( $"players/{steamId}" );
	}

	// ── Sync vers composants en ligne ────────────────────────────────────────
	static void SyncMoneyToPlayer( long steamId, int money )
	{
		var conn = Connection.All.FirstOrDefault( c => (long)c.SteamId.Value == steamId );
		if ( conn is null ) return;

		var player = Player.FindForConnection( conn );
		if ( player is null ) return;

		player.SetMoney( money );
	}
}
