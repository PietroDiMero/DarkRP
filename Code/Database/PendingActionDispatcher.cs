using System.Linq;
using System.Threading.Tasks;
using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Résultat d'un dispatch : statut + message d'erreur précis si fail.
/// Le message remonte jusqu'au panel via pending_actions.error_message pour
/// que le fondateur puisse voir exactement ce qui a planté dans /panel/startup.
/// </summary>
public readonly record struct DispatchResult( bool Ok, string Error )
{
	public static DispatchResult Success() => new( true, null );
	public static DispatchResult Failure( string err ) => new( false, err );
}

/// <summary>
/// Dispatcher central : reçoit une PendingAction et appelle le bon handler in-game.
///
/// Pour ajouter une nouvelle action depuis le panel :
///   1. Ajouter le case ici
///   2. Implémenter la méthode Handle{Xxx}Async
///   3. Côté panel : créer un PendingAction avec le bon `action` et `payload`
/// </summary>
public static class PendingActionDispatcher
{
	/// <summary>
	/// Dispatch + exécute une action. Retourne un DispatchResult avec un message
	/// d'erreur précis si fail. Toute exception est capturée et son message remonté.
	/// </summary>
	public static async Task<DispatchResult> DispatchAsync( PendingAction action )
	{
		if ( action is null || string.IsNullOrEmpty( action.Action ) )
			return DispatchResult.Failure( "Action null ou vide" );

		try
		{
			bool ok = action.Action switch
			{
				"kick"         => await HandleKickAsync( action ),
				"ban"          => await HandleBanAsync( action ),
				"unban"        => await HandleUnbanAsync( action ),
				"jail"         => await HandleJailAsync( action ),
				"warn"         => await HandleWarnAsync( action ),
				"slay"         => await HandleSlayAsync( action ),
				"freeze"       => await HandleFreezeAsync( action ),
				"mute_voice"   => await HandleMuteVoiceAsync( action ),
				"mute_chat"    => await HandleMuteChatAsync( action ),
				"set_job"      => await HandleSetJobAsync( action ),
				"set_money"    => await HandleSetMoneyAsync( action ),
				"set_vip"      => await HandleSetVipAsync( action ),
				"set_role"     => await HandleSetRoleAsync( action ),
				"teleport"     => await HandleTeleportAsync( action ),
				"announce"        => await HandleAnnounceAsync( action ),
				"refresh_jobs"    => await HandleRefreshJobsAsync( action ),
				"refresh_economy" => await HandleRefreshEconomyAsync( action ),
				"server_restart_warning" => await HandleServerRestartWarningAsync( action ),
				_                 => HandleUnknown( action ),
			};

			return ok
				? DispatchResult.Success()
				: DispatchResult.Failure( $"Handler '{action.Action}' a renvoyé false (cf logs serveur)" );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( ex, $"[PendingActionDispatcher] Exception sur action '{action.Action}'." );
			// Format compact : type + message + 1 ligne de stack pour debug rapide depuis le panel
			var firstStackLine = ex.StackTrace?.Split( '\n' ).FirstOrDefault()?.Trim() ?? "";
			var msg = $"{ex.GetType().Name}: {ex.Message}";
			if ( !string.IsNullOrEmpty( firstStackLine ) ) msg += $" @ {firstStackLine}";
			return DispatchResult.Failure( msg );
		}
	}

	// ════════════════════════════════════════════════════════ RESTART ANNOUNCE
	// Affiche un countdown en chat rouge global + force-save toutes les BDD a la fin.
	// Le restart effectif du process doit etre fait via le panel YorkHost.
	private static async Task<bool> HandleServerRestartWarningAsync( PendingAction a )
	{
		int delay     = (int) a.GetPayloadLong( "delay_seconds" );
		if ( delay <= 0 ) delay = 60;
		var reason    = a.GetPayloadString( "reason" ) ?? "";
		var adminName = a.GetPayloadString( "admin_name" ) ?? "Système";

		Log.Info( $"[Restart] Countdown lancé : {delay}s (par {adminName}, raison: {reason})" );

		// Lance le countdown asynchrone (ne bloque pas le poller)
		_ = RunRestartCountdownAsync( delay, reason, adminName );
		return true;
	}

