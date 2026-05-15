namespace Sandbox;

/// <summary>
/// Hiérarchie des rôles staff DarkRP — DOIT rester synchronisé avec :
///   - panel/src/Security/Role.php
///   - players.staff_role en BDD MySQL
/// </summary>
public enum StaffRole
{
	Player         = 0,  // Joueur standard
	Support        = 1,  // Lecture panel, noclip, téléportation
	SubModerator   = 2,  // + Kick, warn, mute
	Moderator      = 3,  // + Ban temporaire (≤ 30 jours)
	SuperModerator = 4,  // + Ban permanent, unban, jail
	Admin          = 5,  // + Set money, annonces, factions
	Founder        = 6,  // + Gestion staff, config serveur
}

public static class StaffRoleExtensions
{
	/// <summary>Convertit un StaffRole vers l'AdminRole legacy du gamemode.</summary>
	public static AdminRole ToAdminRole( this StaffRole role ) => role switch
	{
		StaffRole.Founder        => AdminRole.SuperAdmin,
		StaffRole.Admin          => AdminRole.SuperAdmin,
		StaffRole.SuperModerator => AdminRole.Admin,
		StaffRole.Moderator      => AdminRole.Admin,
		StaffRole.SubModerator   => AdminRole.Admin,
		_                        => AdminRole.None,
	};

	public static string GetLabel( this StaffRole role ) => role switch
	{
		StaffRole.Founder        => "Fondateur",
		StaffRole.Admin          => "Admin",
		StaffRole.SuperModerator => "Supermodo",
		StaffRole.Moderator      => "Modérateur",
		StaffRole.SubModerator   => "Sub-modérateur",
		StaffRole.Support        => "Support",
		_                        => "Joueur",
	};

	public static Color GetColor( this StaffRole role ) => role switch
	{
		StaffRole.Founder        => new Color( 0.94f, 0.27f, 0.27f ), // Rouge fondateur
		StaffRole.Admin          => new Color( 0.98f, 0.75f, 0.14f ), // Jaune
		StaffRole.SuperModerator => new Color( 0.66f, 0.55f, 0.98f ), // Violet
		StaffRole.Moderator      => new Color( 0.37f, 0.70f, 1.00f ), // Bleu
		StaffRole.SubModerator   => new Color( 0.22f, 0.74f, 0.97f ), // Cyan
		StaffRole.Support        => new Color( 0.29f, 0.87f, 0.50f ), // Vert
		_                        => Color.White,
	};

	public static bool HasPanelAccess( this StaffRole role ) => role >= StaffRole.Support;

	// ── Aliases legacy utilisés par le code existant (AdminPanel.razor.cs, DarkDatabase.Roles.cs) ──
	public static bool CanModerate( this StaffRole role )  => role >= StaffRole.SubModerator;
	public static bool CanTempBan( this StaffRole role )   => role >= StaffRole.Moderator;
	public static bool CanAdmin( this StaffRole role )     => role >= StaffRole.Admin;
	public static bool IsFounder( this StaffRole role )    => role >= StaffRole.Founder;
}

/// <summary>
/// Matrice centrale des permissions in-game.
///
/// DOIT rester synchronisé avec panel/src/Security/Permission.php.
/// </summary>
public static class StaffPermissions
{
	// ── Actions joueur de base (tous staff) ─────────────────────
	public static bool CanNoclip( StaffRole role )      => role >= StaffRole.Support;
	public static bool CanTeleport( StaffRole role )    => role >= StaffRole.Support;
	public static bool CanSpectate( StaffRole role )    => role >= StaffRole.Support;

	// ── Modération soft (sub-modo+) ─────────────────────────────
	public static bool CanKick( StaffRole role )        => role >= StaffRole.SubModerator;
	public static bool CanWarn( StaffRole role )        => role >= StaffRole.SubModerator;
	public static bool CanMute( StaffRole role )        => role >= StaffRole.SubModerator;
	public static bool CanFreeze( StaffRole role )      => role >= StaffRole.SubModerator;

	// ── Bans (modo+ pour temporaire, supermodo+ pour long/perm) ─
	public static bool CanBanTemporary( StaffRole role ) => role >= StaffRole.Moderator;
	public static bool CanBanLong( StaffRole role )      => role >= StaffRole.SuperModerator;
	public static bool CanUnban( StaffRole role )        => role >= StaffRole.SuperModerator;
	public static bool CanJail( StaffRole role )         => role >= StaffRole.SuperModerator;
	public static bool CanSlay( StaffRole role )         => role >= StaffRole.SuperModerator;

	// ── Économie / config (admin+) ──────────────────────────────
	public static bool CanSetMoney( StaffRole role )    => role >= StaffRole.Admin;
	public static bool CanSetJob( StaffRole role )      => role >= StaffRole.Admin;
	public static bool CanAnnounce( StaffRole role )    => role >= StaffRole.Admin;
	public static bool CanSlayAll( StaffRole role )     => role >= StaffRole.Admin;

	// ── Fondateur uniquement ────────────────────────────────────
	public static bool CanManageStaff( StaffRole role ) => role >= StaffRole.Founder;
	public static bool CanServerConfig( StaffRole role ) => role >= StaffRole.Founder;

	/// <summary>
	/// Vérifie qu'un admin peut modifier la cible (la cible doit avoir un rang STRICTEMENT inférieur).
	/// Empêche aussi la modification de soi-même par défaut.
	/// </summary>
	public static bool CanModerate( StaffRole admin, StaffRole target, bool allowSelf = false )
	{
		if ( !allowSelf && admin == target ) return false;
		return admin > target;
	}

	/// <summary>Durée max d'un ban en minutes (null = permanent autorisé, 0 = pas de droit).</summary>
	public static int? MaxBanDurationMinutes( StaffRole role )
	{
		return role switch
		{
			>= StaffRole.SuperModerator => null,           // permanent OK
			StaffRole.Moderator         => 30 * 24 * 60,   // 30 jours
			_                           => 0,
		};
	}
}
