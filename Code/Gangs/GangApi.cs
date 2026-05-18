using System.Threading.Tasks;
using Sandbox;

// ═══════════════════════════════════════════════════════════════════════════════
//  Wrapper typé pour les routes /api/gangs/* du sidecar PHP.
//  Toutes les méthodes sont async ; les opérations sensibles renvoient un
//  ApiResult<T> avec Error si quelque chose a foiré (pour relayer côté chat).
// ═══════════════════════════════════════════════════════════════════════════════

public static class GangApi
{
	// ── Lookups ────────────────────────────────────────────────────────────
	public static Task<GangDto[]> ListActiveAsync()
		=> DarkHttpClient.GetAsync<GangDto[]>( "gangs?status=active" );

	public static Task<GangDto> GetByIdAsync( int gangId )
		=> DarkHttpClient.GetAsync<GangDto>( $"gangs/{gangId}" );

	public static Task<GangDto> GetByTagAsync( string tag )
		=> DarkHttpClient.GetAsync<GangDto>( $"gangs/by-tag/{System.Uri.EscapeDataString( tag )}" );

	public static async Task<GangDto> GetByMemberAsync( long steamId )
	{
		var resp = await DarkHttpClient.GetAsync<GangByMemberResponse>( $"gangs/by-member/{steamId}" );
		return resp?.Gang;
	}

	public static Task<GangMemberDto[]> ListMembersAsync( int gangId )
		=> DarkHttpClient.GetAsync<GangMemberDto[]>( $"gangs/{gangId}/members" );

	public static Task<GangInviteDto[]> ListInvitesAsync( int gangId, string status = "pending" )
		=> DarkHttpClient.GetAsync<GangInviteDto[]>( $"gangs/{gangId}/invites?status={status}" );

	public static Task<GangInviteDto[]> ListInvitesByTargetAsync( long steamId, string status = "pending" )
		=> DarkHttpClient.GetAsync<GangInviteDto[]>( $"gangs/invites/by-target/{steamId}?status={status}" );

	public static Task<GangTreasuryLogDto[]> ListTreasuryAsync( int gangId, int limit = 50 )
		=> DarkHttpClient.GetAsync<GangTreasuryLogDto[]>( $"gangs/{gangId}/treasury?limit={limit}" );

	public static async Task<bool> IsFounderBannedAsync( long steamId )
	{
		var resp = await DarkHttpClient.GetAsync<GangFounderBanCheckResponse>( $"gangs/founder-banned/{steamId}" );
		return resp?.Banned == true;
	}

	// ── Création / dissolution ─────────────────────────────────────────────
	public static Task<DarkHttpClient.ApiResult<GangDto>> CreateAsync( string name, string tag, long founderSteamId, string color = null, string description = null )
		=> DarkHttpClient.PostWithResultAsync<GangDto>( "gangs", new
		{
			name,
			tag,
			founder_steamid = founderSteamId,
			color           = color ?? "#5eb3ff",
			description,
		} );

	public static Task<bool> DissolveAsync( int gangId, string reason = null )
		=> DarkHttpClient.PostAsync( $"gangs/{gangId}/dissolve", new { reason } );

	// ── Update settings (description / color / taux taxe) ──────────────────
	public static Task<bool> UpdateTaxAsync( int gangId, int pct )
		=> DarkHttpClient.PatchAsync( $"gangs/{gangId}", new { tax_pct_criminal = pct } );

	public static Task<bool> UpdateMetaAsync( int gangId, string description = null, string color = null )
	{
		var body = new System.Collections.Generic.Dictionary<string, object>();
		if ( description is not null ) body["description"] = description;
		if ( color       is not null ) body["color"]       = color;
		return DarkHttpClient.PatchAsync( $"gangs/{gangId}", body );
	}

	// ── Membres ────────────────────────────────────────────────────────────
	public static Task<DarkHttpClient.ApiResult<object>> AddMemberAsync( int gangId, long steamId, string role = "member" )
		=> DarkHttpClient.PostWithResultAsync<object>( $"gangs/{gangId}/members", new { steamid = steamId, role } );

	public static Task<bool> RemoveMemberAsync( int gangId, long steamId )
		=> DarkHttpClient.DeleteAsync( $"gangs/{gangId}/members/{steamId}" );

	public static Task<bool> SetMemberRoleAsync( int gangId, long steamId, string role )
		=> DarkHttpClient.PatchAsync( $"gangs/{gangId}/members/{steamId}/role", new { role } );

	// ── Invitations ────────────────────────────────────────────────────────
	public static Task<bool> CreateInviteAsync( int gangId, long target, long inviter, int expiresInSeconds = 300 )
		=> DarkHttpClient.PostAsync( $"gangs/{gangId}/invites", new
		{
			target_steamid     = target,
			inviter_steamid    = inviter,
			expires_in_seconds = expiresInSeconds,
		} );

	public static Task<bool> PatchInviteAsync( int gangId, int inviteId, string status )
		=> DarkHttpClient.PatchAsync( $"gangs/{gangId}/invites/{inviteId}", new { status } );

	// ── Caisse (treasury) ──────────────────────────────────────────────────
	public static Task<DarkHttpClient.ApiResult<GangTreasuryResponse>> RecordTreasuryAsync(
		int gangId, string type, long amount, long? steamId = null, string reason = null )
		=> DarkHttpClient.PostWithResultAsync<GangTreasuryResponse>( $"gangs/{gangId}/treasury", new
		{
			type,
			amount,
			steamid = steamId,
			reason,
		} );
}
