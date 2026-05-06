using Sandbox.UI;

namespace Sandbox;

public sealed partial class DarkDatabase
{
	// ── Ban par connexion active ─────────────────────────────────────────────
	public BanRecord BanPlayer(
		Connection target,
		string     reason,
		Connection admin    = null,
		TimeSpan?  duration = null,
		bool       banIp    = false )
	{
		Assert.True( Networking.IsHost );

		var steamId   = (long)target.SteamId.Value;
		var adminId   = admin is not null ? (long?)admin.SteamId.Value : null;
		var adminName = admin?.DisplayName;

		var record = CreateBanRecord(
			steamId, target.DisplayName,
			target.Address, banIp,
			reason, adminId, adminName, duration );

		// Mise à jour cache + MySQL
		_bans[steamId] = record;
		_ = SaveBanAsync( record );

		LogAction( admin, target.DisplayName, steamId, "BAN",
			$"{reason} | Durée: {record.FormatDuration()} | IP: {banIp}" );

		// Kick immédiat
		target.Kick( $"Banni : {reason}" );

		Scene.Get<Chat>()?.AddSystemText(
			$"🔨 {target.DisplayName} a été banni ({record.FormatDuration()}) : {reason}", null );

		return record;
	}

	// ── Ban par SteamId (joueur hors ligne) ─────────────────────────────────
	public BanRecord BanSteamId(
		long       steamId,
		string     displayName,
		string     reason,
		Connection admin    = null,
		TimeSpan?  duration = null )
	{
		Assert.True( Networking.IsHost );

		var adminId   = admin is not null ? (long?)admin.SteamId.Value : null;
		var adminName = admin?.DisplayName;

		var record = CreateBanRecord(
			steamId, displayName,
			null, false,
			reason, adminId, adminName, duration );

		_bans[steamId] = record;
		_ = SaveBanAsync( record );

		LogAction( admin, displayName, steamId, "BAN_OFFLINE",
			$"{reason} | Durée: {record.FormatDuration()}" );

		return record;
	}

	// ── Unban ───────────────────────────────────────────────────────────────
	public bool Unban( long steamId, Connection admin = null )
	{
		Assert.True( Networking.IsHost );

		if ( !_bans.TryGetValue( steamId, out var ban ) ) return false;

		ban.IsActive = false;
		_ = DarkHttpClient.PatchAsync( $"bans/{steamId}/unban", new { } );

		LogAction( admin, ban.DisplayName, steamId, "UNBAN", "" );
		Log.Info( $"[DarkDatabase] Unban : {ban.DisplayName} ({steamId})" );
		return true;
	}

	// ── Getters ─────────────────────────────────────────────────────────────
	public bool IsBanned( long steamId ) =>
		_bans.TryGetValue( steamId, out var b ) && b.IsActive;

	public BanRecord GetBan( long steamId ) =>
		_bans.TryGetValue( steamId, out var b ) ? b : null;

	public IReadOnlyList<BanRecord> GetActiveBans() =>
		_bans.Values.Where( b => b.IsActive ).ToList().AsReadOnly();

	// ── Construction interne ────────────────────────────────────────────────
	static BanRecord CreateBanRecord(
		long      steamId,
		string    displayName,
		string    ip,
		bool      banIp,
		string    reason,
		long?     adminId,
		string    adminName,
		TimeSpan? duration )
	{
		return new BanRecord
		{
			SteamId      = steamId,
			Ip           = banIp ? ip : null,
			DisplayName  = displayName,
			Reason       = string.IsNullOrWhiteSpace( reason ) ? "Banni" : reason.Trim(),
			AdminSteamId = adminId,
			AdminName    = adminName,
			ExpiresAt    = duration.HasValue ? DateTime.UtcNow + duration.Value : null,
			IsActive     = true,
		};
	}

	// ── Persistance HTTP ────────────────────────────────────────────────────

	/// <summary>Charge tous les bans actifs depuis MySQL et remplit le cache.</summary>
	async Task LoadBansAsync()
	{
		var bans = await DarkHttpClient.GetAsync<List<BanRecord>>( "bans" );
		if ( bans is null )
		{
			Log.Warning( "[DarkDatabase] Impossible de charger les bans depuis MySQL." );
			return;
		}

		_bans.Clear();
		foreach ( var b in bans )
		{
			if ( b.SteamId.HasValue )
				_bans[b.SteamId.Value] = b;
		}

		Log.Info( $"[DarkDatabase] {_bans.Count} ban(s) actif(s) chargé(s)." );
	}

	/// <summary>Enregistre un nouveau ban dans MySQL.</summary>
	async Task SaveBanAsync( BanRecord record )
	{
		var payload = new
		{
			steam_id       = record.SteamId,
			ip             = record.Ip,
			display_name   = record.DisplayName,
			reason         = record.Reason,
			admin_steam_id = record.AdminSteamId,
			admin_name     = record.AdminName,
			expires_at     = record.ExpiresAt?.ToString( "o" ),
		};

		var ok = await DarkHttpClient.PostAsync( "bans", payload );
		if ( !ok )
			Log.Warning( $"[DarkDatabase] Impossible de sauvegarder le ban pour {record.SteamId}" );
	}
}
