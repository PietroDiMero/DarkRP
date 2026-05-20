using Sandbox.UI;

/// <summary>
/// Holds a banlist, can ban users.
/// </summary>
public sealed class BanSystem : GameObjectSystem<BanSystem>, Component.INetworkListener
{
	public record struct BanEntry( string DisplayName, string Reason );

	private Dictionary<long, BanEntry> _bans = new();
	private Dictionary<long, BanEntry> _visibleBans = new();

	public int BanListRevision { get; private set; }

	public BanSystem( Scene scene ) : base( scene )
	{
		_bans = LocalData.Get<Dictionary<long, BanEntry>>( "bans", new() ) ?? new();
	}

	bool Component.INetworkListener.AcceptConnection( Connection connection, ref string reason )
	{
		if ( !_bans.TryGetValue( connection.SteamId, out var entry ) )
			return true;

		// MySQL est la source de verite. Si DarkDatabase dit que ce ban est inactif/inexistant,
		// le BanSystem LocalData est perime (l'unban via panel n'avait pas invalide ce cache).
		// On nettoie le cache local et on autorise la connexion.
		var db = Sandbox.DarkDatabase.Instance;
		if ( db is not null )
		{
			var sid = (long)connection.SteamId.Value;
			var dbBan = db.GetBan( sid );
			if ( dbBan is null || !dbBan.IsActive )
			{
				_bans.Remove( connection.SteamId );
				Save();
				SendBannedListToAdmins();
				Sandbox.Log.Info( $"[BanSystem] Cache LocalData perime pour {sid} (MySQL = pas de ban actif). Cache nettoye, connexion acceptee." );
				return true;
			}
		}

		reason = $"You're banned from this server: {entry.Reason}";
		return false;
	}

	/// <summary>
	/// Bans a connected player and kicks them immediately.
	/// </summary>
	public void Ban( Connection connection, string reason )
	{
		Assert.True( Networking.IsHost, "Only the host may ban players." );

		_bans[connection.SteamId] = new BanEntry( connection.DisplayName, reason );
		Save();
		SendBannedListToAdmins();
		Scene.Get<Chat>()?.AddSystemText( $"{connection.DisplayName} was banned: {reason}", "🔨" );
		connection.Kick( reason );
	}

	/// <summary>
	/// Bans a Steam ID by value. Use for pre-banning or banning players who are not currently connected.
	/// Display name falls back to the Steam ID string.
	/// </summary>
	public void Ban( SteamId steamId, string reason )
	{
		Assert.True( Networking.IsHost, "Only the host may ban players." );

		_bans[steamId] = new BanEntry( steamId.ToString(), reason );
		Save();
		SendBannedListToAdmins();
	}

	/// <summary>
	/// Removes the ban for the given Steam ID.
	/// </summary>
	public void Unban( SteamId steamId )
	{
		Assert.True( Networking.IsHost, "Only the host may unban players." );

		if ( _bans.Remove( steamId ) )
		{
			Save();
			SendBannedListToAdmins();
		}
	}

	/// <summary>
	/// Returns true if the given Steam ID is currently banned.
	/// </summary>
	public bool IsBanned( SteamId steamId ) => _bans.ContainsKey( steamId );

	/// <summary>
	/// Returns a read-only view of all active bans visible to this instance.
	/// </summary>
	public IReadOnlyDictionary<SteamId, BanEntry> GetBannedList()
	{
		var bans = Networking.IsHost ? _bans : _visibleBans;
		return bans.ToDictionary( x => (SteamId)x.Key, x => x.Value );
	}

	private void Save() => LocalData.Set( "bans", _bans );

	[Rpc.Host]
	public static void RpcRequestBannedList()
	{
		if ( AdminSystem.Current?.HasAdminAccess( Rpc.Caller ) != true )
			return;

		Current?.SendBannedList( Rpc.Caller );
	}

	[Rpc.Host]
	public static void RpcUnban( long steamId )
	{
		if ( AdminSystem.Current?.HasSuperAdminAccess( Rpc.Caller ) != true || steamId <= 0 )
			return;

		Current?.Unban( (SteamId)steamId );
	}

	void SendBannedListToAdmins()
	{
		foreach ( var connection in Connection.All )
		{
			if ( AdminSystem.Current?.HasAdminAccess( connection ) != true )
				continue;

			SendBannedList( connection );
		}
	}

	void SendBannedList( Connection connection )
	{
		if ( connection is null )
			return;

		var payload = Json.Serialize( _bans );

		using ( Rpc.FilterInclude( connection ) )
		{
			ReceiveBannedList( payload );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	static void ReceiveBannedList( string payload )
	{
		if ( Current is null )
			return;

		Current._visibleBans = Json.Deserialize<Dictionary<long, BanEntry>>( payload ) ?? new();
		Current.BanListRevision++;
	}

	/// <summary>
	/// RPC to ban a connected player. Caller must have DarkRP superadmin access.
	/// </summary>
	[Rpc.Host]
	public static void RpcBanPlayer( Connection target, string reason = "Banned" )
	{
		if ( Current is null )
			return;

		if ( AdminSystem.Current?.HasSuperAdminAccess( Rpc.Caller ) != true )
			return;

		if ( target is null || target.IsHost || target == Rpc.Caller )
			return;

		var finalReason = string.IsNullOrWhiteSpace( reason ) ? "Banned" : reason.Trim();
		Current.Ban( target, finalReason );
		Notices.SendNotice( Rpc.Caller, "gavel", Color.Green, $"{target.DisplayName} was banned.", 3 );
	}

	/// <summary>
	/// Bans a player by name or Steam ID. Optionally provide a reason.
	/// Usage: ban [name|steamid] [reason]
	/// </summary>
	[ConCmd( "ban" )]
	public static void BanCommand( string target, string reason = "Banned" )
	{
		if ( !Networking.IsHost ) return;

		// Try parsing as a Steam ID (64-bit integer) first.
		if ( ulong.TryParse( target, out var steamIdValue ) )
		{
			var steamId = steamIdValue;
			var connection = Connection.All.FirstOrDefault( c => c.SteamId == steamId );

			if ( connection is not null )
				Current.Ban( connection, reason );
			else
				Current.Ban( steamId, reason );

			Log.Info( $"Banned {steamId}: {reason}" );
			return;
		}

		// Fall back to partial name match.
		var conn = GameManager.FindPlayerWithName( target );
		if ( conn is not null )
		{
			Current.Ban( conn, reason );
			Log.Info( $"Banned {conn.DisplayName}: {reason}" );
		}
		else
		{
			Log.Warning( $"Could not find player '{target}'" );
		}
	}
}
