using System.Linq;
using System.Threading.Tasks;
using Sandbox.UI;

namespace Sandbox;

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
	/// Dispatch + exécute une action. Retourne true si OK, false sinon.
	/// </summary>
	public static async Task<bool> DispatchAsync( PendingAction action )
	{
		if ( action is null || string.IsNullOrEmpty( action.Action ) ) return false;

		try
		{
			return action.Action switch
			{
				"kick"      => await HandleKickAsync( action ),
				"ban"       => await HandleBanAsync( action ),
				"unban"     => await HandleUnbanAsync( action ),
				"jail"      => await HandleJailAsync( action ),
				"warn"      => await HandleWarnAsync( action ),
				"set_money" => await HandleSetMoneyAsync( action ),
				"set_vip"   => await HandleSetVipAsync( action ),
				"set_role"  => await HandleSetRoleAsync( action ),
				"teleport"  => await HandleTeleportAsync( action ),
				"announce"  => await HandleAnnounceAsync( action ),
				_           => HandleUnknown( action ),
			};
		}
		catch ( System.Exception ex )
		{
			Log.Warning( ex, $"[PendingActionDispatcher] Exception sur action '{action.Action}'." );
			return false;
		}
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
		// Côté jeu, on n'a juste rien à faire sauf si on a un cache.
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