	private static async Task RunRestartCountdownAsync( int totalSeconds, string reason, string adminName )
	{
		var chat = Game.ActiveScene?.Get<Chat>();
		string subText = string.IsNullOrWhiteSpace( reason )
			? $"Lancé par {adminName}"
			: $"Lancé par {adminName} · {reason}";

		// Banner global centre haut (synchro tous les clients via [Sync])
		RestartBanner.ShowGlobal( $"Redémarrage dans {FormatDuration( totalSeconds )}", subText );

		// Annonce initiale en chat
		chat?.AddSystemText(
			$"⚠ REDÉMARRAGE PROGRAMMÉ dans {FormatDuration( totalSeconds )} (par {adminName})",
			"🔄" );

		var sinceStart = 0;
		while ( sinceStart < totalSeconds )
		{
			await GameTask.DelaySeconds( 1f );
			sinceStart++;
			var remaining = totalSeconds - sinceStart;

			// Update du banner toutes les secondes pour avoir un vrai countdown live
			if ( remaining > 0 )
			{
				RestartBanner.ShowGlobal( $"Redémarrage dans {FormatDuration( remaining )}", subText );
			}
		}

		// Fin du countdown
		RestartBanner.ShowGlobal( "Sauvegarde en cours...", "Le serveur va redémarrer" );
		chat?.AddSystemText( "🔴 REDÉMARRAGE IMMINENT — Sauvegarde en cours...", "🔄" );
		await ForceSaveAllPlayersAsync();
		chat?.AddSystemText( "✅ Données sauvegardées.", "✅" );

		// KICK tous les joueurs proprement avec un message
		RestartBanner.ShowGlobal( "Serveur en redémarrage", "Reconnecte-toi dans ~30s" );
		await GameTask.DelaySeconds( 2f );  // laisse 2s pour que le banner soit vu

		foreach ( var conn in Connection.All.Where( c => !c.IsHost ).ToList() )
		{
			try
			{
				conn.Kick( "Serveur en redémarrage — reconnecte-toi dans 30s" );
			}
			catch { /* ignore si déjà déconnecté */ }
		}

		// Eteint le banner apres 5s (au cas ou il reste quelqu un qui n a pas ete kick)
		await GameTask.DelaySeconds( 5f );
		RestartBanner.HideGlobal();
	}

	private static async Task ForceSaveAllPlayersAsync()
	{
		var db = DarkDatabase.Instance;
		if ( db is null ) return;

		foreach ( var p in Game.ActiveScene.GetAllComponents<Player>() )
		{
			if ( p.SteamId <= 0 ) continue;
			// Sync de l'argent + kills + deaths vers BDD
			db.SyncMoneyFromPlayer( p.SteamId, p.Money );
			// Le UpdatePlaytime + SavePlayerAsync sera fait par OnDisconnected, mais on
			// declenche aussi un save complet maintenant pour eviter les pertes si crash.
			_ = DarkHttpClient.PatchAsync( $"players/{p.SteamId}/live-stats", new
			{
				money    = p.Money,
				kills    = p.PlayerData?.Kills ?? 0,
				deaths   = p.PlayerData?.Deaths ?? 0,
				playtime_delta_seconds = 0,   // pas de delta supplementaire ici
			} );
		}
		await GameTask.DelaySeconds( 0.5f );  // laisse le temps aux requetes de partir
	}

	private static string FormatDuration( int seconds )
	{
		if ( seconds >= 60 )
		{
			int m = seconds / 60;
			int s = seconds % 60;
			return s == 0 ? $"{m}min" : $"{m}min {s}s";
		}
		return $"{seconds}s";
	}

	// ─────────────────────────────────────────────────────────── Helpers
	private static Connection FindConnection( long steamId )
	{
		return Connection.All.FirstOrDefault( c => c.SteamId.Value == steamId );
	}

	private static Player FindPlayer( long steamId )
	{
		// Cherche le Player connecté correspondant au SteamID
		return Game.ActiveScene?
			.GetAllComponents<Player>()
			.FirstOrDefault( p => p.Network.Owner?.SteamId.Value == steamId );
	}

