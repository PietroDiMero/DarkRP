using Sandbox.UI;

public sealed partial class Player
{
	[Property, Sync( SyncFlags.FromHost )]
	public AdminRole AdminRole { get; private set; } = AdminRole.None;

	public bool HasAdminAccess => (Network.Owner?.IsHost ?? false) || AdminRole >= AdminRole.Admin;
	public bool HasSuperAdminAccess => (Network.Owner?.IsHost ?? false) || AdminRole >= AdminRole.SuperAdmin;

	public void SetAdminRole( AdminRole role )
	{
		if ( !Networking.IsHost )
			return;

		AdminRole = role;
	}

	[Rpc.Host]
	public void RequestKickPlayer( long steamId, string reason )
	{
		if ( !AdminSystem.Current.HasAdminAccess( Rpc.Caller ) || steamId <= 0 )
			return;

		var connection = Connection.All.FirstOrDefault( x => x.SteamId.Value == steamId );
		if ( connection is null || connection.IsHost || connection == Rpc.Caller )
			return;

		var finalReason = string.IsNullOrWhiteSpace( reason ) ? "Kicked" : reason.Trim();
		GameManager.Current?.Kick( connection, finalReason );
		Notices.SendNotice( Rpc.Caller, "person_remove", Color.Green, $"{connection.DisplayName} was kicked.", 3 );
	}

	[Rpc.Host]
	public void RequestBanPlayer( long steamId, string reason )
	{
		if ( !AdminSystem.Current.HasSuperAdminAccess( Rpc.Caller ) || steamId <= 0 )
			return;

		var connection = Connection.All.FirstOrDefault( x => x.SteamId.Value == steamId );
		if ( connection is null || connection.IsHost || connection == Rpc.Caller )
			return;

		var finalReason = string.IsNullOrWhiteSpace( reason ) ? "Banned" : reason.Trim();
		BanSystem.Current?.Ban( connection, finalReason );
		Notices.SendNotice( Rpc.Caller, "gavel", Color.Green, $"{connection.DisplayName} was banned.", 3 );
	}

	/// <summary>
	/// Téléporte le joueur (côté host uniquement). Utilisé par les actions panel.
	/// </summary>
	public void ServerTeleport( Vector3 position, Rotation? rotation = null )
	{
		if ( !Networking.IsHost ) return;
		ApplyPlayerTeleport( new Transform( position, rotation ?? Rotation.Identity ) );
	}

	/// <summary>Téléporte ce joueur à la position d'un autre joueur connecté.</summary>
	public bool ServerTeleportToPlayer( Player target )
	{
		if ( !Networking.IsHost || target is null || !target.GameObject.IsValid() ) return false;
		ServerTeleport( target.WorldPosition, target.WorldRotation );
		return true;
	}

	[Rpc.Host]
	public void RequestSetAdminRole( long steamId, AdminRole role )
	{
		if ( !AdminSystem.Current.HasSuperAdminAccess( Rpc.Caller ) || steamId <= 0 )
			return;

		var targetSteamId = (SteamId)steamId;
		var connection = Connection.All.FirstOrDefault( x => x.SteamId == targetSteamId );
		if ( connection?.IsHost == true )
			return;

		var displayName = connection?.DisplayName ?? targetSteamId.ToString();
		AdminSystem.Current.SetRole( targetSteamId, role, displayName );

		var roleText = role switch
		{
			AdminRole.Admin => "set as admin",
			AdminRole.SuperAdmin => "set as superadmin",
			_ => "removed from staff"
		};

        Notices.SendNotice( Rpc.Caller, role == AdminRole.SuperAdmin ? "stars" : "security", Color.Green, $"{displayName} {roleText}.", 3 );
	}
}
