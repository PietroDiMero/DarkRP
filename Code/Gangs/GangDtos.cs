using System.Text.Json.Serialization;

// ═══════════════════════════════════════════════════════════════════════════════
//  DTOs sérialisés/désérialisés depuis le sidecar PHP /api/gangs/*.
//  Les attributs [JsonPropertyName] mappent snake_case ↔ PascalCase
//  car DarkHttpClient n'utilise pas de SnakeCaseNamingPolicy.
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class GangDto
{
	[JsonPropertyName( "id" )]                public int    Id              { get; set; }
	[JsonPropertyName( "name" )]              public string Name            { get; set; }
	[JsonPropertyName( "tag" )]               public string Tag             { get; set; }
	[JsonPropertyName( "description" )]       public string Description     { get; set; }
	[JsonPropertyName( "color" )]             public string Color           { get; set; }
	[JsonPropertyName( "founder_steamid" )]   public string FounderSteamId  { get; set; }
	[JsonPropertyName( "balance" )]           public long   Balance         { get; set; }
	[JsonPropertyName( "tax_pct_criminal" )]  public int    TaxPctCriminal  { get; set; }
	[JsonPropertyName( "member_limit" )]      public int    MemberLimit     { get; set; }
	[JsonPropertyName( "status" )]            public string Status          { get; set; }
	[JsonPropertyName( "dissolved_at" )]      public string DissolvedAt     { get; set; }
	[JsonPropertyName( "dissolved_reason" )]  public string DissolvedReason { get; set; }
	[JsonPropertyName( "created_at" )]        public string CreatedAt       { get; set; }
	[JsonPropertyName( "updated_at" )]        public string UpdatedAt       { get; set; }
	[JsonPropertyName( "member_count" )]      public int    MemberCount     { get; set; }

	// Champs supplémentaires pour /gangs/by-member/{steamid}
	[JsonPropertyName( "my_role" )]            public string MyRole           { get; set; }
	[JsonPropertyName( "my_joined_at" )]       public string MyJoinedAt       { get; set; }
	[JsonPropertyName( "my_deposited_total" )] public long   MyDepositedTotal { get; set; }
}

/// <summary>Réponse de GET /gangs/by-member/{steamid} (gang peut être null).</summary>
public sealed class GangByMemberResponse
{
	[JsonPropertyName( "gang" )] public GangDto Gang { get; set; }
}

public sealed class GangMemberDto
{
	[JsonPropertyName( "steamid" )]          public string SteamId        { get; set; }
	[JsonPropertyName( "role" )]             public string Role           { get; set; }
	[JsonPropertyName( "joined_at" )]        public string JoinedAt       { get; set; }
	[JsonPropertyName( "deposited_total" )]  public long   DepositedTotal { get; set; }
	[JsonPropertyName( "steam_name" )]       public string SteamName      { get; set; }
	[JsonPropertyName( "rp_name" )]          public string RpName         { get; set; }
	[JsonPropertyName( "avatar_url" )]       public string AvatarUrl      { get; set; }
	[JsonPropertyName( "last_seen" )]        public string LastSeen       { get; set; }
}

public sealed class GangInviteDto
{
	[JsonPropertyName( "id" )]                public int    Id              { get; set; }
	[JsonPropertyName( "gang_id" )]           public int    GangId          { get; set; }
	[JsonPropertyName( "target_steamid" )]    public string TargetSteamId   { get; set; }
	[JsonPropertyName( "inviter_steamid" )]   public string InviterSteamId  { get; set; }
	[JsonPropertyName( "created_at" )]        public string CreatedAt       { get; set; }
	[JsonPropertyName( "expires_at" )]        public string ExpiresAt       { get; set; }
	[JsonPropertyName( "status" )]            public string Status          { get; set; }
	[JsonPropertyName( "target_steam_name" )] public string TargetSteamName { get; set; }
	[JsonPropertyName( "target_avatar_url" )] public string TargetAvatarUrl { get; set; }

	// Pour /gangs/invites/by-target/{steamid} (jointure avec gangs)
	[JsonPropertyName( "gang_name" )]  public string GangName  { get; set; }
	[JsonPropertyName( "gang_tag" )]   public string GangTag   { get; set; }
	[JsonPropertyName( "gang_color" )] public string GangColor { get; set; }
}

public sealed class GangTreasuryLogDto
{
	[JsonPropertyName( "id" )]            public long   Id           { get; set; }
	[JsonPropertyName( "gang_id" )]       public int    GangId       { get; set; }
	[JsonPropertyName( "steamid" )]       public string SteamId      { get; set; }
	[JsonPropertyName( "steam_name" )]    public string SteamName    { get; set; }
	[JsonPropertyName( "type" )]          public string Type         { get; set; }
	[JsonPropertyName( "amount" )]        public long   Amount       { get; set; }
	[JsonPropertyName( "balance_after" )] public long   BalanceAfter { get; set; }
	[JsonPropertyName( "reason" )]        public string Reason       { get; set; }
	[JsonPropertyName( "created_at" )]    public string CreatedAt    { get; set; }
}

public sealed class GangFounderBanDto
{
	[JsonPropertyName( "steamid" )]            public string SteamId         { get; set; }
	[JsonPropertyName( "steam_name" )]         public string SteamName       { get; set; }
	[JsonPropertyName( "reason" )]             public string Reason          { get; set; }
	[JsonPropertyName( "banned_at" )]          public string BannedAt        { get; set; }
	[JsonPropertyName( "banned_by_steamid" )]  public string BannedBySteamId { get; set; }
	[JsonPropertyName( "expires_at" )]         public string ExpiresAt       { get; set; }
}

public sealed class GangFounderBanCheckResponse
{
	[JsonPropertyName( "banned" )] public bool              Banned { get; set; }
	[JsonPropertyName( "detail" )] public GangFounderBanDto Detail { get; set; }
}

/// <summary>Réponse POST /gangs/{id}/treasury : nouveau solde.</summary>
public sealed class GangTreasuryResponse
{
	[JsonPropertyName( "status" )]        public string Status       { get; set; }
	[JsonPropertyName( "balance_after" )] public long   BalanceAfter { get; set; }
}

/// <summary>Rôles ENUM côté DB.</summary>
public static class GangRole
{
	public const string Leader     = "leader";
	public const string Lieutenant = "lieutenant";
	public const string Member     = "member";
}
