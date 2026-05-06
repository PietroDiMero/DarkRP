using Sandbox.UI;

namespace Sandbox;

public sealed partial class DarkDatabase
{
	// ── Getters ─────────────────────────────────────────────────────────────
	public StaffRole GetRole( long steamId )
	{
		if ( _roles.TryGetValue( steamId, out var role ) ) return role;

		// Fallback : lire depuis le cache joueur
		var record = GetPlayer( steamId );
		return record?.StaffRole ?? StaffRole.Player;
	}

	public StaffRole GetRole( Connection connection ) =>
		GetRole( (long)connection.SteamId.Value );

	public bool HasPanelAccess( long steamId ) => GetRole( steamId ).HasPanelAccess();

	// ── Setters ─────────────────────────────────────────────────────────────
	public void SetRole( long steamId, StaffRole role, Connection admin = null )
	{
		Assert.True( Networking.IsHost );

		// Sécurité : seul un fondateur peut modifier le rôle d'un autre fondateur
		var currentRole = GetRole( steamId );
		if ( currentRole.IsFounder() && admin is not null && !GetRole( admin ).IsFounder() )
		{
			Log.Warning( "[DarkDatabase] Seul un fondateur peut modifier le rôle d'un fondateur." );
			return;
		}

		// Mise à jour cache
		_roles[steamId] = role;

		// Mise à jour du PlayerRecord en cache
		if ( _players.TryGetValue( steamId, out var record ) )
			record.StaffRole = role;

		// Persistance MySQL — on utilise PATCH /players/{id}/role (plus léger qu'un upsert complet)
		_ = DarkHttpClient.PatchAsync( $"players/{steamId}/role", new { staff_role = (int)role } );

		// Synchroniser avec AdminSystem existant du gamemode
		SyncRoleToAdminSystem( steamId, role );

		var targetName = GetPlayer( steamId )?.SteamName ?? steamId.ToString();
		LogAction( admin, targetName, steamId, "SET_ROLE", role.GetLabel() );

		// Notification à l'admin
		if ( admin is not null )
		{
			using ( Rpc.FilterInclude( admin ) )
			{
				Notices.AddNotice( "security", role.GetColor(),
					$"{targetName} est maintenant {role.GetLabel()}.", 3f );
			}
		}

		Log.Info( $"[DarkDatabase] Rôle '{role.GetLabel()}' → {steamId}" );
	}

	// ── Synchro AdminSystem du gamemode ─────────────────────────────────────
	void SyncRoleToAdminSystem( long steamId, StaffRole staffRole )
	{
		var adminSystem = AdminSystem.Current;
		if ( adminSystem is null ) return;

		var adminRole   = staffRole.ToAdminRole();
		var steamId64   = (SteamId)steamId;
		var conn        = Connection.All.FirstOrDefault( c => (long)c.SteamId.Value == steamId );
		var displayName = conn?.DisplayName ?? steamId.ToString();

		adminSystem.SetRole( steamId64, adminRole, displayName );

		if ( conn is not null )
		{
			var player = Player.FindForConnection( conn );
			if ( player.IsValid() )
				player.SetAdminRole( adminRole );
		}
	}

	// ── Persistance HTTP ────────────────────────────────────────────────────

	/// <summary>
	/// Charge les rôles staff depuis MySQL.
	/// Les rôles sont stockés dans la colonne staff_role de la table players.
	/// On charge uniquement les joueurs avec un rôle > 0 (Player).
	/// </summary>
	async Task LoadRolesAsync()
	{
		// On charge tous les joueurs pour récupérer leurs rôles
		// (évite une requête séparée — les joueurs sont déjà stockés dans players)
		var players = await DarkHttpClient.GetAsync<List<PlayerRecord>>( "players" );
		if ( players is null )
		{
			Log.Warning( "[DarkDatabase] Impossible de charger les rôles depuis MySQL." );
			return;
		}

		_roles.Clear();
		foreach ( var p in players )
		{
			if ( p.StaffRole > StaffRole.Player )
				_roles[p.SteamId] = p.StaffRole;
		}

		Log.Info( $"[DarkDatabase] {_roles.Count} rôle(s) staff chargé(s)." );
	}
}
