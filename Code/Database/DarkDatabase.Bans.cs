using System.Text.Json;
using Sandbox.UI;

namespace Sandbox;

public sealed partial class DarkDatabase
{
	// ── Ban par connexion active ─────────────────────────────────────────
	public BanRecord BanPlayer(
		Connection target,
		string reason,
		Connection admin        = null,
		TimeSpan?  duration     = null,
		bool       banIp        = false )
	{
		Assert.True( Networking.IsHost );

		var steamId  = (long)target.SteamId.Value;
		var adminId  = admin is not null ? (long?)admin.SteamId.Value : null;
		var adminName = admin?.DisplayName;

		var record = CreateBanRecord(
			steamId, target.DisplayName,
			target.Address, banIp,
			reason, adminId, adminName, duration );

		_bans[steamId] = record;
		SaveBans();

		LogAction( admin, target.DisplayName, steamId, "BAN",
			$"{reason} | Durée: {record.FormatDuration()} | IP: {banIp}" );

		// Kick immédiat
		target.Kick( $"Banni : {reason}" );

		Scene.Get<Chat>()?.AddSystemText(
			$"🔨 {target.DisplayName} a été banni ({record.FormatDuration()}) : {reason}", null );

		return record;
	}

	// ── Ban par SteamId (hors ligne) ────────────────────────────────────
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
		SaveBans();
		LogAction( admin, displayName, steamId, "BAN_OFFLINE",
			$"{reason} | Durée: {record.FormatDuration()}" );

		return record;
	}

	// ── Unban ───────────────────────────────────────────────────────────
	public bool Unban( long steamId, Connection admin = null )
	{
		Assert.True( Networking.IsHost );

		if ( !_bans.TryGetValue( steamId, out var ban ) ) return false;

		ban.IsActive = false;
		SaveBans();
		LogAction( admin, ban.DisplayName, steamId, "UNBAN", "" );

		Log.Info( $"[DarkDatabase] Unban : {ban.DisplayName} ({steamId})" );
		return true;
	}

	// ── Getters ─────────────────────────────────────────────────────────
	public bool IsBanned( long steamId )
	{
		return _bans.TryGetValue( steamId, out var b ) && b.IsActive;
	}

	public BanRecord GetBan( long steamId ) =>
		_bans.TryGetValue( steamId, out var b ) ? b : null;

	public IReadOnlyList<BanRecord> GetActiveBans() =>
		_bans.Values.Where( b => b.IsActive ).ToList().AsReadOnly();

	// ── Construction interne ────────────────────────────────────────────
	static BanRecord CreateBanRecord(
		long    steamId,
		string  displayName,
		string  ip,
		bool    banIp,
		string  reason,
		long?   adminId,
		string  adminName,
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

	// ── Persistance ─────────────────────────────────────────────────────
	void LoadBans()
	{
		if ( !FileSystem.Data.FileExists( BansFile ) ) return;
		try
		{
			var json    = FileSystem.Data.ReadAllText( BansFile );
			var records = JsonSerializer.Deserialize<List<BanRecord>>( json ) ?? new();
			foreach ( var r in records )
			{
				if ( r.SteamId.HasValue )
					_bans[r.SteamId.Value] = r;
			}
			Log.Info( $"[DarkDatabase] {_bans.Count} ban(s) chargé(s)." );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "[DarkDatabase] Impossible de charger les bans." );
		}
	}

	void SaveBans()
	{
		try
		{
			var json = JsonSerializer.Serialize(
				_bans.Values.ToList(),
				new JsonSerializerOptions { WriteIndented = true } );
			FileSystem.Data.WriteAllText( BansFile, json );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "[DarkDatabase] Impossible de sauvegarder les bans." );
		}
	}
}