	// ═════════════════════════════════════════════════════════════ KICK
	private static async Task<bool> HandleKickAsync( PendingAction a )
	{
		var conn = FindConnection( a.TargetSteamId );
		if ( conn is null )
		{
			// Le joueur n'est pas en ligne, l'action est sans effet mais on la marque traitée
			Log.Info( $"[Kick] Joueur {a.TargetSteamId} non connecté, ignoré." );
			return true;
		}

		var reason = a.GetPayloadString( "reason" ) ?? "Kicked par admin";
		GameManager.Current?.Kick( conn, reason );
		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, conn.DisplayName, "kick", reason );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ BAN
	private static async Task<bool> HandleBanAsync( PendingAction a )
	{
		var conn = FindConnection( a.TargetSteamId );
		var reason = a.GetPayloadString( "reason" ) ?? "Banni par admin";

		// Si le joueur est connecté, on le bannit immédiatement.
		// Sinon le ban est déjà persisté côté panel (BDD `bans`), il sera bloqué à la prochaine connexion.
		if ( conn is not null )
		{
			BanSystem.Current?.Ban( conn, reason );
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, conn?.DisplayName ?? a.TargetSteamId.ToString(), "ban", reason );
		return true;
	}

	private static async Task<bool> HandleUnbanAsync( PendingAction a )
	{
		// L'unban est déjà persisté côté panel (is_active=0 sur bans).
		// MAIS le BanSystem côté C# garde un cache LocalData ("bans"), c'est lui
		// qui est consulté par AcceptConnection. Sans cette invalidation, le joueur
		// reste kické "You're banned" même après unban via le panel web.
		BanSystem.Current?.Unban( (SteamId)a.TargetSteamId );

		// Et le cache DarkDatabase._bans utilisé par l'admin panel ingame
		var db = DarkDatabase.Instance;
		if ( db is not null && db.GetBan( a.TargetSteamId ) is { } ban )
		{
			ban.IsActive = false;
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, null, "unban", "Ban levé depuis le panel" );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ JAIL / UNJAIL
	private static async Task<bool> HandleJailAsync( PendingAction a )
	{
		var player = FindPlayer( a.TargetSteamId );
		if ( player is null )
		{
			Log.Info( $"[Jail] Joueur {a.TargetSteamId} non connecté, jail ignoré." );
			return true; // pas d'erreur — sera persisté en BDD quand le joueur revient
		}

		var release = a.GetPayloadBool( "release" );
		var reason  = a.GetPayloadString( "reason" ) ?? "Arrest par admin";

		if ( release )
		{
			player.ReleaseFromArrest();
		}
		else
		{
			// BeginArrest accepte officer = null (le système dit "arrêté par the law")
			player.BeginArrest( null );
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, player.GameObject.Name,
			release ? "release" : "jail",
			reason );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ WARN
	private static async Task<bool> HandleWarnAsync( PendingAction a )
	{
		var player = FindPlayer( a.TargetSteamId );
		var reason = a.GetPayloadString( "reason" ) ?? "Avertissement";

		// Notif au joueur si connecté
		if ( player is not null )
		{
			Notices.SendNotice( player.Network.Owner, "warning", Color.Yellow,
				$"Avertissement : {reason}", 5 );
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, player?.GameObject.Name, "warn", reason );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ SLAY
	// Payload : { reason, admin_name }
	private static async Task<bool> HandleSlayAsync( PendingAction a )
	{
		var player = FindPlayer( a.TargetSteamId );
		var reason = a.GetPayloadString( "reason" ) ?? "Slay admin";

		if ( player is null )
		{
			Log.Info( $"[Slay] Joueur {a.TargetSteamId} non connecté, ignoré." );
			return true;
		}

		// Damage massif → tue le joueur instantanément (respecte les events Damaging/Dying)
		player.OnDamage( new DamageInfo( float.MaxValue, player.GameObject, null ) );

		if ( player.Network.Owner is not null )
		{
			Notices.SendNotice( player.Network.Owner, "warning", Color.Red,
				$"Slay admin : {reason}", 5 );
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, player.GameObject.Name, "slay", reason );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ FREEZE / UNFREEZE
	// Payload : { frozen: bool, admin_name }
	private static async Task<bool> HandleFreezeAsync( PendingAction a )
	{
		var player = FindPlayer( a.TargetSteamId );
		var frozen = a.GetPayloadBool( "frozen" );

		if ( player is null )
		{
			Log.Info( $"[Freeze] Joueur {a.TargetSteamId} non connecté." );
			return true; // pas d'erreur — l'état est persisté en BDD par le panel
		}

		player.SetFrozen( frozen );

		if ( player.Network.Owner is not null )
		{
			Notices.SendNotice( player.Network.Owner,
				frozen ? "warning" : "campaign",
				frozen ? Color.Orange : Color.Green,
				frozen ? "Tu as été gelé par un admin." : "Tu as été dégelé.",
				4 );
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, player.GameObject.Name,
			frozen ? "freeze" : "unfreeze",
			frozen ? "Gelé" : "Dégelé" );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ MUTE VOICE
	// Payload : { muted: bool, minutes: int, reason, admin_name }
	private static async Task<bool> HandleMuteVoiceAsync( PendingAction a )
	{
		var muted   = a.GetPayloadBool( "muted" );
		var minutes = (int) a.GetPayloadLong( "minutes" );
		var reason  = a.GetPayloadString( "reason" ) ?? "";

		var conn = FindConnection( a.TargetSteamId );
		if ( conn is null )
		{
			Log.Info( $"[MuteVoice] Joueur {a.TargetSteamId} non connecté. État persisté côté BDD." );
			return true;
		}

		// Apply au système Voice via la SteamId de la Connection (in-memory).
		SandboxVoice.SetMuted( conn.SteamId, muted );

		Notices.SendNotice( conn,
			muted ? "warning" : "campaign",
			muted ? Color.Orange : Color.Green,
			muted
				? $"Tu as été muté en vocal pour {minutes} min." + (reason != "" ? $"\nRaison : {reason}" : "")
				: "Tu as été démuté en vocal.",
			5 );

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, conn.DisplayName,
			muted ? "mute_voice" : "unmute_voice",
			muted ? $"{minutes} min — {reason}" : "Voice restauré" );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ MUTE CHAT
	// Payload : { muted: bool, minutes: int, reason, admin_name }
	private static async Task<bool> HandleMuteChatAsync( PendingAction a )
	{
		var muted   = a.GetPayloadBool( "muted" );
		var minutes = (int) a.GetPayloadLong( "minutes" );
		var reason  = a.GetPayloadString( "reason" ) ?? "";

		var player = FindPlayer( a.TargetSteamId );
		if ( player is null )
		{
			Log.Info( $"[MuteChat] Joueur {a.TargetSteamId} non connecté." );
			return true;
		}

		player.SetChatMuted( muted );

		if ( player.Network.Owner is not null )
		{
			Notices.SendNotice( player.Network.Owner,
				muted ? "warning" : "campaign",
				muted ? Color.Orange : Color.Green,
				muted
					? $"Tu as été muté du chat pour {minutes} min." + (reason != "" ? $"\nRaison : {reason}" : "")
					: "Tu as été démuté du chat.",
				5 );
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, player.GameObject.Name,
			muted ? "mute_chat" : "unmute_chat",
			muted ? $"{minutes} min — {reason}" : "Chat restauré" );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ SET JOB
	// Payload : { job_code, admin_name }
	// Le panel envoie le code du job (ex: "police"). On résout vers la JobDefinition
	// via JobDefinition.GetAll() pour trouver celle dont le code correspond.
	private static async Task<bool> HandleSetJobAsync( PendingAction a )
	{
		var player  = FindPlayer( a.TargetSteamId );
		var jobCode = a.GetPayloadString( "job_code" ) ?? "";

		if ( player is null )
		{
			Log.Info( $"[SetJob] Joueur {a.TargetSteamId} non connecté." );
			return true;
		}
		if ( string.IsNullOrWhiteSpace( jobCode ) )
		{
			Log.Warning( $"[SetJob] Job code vide pour {a.TargetSteamId}." );
			return false;
		}

		// Cherche la JobDefinition. Le panel peut envoyer :
		//   - le ResourcePath complet : "jobs/mob_boss.jobdef"
		//   - le ResourceName seul    : "mob_boss"
		// On essaie les 2 formes pour etre tolerant.
		var definition = ResourceLibrary.GetAll<JobDefinition>()
			.FirstOrDefault( j =>
				string.Equals( j.ResourcePath, jobCode, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( j.ResourceName, jobCode, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( j.ResourceName?.Replace( "_", "-" ), jobCode, StringComparison.OrdinalIgnoreCase )
			);

		if ( definition is null )
		{
			Log.Warning( $"[SetJob] JobDefinition '{jobCode}' introuvable côté C#." );
			await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
				a.TargetSteamId, player.GameObject.Name, "set_job",
				$"ÉCHEC : job '{jobCode}' introuvable" );
			return false;
		}

		player.SetJobDefinition( definition );

		if ( player.Network.Owner is not null )
		{
			Notices.SendNotice( player.Network.Owner, "campaign", Color.Cyan,
				$"Job changé par admin : {definition.Title}", 5 );
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, player.GameObject.Name, "set_job",
			$"→ {definition.Title} ({jobCode})" );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ SET MONEY
	private static async Task<bool> HandleSetMoneyAsync( PendingAction a )
	{
		var player = FindPlayer( a.TargetSteamId );
		var amount = (int)a.GetPayloadLong( "amount" );
		var mode   = a.GetPayloadString( "mode" ) ?? "set"; // 'set' | 'give' | 'take'

		// Met à jour la BDD (DarkDatabase synchronise automatiquement le Player s'il est connecté)
		var db = Game.ActiveScene?.GetAllComponents<DarkDatabase>().FirstOrDefault();
		if ( db is not null )
		{
			switch ( mode )
			{
				case "give": db.GiveMoney( a.TargetSteamId, amount ); break;
				case "take": db.GiveMoney( a.TargetSteamId, -amount ); break;
				case "set":
				default:     db.SetMoney( a.TargetSteamId, amount ); break;
			}
		}
		else if ( player is not null )
		{
			// Fallback si DarkDatabase introuvable : ajuste juste côté Player
			switch ( mode )
			{
				case "give": player.GiveMoney( amount ); break;
				case "take": player.TryTakeMoney( amount ); break;
				case "set":
				default:     player.SetMoney( amount ); break;
			}
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, player?.GameObject.Name, "set_money", $"{mode} ${amount}" );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ SET VIP
	private static async Task<bool> HandleSetVipAsync( PendingAction a )
	{
		var player = FindPlayer( a.TargetSteamId );
		var isVip = a.GetPayloadBool( "is_vip" );

		// Update BDD (qui propage au Player si connecté via DarkDatabase logic)
		var db = Game.ActiveScene?.GetAllComponents<DarkDatabase>().FirstOrDefault();
		db?.SetVip( a.TargetSteamId, isVip );

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, player?.GameObject.Name, "set_vip", isVip ? "VIP on" : "VIP off" );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ SET ROLE
	private static async Task<bool> HandleSetRoleAsync( PendingAction a )
	{
		var player = FindPlayer( a.TargetSteamId );
		var newRole = (StaffRole)(int)a.GetPayloadLong( "new_role" );

		if ( player is not null )
		{
			player.SetAdminRole( newRole.ToAdminRole() );
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, player?.GameObject.Name, "set_role",
			$"{a.GetPayloadLong( "old_role" )} → {(int)newRole}" );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ TELEPORT
	// Payload supporté :
	//   { "destination": "spawn" }                    → TP au spawn
	//   { "destination": "player", "steam_id": 765... } → TP vers un autre joueur
	//   { "destination": "coords", "x":0,"y":0,"z":0 } → TP coordonnées brutes
	private static async Task<bool> HandleTeleportAsync( PendingAction a )
	{
		var player = FindPlayer( a.TargetSteamId );
		if ( player is null )
		{
			Log.Info( $"[Teleport] Joueur {a.TargetSteamId} non connecté." );
			return true;
		}

		var dest = a.GetPayloadString( "destination" ) ?? "spawn";
		var success = false;
		var detail  = dest;

		switch ( dest )
		{
			case "spawn":
				var spawn = GameManager.Current?.FindSpawnLocation();
				if ( spawn.HasValue )
				{
					player.ServerTeleport( spawn.Value.Position, spawn.Value.Rotation );
					success = true;
				}
				break;

			case "player":
				var targetSid = a.GetPayloadLong( "steam_id" );
				var dst = FindPlayer( targetSid );
				if ( dst is not null )
				{
					success = player.ServerTeleportToPlayer( dst );
					detail  = $"vers {dst.GameObject.Name}";
				}
				break;

			case "coords":
				var pos = new Vector3(
					(float) a.GetPayloadLong( "x" ),
					(float) a.GetPayloadLong( "y" ),
					(float) a.GetPayloadLong( "z" )
				);
				player.ServerTeleport( pos );
				success = true;
				detail = $"({pos.x:0},{pos.y:0},{pos.z:0})";
				break;
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			a.TargetSteamId, player.GameObject.Name, "teleport", detail );
		return success;
	}

	// ═════════════════════════════════════════════════════════════ REFRESH JOBS
	// Déclenché par le panel quand un admin sauve une modification de job.
	// Refetch les overrides BDD et les applique aux JobDefinition en mémoire.
	private static async Task<bool> HandleRefreshJobsAsync( PendingAction a )
	{
		var count = await JobSyncService.RefreshOverridesAsync();
		if ( count < 0 )
		{
			Log.Warning( "[RefreshJobs] Échec du refresh." );
			return false;
		}

		Log.Info( $"[RefreshJobs] {count} job(s) actualisé(s) depuis le panel." );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ REFRESH ECONOMY
	// Déclenché par le panel via les boutons "🔄 Refresh ingame" sur /panel/economy/*
	// Payload : { scope: 'printers' | 'shops:weapon' | 'shops:shipment' | 'shops:ammo' | 'shops:misc' }
	// Recharge les overrides BDD et les ré-applique aux catalogues in-memory.
	private static async Task<bool> HandleRefreshEconomyAsync( PendingAction a )
	{
		var scope = a.GetPayloadString( "scope" ) ?? "";

		bool ok;
		if ( scope == "printers" )
		{
			ok = await EconomyOverrideStore.RefreshPrintersAsync();
		}
		else if ( scope.StartsWith( "shops:" ) )
		{
			var category = scope.Substring( "shops:".Length );
			ok = await EconomyOverrideStore.RefreshShopsAsync( category );
		}
		else
		{
			// Scope vide ou inconnu : refresh complet
			ok = await EconomyOverrideStore.RefreshAllAsync();
		}

		if ( !ok )
		{
			Log.Warning( $"[RefreshEconomy] Échec partiel du refresh scope='{scope}'." );
		}
		return true; // toujours considérer traité (sinon l'action sera retentée en boucle)
	}

	// ═════════════════════════════════════════════════════════════ ANNONCE
	private static async Task<bool> HandleAnnounceAsync( PendingAction a )
	{
		var title = a.GetPayloadString( "title" ) ?? "Annonce";
		var body  = a.GetPayloadString( "body" )  ?? "";

		// Broadcast à tous les joueurs connectés
		foreach ( var c in Connection.All )
		{
			Notices.SendNotice( c, "campaign", Color.Cyan, $"{title}\n{body}", 10 );
		}

		await DarkHttpClient.LogAdminActionAsync( a.CreatedBy, a.GetPayloadString( "admin_name" ),
			null, null, "announce", title );
		return true;
	}

	// ═════════════════════════════════════════════════════════════ UNKNOWN
	private static bool HandleUnknown( PendingAction a )
	{
		Log.Warning( $"[PendingActionDispatcher] Action inconnue : '{a.Action}' (id={a.Id})" );
		return false;
	}
}
