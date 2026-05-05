using System.Text.Json;
using Sandbox.UI;

namespace Sandbox;

public sealed partial class DarkDatabase
{
	// ── Getters ─────────────────────────────────────────────────────────
	public StaffRole GetRole( long steamId )
	{
		if ( _roles.TryGetValue( steamId, out var role ) ) return role;

		// Fallback : lire depuis le fichier joueur
		var record = GetPlayer( steamId );
		return record?.StaffRole ?? StaffRole.Player;
	}

	public StaffRole GetRole( Connection connection ) =>
		GetRole( (long)connection.SteamId.Value );

	public bool HasPanelAccess( long steamId ) => GetRole( steamId ).HasPanelAccess();

	// ── Setters ─────────────────────────────────────────────────────────
	public void SetRole( long steamId, StaffRole role, Connection admin = null )
	{
		Assert.True( Networking.IsHost );

		// Un fondateur ne peut pas être rétrogradé sauf par un autre fondateur
		var currentRole = GetRole( steamId );
		if ( currentRole.IsFounder() && admin is not null && !GetRole( admin ).IsFounder() )
		{
			Log.Warning( "[DarkDatabase] Seul un fondateur peut modifier le rôle d'un fondateur." );
			return;
		}

		_roles[steamId] = role;

		// Persister dans le fichier joueur
		if ( _players.TryGetValue( steamId, out var record ) )
		{
			record.StaffRole = role;
			SavePlayer( record );
		}

		SaveRoles();

		// Synchroniser avec AdminSystem existant
		SyncRoleToAdminSystem( steamId, role );

		var targetName = GetPlayer( steamId )?.SteamName ?? steamId.ToString();
		LogAction( admin, targetName, steamId, "SET_ROLE", role.GetLabel() );

		// Notifier l'admin
		if ( admin is not null )
		{
			using ( Rpc.FilterInclude( admin ) )
			{
				Notices.AddNotice( "security", role.GetColor(),
					$"{targetName} est maintenant {role.GetLabel()}.", 3f );
			}
		}

		Log.Info( $"[DarkDatabase] Rôle '{role.GetLabel()}' attribué à {steamId}" );
	}

	// ── Synchro AdminSystem ─────────────────────────────────────────────
	void SyncRoleToAdminSystem( long steamId, StaffRole staffRole )
	{
		var adminSystem = AdminSystem.Current;
		if ( adminSystem is null ) return;

		var adminRole   = staffRole.ToAdminRole();
		var steamId64   = (SteamId)steamId;
		var conn        = Connection.All.FirstOrDefault( c => (long)c.SteamId.Value == steamId );
		var displayName = conn?.DisplayName ?? steamId.ToString();

		adminSystem.SetRole( steamId64, adminRole, displayName );

		// Rafraîchir le composant Player en ligne
		if ( conn is not null )
		{
			var player = Player.FindForConnection( conn );
			if ( player.IsValid() )
				player.SetAdminRole( adminRole );
		}
	}

	// ── Persistance ─────────────────────────────────────────────────────
	void LoadRoles()
	{
		if ( !FileSystem.Data.FileExists( RolesFile ) ) return;
		try
		{
			var json    = FileSystem.Data.ReadAllText( RolesFile );
			var records = JsonSerializer.Deserialize<Dictionary<string, int>>( json ) ?? new();
			foreach ( var (key, val) in records )
			{
				if ( long.TryParse( key, out var id ) )
					_roles[id] = (StaffRole)val;
			}
			Log.Info( $"[DarkDatabase] {_roles.Count} rôle(s) chargé(s)." );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "[DarkDatabase] Impossible de charger les rôles." );
		}
	}

	void SaveRoles()
	{
		try
		{
			var dict = _roles.ToDictionary( x => x.Key.ToString(), x => (int)x.Value );
			var json = JsonSerializer.Serialize( dict, new JsonSerializerOptions { WriteIndented = true } );
			FileSystem.Data.WriteAllText( RolesFile, json );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "[DarkDatabase] Impossible de sauvegarder les rôles." );
		}
	}
}
