namespace Sandbox;

/// <summary>
/// Hiérarchie des rôles du staff, du joueur ordinaire jusqu'au fondateur.
/// </summary>
public enum StaffRole
{
	Player       = 0,  // Joueur standard
	Support      = 1,  // Accès panneau lecture seule
	Moderator    = 2,  // Kick, jail, warn
	SuperModerator = 3,// + Ban temporaire
	Admin        = 4,  // + Ban perm, setjob, setmoney, setrole (≤ Mod)
	Founder      = 5,  // Tout, ne peut pas être banni
}

public static class StaffRoleExtensions
{
	/// <summary>Convertit un StaffRole vers l'AdminRole existant du gamemode.</summary>
	public static AdminRole ToAdminRole( this StaffRole role ) => role switch
	{
		StaffRole.SuperModerator => AdminRole.Admin,
		StaffRole.Admin          => AdminRole.Admin,
		StaffRole.Founder        => AdminRole.SuperAdmin,
		_                        => AdminRole.None,
	};

	public static string GetLabel( this StaffRole role ) => role switch
	{
		StaffRole.Founder        => "Fondateur",
		StaffRole.Admin          => "Administrateur",
		StaffRole.SuperModerator => "Super-Modérateur",
		StaffRole.Moderator      => "Modérateur",
		StaffRole.Support        => "Support",
		_                        => "Joueur",
	};

	public static Color GetColor( this StaffRole role ) => role switch
	{
		StaffRole.Founder        => new Color( 1.00f, 0.84f, 0.00f ), // Or
		StaffRole.Admin          => new Color( 1.00f, 0.30f, 0.30f ), // Rouge
		StaffRole.SuperModerator => new Color( 1.00f, 0.55f, 0.10f ), // Orange
		StaffRole.Moderator      => new Color( 0.20f, 0.85f, 0.30f ), // Vert
		StaffRole.Support        => new Color( 0.40f, 0.70f, 1.00f ), // Bleu
		_                        => Color.White,
	};

	/// <summary>Peut exécuter des actions de modération (kick, jail).</summary>
	public static bool CanModerate( this StaffRole role )  => role >= StaffRole.Moderator;

	/// <summary>Peut bannir temporairement.</summary>
	public static bool CanTempBan( this StaffRole role )   => role >= StaffRole.SuperModerator;

	/// <summary>Peut bannir définitivement, modifier les jobs/money.</summary>
	public static bool CanAdmin( this StaffRole role )     => role >= StaffRole.Admin;

	/// <summary>Accès complet, peut gérer tous les rôles.</summary>
	public static bool IsFounder( this StaffRole role )    => role >= StaffRole.Founder;

	/// <summary>A accès au panneau admin (role ≥ Support).</summary>
	public static bool HasPanelAccess( this StaffRole role ) => role >= StaffRole.Support;
}
