// ============================================================
//  DarkRP — Staff Role hierarchy
//
//  ⚠️  CE FICHIER DOIT RESTER SYNCHRO AVEC :
//     - Panel PHP :  panel/src/Security/Role.php
//                    panel/src/Security/Permission.php
//
//  Mapping correspond exactement à players.staff_role en BDD.
// ============================================================

using System;

namespace Sandbox;

/// <summary>Hiérarchie des rôles staff DarkRP (cf. players.staff_role).</summary>
public enum StaffRole : byte
{
    Joueur     = 0,
    Support    = 1,
    SubModo    = 2,
    Modo       = 3,
    Supermodo  = 4,
    Admin      = 5,
    Fondateur  = 6,
}

/// <summary>
/// Matrice centrale des permissions in-game.
/// Doit refléter App\Security\Permission côté panel.
/// </summary>
public static class StaffPermissions
{
    // ── Mouvement / vue ─────────────────────────────────────
    public static bool CanNoclip(StaffRole role)         => role >= StaffRole.Support;
    public static bool CanTeleport(StaffRole role)       => role >= StaffRole.Support;
    public static bool CanSpectate(StaffRole role)       => role >= StaffRole.Support;

    // ── Modération douce ────────────────────────────────────
    public static bool CanKick(StaffRole role)           => role >= StaffRole.SubModo;
    public static bool CanWarn(StaffRole role)           => role >= StaffRole.SubModo;
    public static bool CanMuteChat(StaffRole role)       => role >= StaffRole.SubModo;
    public static bool CanMuteVoice(StaffRole role)      => role >= StaffRole.SubModo;

    // ── Ban / Jail ──────────────────────────────────────────
    public static bool CanBanTemporary(StaffRole role)   => role >= StaffRole.Modo;
    public static bool CanBanPermanent(StaffRole role)   => role >= StaffRole.Supermodo;
    public static bool CanUnban(StaffRole role)          => role >= StaffRole.Supermodo;
    public static bool CanJail(StaffRole role)           => role >= StaffRole.Supermodo;

    // ── Économie / VIP ──────────────────────────────────────
    public static bool CanSetMoney(StaffRole role)       => role >= StaffRole.Admin;
    public static bool CanGiveVip(StaffRole role)        => role >= StaffRole.Admin;
    public static bool CanAnnounce(StaffRole role)       => role >= StaffRole.Admin;
    public static bool CanEditFactions(StaffRole role)   => role >= StaffRole.Admin;
    public static bool CanChangeRole(StaffRole role)     => role >= StaffRole.Admin;

    // ── Réservé Fondateur ───────────────────────────────────
    public static bool CanManageStaff(StaffRole role)    => role >= StaffRole.Fondateur;
    public static bool CanServerConfig(StaffRole role)   => role >= StaffRole.Fondateur;
    public static bool CanRunServerCommand(StaffRole role) => role >= StaffRole.Fondateur;

    /// <summary>
    /// Durée maximale d'un ban (en minutes) autorisée pour un rôle donné.
    /// `null` = permanent autorisé, `0` = aucun droit de ban.
    /// </summary>
    public static int? MaxBanDurationMinutes(StaffRole role)
    {
        if (role >= StaffRole.Supermodo) return null;
        if (role >= StaffRole.Modo)      return 30 * 24 * 60;
        return 0;
    }

    /// <summary>
    /// Peut-on modifier la cible (kick/ban/jail/warn) ?
    /// Règle : on ne peut jamais toucher à un staff de rang ≥ au sien (sauf soi-même).
    /// </summary>
    public static bool CanModifyTarget(StaffRole self, StaffRole target, bool isSelf = false)
    {
        if (isSelf) return true;
        return target < self;
    }

    /// <summary>
    /// Peut-on assigner ce rôle ?
    /// Règle : on ne peut assigner qu'un rôle STRICTEMENT inférieur au sien.
    /// </summary>
    public static bool CanAssignRole(StaffRole self, StaffRole newRole)
    {
        return CanChangeRole(self) && newRole < self;
    }

    /// <summary>Libellé court pour affichage in-game.</summary>
    public static string Label(StaffRole role) => role switch
    {
        StaffRole.Joueur    => "Joueur",
        StaffRole.Support   => "Support",
        StaffRole.SubModo   => "Sub-modo",
        StaffRole.Modo      => "Modérateur",
        StaffRole.Supermodo => "Supermodo",
        StaffRole.Admin     => "Admin",
        StaffRole.Fondateur => "Fondateur",
        _                   => "?",
    };
}
