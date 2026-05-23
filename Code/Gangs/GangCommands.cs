using System.Linq;
using System.Threading.Tasks;
using Sandbox;

// ═══════════════════════════════════════════════════════════════════════════════
//  Toutes les sous-commandes /gang sont dispatchées ici.
//  Enregistré dans ChatCommandSystem.StaticCommands sous le nom "gang".
//
//  Usage in-game :
//    /gang help                        — affiche la liste des sous-commandes
//    /gang create <nom> <tag>          — crée un gang (10 000$ + 5h de jeu min)
//    /gang info [tag]                  — info sur ton gang (ou un autre par tag)
//    /gang invite <player>             — invite un joueur (chef/lieutenant)
//    /gang accept                      — accepte la première invitation pending
//    /gang decline                     — refuse la première invitation pending
//    /gang leave                       — quitte ton gang
//    /gang kick <player>               — kicke un membre (chef/lieutenant)
//    /gang promote <player>            — passe member → lieutenant (chef seulement)
//    /gang demote <player>             — passe lieutenant → member (chef seulement)
//    /gang deposit <amount>            — verse à la caisse
//    /gang withdraw <amount>           — retire de la caisse (chef seulement)
//    /gang tax <pct>                   — change le % de taxe sur jobs criminels (chef)
//    /gang dissolve                    — dissout ton gang (chef seulement)
//    /gang list                        — top 10 des gangs
// ═══════════════════════════════════════════════════════════════════════════════

public static class GangCommands
{
	// ── Constantes de design ────────────────────────────────────────────────
	public const int  CreateCostMoney     = 10_000;
	public const long CreateMinPlaytimeS  = 5 * 3600; // 5 h
	public const int  TagMinLength        = 3;
	public const int  TagMaxLength        = 6;
	public const int  NameMaxLength       = 40;
	public const int  InviteTtlSeconds    = 300;       // 5 min

	/// <summary>Entry point — appelé par ChatCommandSystem pour /gang ...</summary>
	public static void Handle( ChatCommandContext ctx )
	{
		if ( ctx.Arguments.Count == 0 ) { ShowHelp( ctx ); return; }

		var sub = ctx.Arguments[0].ToLowerInvariant();
		// Tout le reste après le sous-commande
		var restText = ctx.ArgumentsText.Length > sub.Length
			? ctx.ArgumentsText[sub.Length..].Trim()
			: string.Empty;

		switch ( sub )
		{
			case "help":     ShowHelp( ctx );                            break;
			case "create":   _ = HandleCreate( ctx, restText );          break;
			case "info":     _ = HandleInfo( ctx, restText );            break;
			case "list":     _ = HandleList( ctx );                      break;
			case "invite":   _ = HandleInvite( ctx, restText );          break;
			case "accept":   _ = HandleAccept( ctx );                    break;
			case "decline":  _ = HandleDecline( ctx );                   break;
			case "leave":    _ = HandleLeave( ctx );                     break;
			case "kick":     _ = HandleKick( ctx, restText );            break;
			case "promote":  _ = HandleSetRole( ctx, restText, GangRole.Lieutenant ); break;
			case "demote":   _ = HandleSetRole( ctx, restText, GangRole.Member );     break;
			case "deposit":  _ = HandleDeposit( ctx, restText );         break;
			case "withdraw": _ = HandleWithdraw( ctx, restText );        break;
			case "tax":      _ = HandleSetTax( ctx, restText );          break;
			case "dissolve": _ = HandleDissolve( ctx );                  break;
			case "chat":
			case "c":        _ = HandleChat( ctx, restText );            break;
			case "online":   _ = HandleOnline( ctx );                    break;
			default:
				ctx.Reply( $"Sous-commande '{sub}' inconnue. Tape /gang help.", "!" );
				break;
		}
	}

	static void ShowHelp( ChatCommandContext ctx )
	{
		ctx.Reply( "─── Commandes gang ───", "💀" );
		ctx.Reply( "/gang create <nom> <tag>   ·  Crée un gang (10k$, 5h min)", "·" );
		ctx.Reply( "/gang info [tag]           ·  Détail d'un gang", "·" );
		ctx.Reply( "/gang list                 ·  Top 10 des gangs", "·" );
		ctx.Reply( "/gang invite <player>      ·  Inviter (chef/lieutenant)", "·" );
		ctx.Reply( "/gang accept | decline     ·  Accepter / refuser une invitation", "·" );
		ctx.Reply( "/gang leave                ·  Quitter ton gang", "·" );
		ctx.Reply( "/gang kick <player>        ·  Kicker un membre (chef/lt)", "·" );
		ctx.Reply( "/gang promote|demote <pl>  ·  Changer le rôle d'un membre (chef)", "·" );
		ctx.Reply( "/gang deposit|withdraw <$> ·  Caisse commune", "·" );
		ctx.Reply( "/gang tax <pct>            ·  Taux taxe sur jobs criminels (chef)", "·" );
		ctx.Reply( "/gang dissolve             ·  Dissoudre ton gang (chef)", "·" );
		ctx.Reply( "/gang chat <msg>  (/gang c)·  Chat interne du gang", "·" );
		ctx.Reply( "/gang online              ·  Voir les membres de ton gang connectés", "·" );
	}

	// ════════════════════════════════════════════════════════════════════════
	//  Helpers
	// ════════════════════════════════════════════════════════════════════════

	static long Sid( ChatCommandContext ctx ) => (long)ctx.Connection.SteamId.Value;

	/// <summary>Récupère le playtime persistant du joueur (en secondes).</summary>
	static long GetPlaytimeSeconds( long steamId )
	{
		var record = DarkDatabase.Instance?.GetPlayer( steamId );
		return record?.PlaytimeSeconds ?? 0;
	}

	/// <summary>Format helper : "12 345" → "12,345" avec espace fin.</summary>
	static string FmtMoney( long amount ) => $"${amount:n0}";

	/// <summary>Cherche un joueur online par nom (helper similaire à ChatCommandSystem).</summary>
	static Player FindOnlinePlayer( string query )
	{
		if ( string.IsNullOrWhiteSpace( query ) ) return null;
		var players = Game.ActiveScene?.GetAll<Player>()
			.Where( p => p.IsValid() && p.Network.Owner is not null )
			.ToArray() ?? [];

		if ( long.TryParse( query, out var sid ) )
		{
			var byId = players.FirstOrDefault( p => p.SteamId == sid );
			if ( byId.IsValid() ) return byId;
		}

		return players.FirstOrDefault( p => string.Equals( p.DisplayName, query, System.StringComparison.OrdinalIgnoreCase ) )
			?? players.FirstOrDefault( p => p.DisplayName.Contains( query, System.StringComparison.OrdinalIgnoreCase ) );
	}

	// ════════════════════════════════════════════════════════════════════════
	//  Handlers async
	// ════════════════════════════════════════════════════════════════════════

	static async Task HandleCreate( ChatCommandContext ctx, string rest )
	{
		// rest = "<nom> <tag>" — on prend le DERNIER mot comme tag, le reste comme nom
		var parts = rest.Split( ' ', System.StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length < 2 )
		{
			ctx.Reply( "Usage : /gang create <nom> <tag>", "!" );
			return;
		}
		var tag  = parts[^1].ToUpperInvariant();
		var name = string.Join( ' ', parts.Take( parts.Length - 1 ) ).Trim();

		if ( name.Length == 0 || name.Length > NameMaxLength )
		{
			ctx.Reply( $"Nom invalide (1-{NameMaxLength} caractères).", "!" );
			return;
		}
		if ( tag.Length < TagMinLength || tag.Length > TagMaxLength || !tag.All( char.IsLetterOrDigit ) )
		{
			ctx.Reply( $"Tag invalide ({TagMinLength}-{TagMaxLength} caractères alphanumériques).", "!" );
			return;
		}

		var sid = Sid( ctx );

		// 1. Temps de jeu min
		var playtime = GetPlaytimeSeconds( sid );
		if ( playtime < CreateMinPlaytimeS )
		{
			var remainingH = (CreateMinPlaytimeS - playtime) / 3600.0;
			ctx.Reply( $"Tu dois avoir au moins {CreateMinPlaytimeS / 3600}h de jeu (il te manque ~{remainingH:F1}h).", "!" );
			return;
		}

		// 2. Argent
		if ( !ctx.Player.IsValid() || ctx.Player.Money < CreateCostMoney )
		{
			ctx.Reply( $"Tu as besoin de {FmtMoney( CreateCostMoney )} pour fonder un gang.", "!" );
			return;
		}

		// 3. Pas déjà dans un gang
		var existing = await GangApi.GetByMemberAsync( sid );
		if ( existing is not null )
		{
			ctx.Reply( $"Tu es déjà dans le gang [{existing.Tag}] {existing.Name}.", "!" );
			return;
		}

		// 4. Pas founder-banni
		if ( await GangApi.IsFounderBannedAsync( sid ) )
		{
			ctx.Reply( "Tu es interdit de création de gang (sanction modération).", "!" );
			return;
		}

		// 5. Appel API
		var result = await GangApi.CreateAsync( name, tag, sid );
		if ( !result.Ok )
		{
			ctx.Reply( $"Création refusée : {result.Error}", "!" );
			return;
		}

		// 6. Débit du coût (après succès BDD)
		ctx.Player.TryTakeMoney( CreateCostMoney );

		var g = result.Data;
		ctx.Reply( $"✓ Gang [{g.Tag}] {g.Name} créé. Coût : {FmtMoney( CreateCostMoney )}.", "💀" );
		ctx.Broadcast( $"🆕 Un nouveau gang vient d'être fondé : [{g.Tag}] {g.Name}", "💀" );

		// Log staff
		_ = DarkHttpClient.LogAdminActionAsync( sid, ctx.Player.DisplayName, null, null,
			"gang.create", $"[{g.Tag}] {g.Name}" );
	}

	static async Task HandleInfo( ChatCommandContext ctx, string rest )
	{
		GangDto gang;
		if ( !string.IsNullOrWhiteSpace( rest ) )
		{
			gang = await GangApi.GetByTagAsync( rest.Trim().ToUpperInvariant() );
			if ( gang is null ) { ctx.Reply( $"Gang [{rest}] introuvable.", "!" ); return; }
		}
		else
		{
			gang = await GangApi.GetByMemberAsync( Sid( ctx ) );
			if ( gang is null ) { ctx.Reply( "Tu n'es dans aucun gang. (/gang info <tag> pour voir un autre)", "!" ); return; }
		}

		ctx.Reply( $"── [{gang.Tag}] {gang.Name} ──", "💀" );
		if ( !string.IsNullOrWhiteSpace( gang.Description ) )
			ctx.Reply( gang.Description, "·" );
		ctx.Reply( $"Membres : {gang.MemberCount}/{gang.MemberLimit}  ·  Caisse : {FmtMoney( gang.Balance )}  ·  Taxe : {gang.TaxPctCriminal}% sur jobs criminels", "·" );
	}

	static async Task HandleList( ChatCommandContext ctx )
	{
		var gangs = await GangApi.ListActiveAsync() ?? System.Array.Empty<GangDto>();
		if ( gangs.Length == 0 ) { ctx.Reply( "Aucun gang actif.", "·" ); return; }

		ctx.Reply( "── Top 10 des gangs ──", "💀" );
		int rank = 1;
		foreach ( var g in gangs.Take( 10 ) )
		{
			ctx.Reply( $"{rank}. [{g.Tag}] {g.Name}  ·  {g.MemberCount} membres  ·  {FmtMoney( g.Balance )}", "·" );
			rank++;
		}
	}

	static async Task HandleInvite( ChatCommandContext ctx, string rest )
	{
		if ( string.IsNullOrWhiteSpace( rest ) ) { ctx.Reply( "Usage : /gang invite <player>", "!" ); return; }

		var sid = Sid( ctx );
		var my  = await GangApi.GetByMemberAsync( sid );
		if ( my is null ) { ctx.Reply( "Tu n'es dans aucun gang.", "!" ); return; }
		if ( my.MyRole != GangRole.Leader && my.MyRole != GangRole.Lieutenant )
		{
			ctx.Reply( "Seuls le chef et les lieutenants peuvent inviter.", "!" );
			return;
		}
		if ( my.MemberCount >= my.MemberLimit )
		{
			ctx.Reply( $"Limite de membres atteinte ({my.MemberLimit}).", "!" );
			return;
		}

		var target = FindOnlinePlayer( rest.Trim() );
		if ( !target.IsValid() ) { ctx.Reply( $"Joueur '{rest}' introuvable (doit être en ligne).", "!" ); return; }
		if ( target.SteamId == sid ) { ctx.Reply( "Tu ne peux pas t'inviter toi-même.", "!" ); return; }

		// Vérifie que target n'est pas déjà dans un gang
		var targetGang = await GangApi.GetByMemberAsync( target.SteamId );
		if ( targetGang is not null ) { ctx.Reply( $"{target.DisplayName} est déjà dans [{targetGang.Tag}].", "!" ); return; }

		var ok = await GangApi.CreateInviteAsync( my.Id, target.SteamId, sid, InviteTtlSeconds );
		if ( !ok ) { ctx.Reply( "Échec de l'envoi de l'invitation.", "!" ); return; }

		ctx.Reply( $"✓ Invitation envoyée à {target.DisplayName}.", "📨" );
		// Notifie la cible
		var targetConn = target.Network.Owner;
		if ( targetConn is not null )
		{
			ctx.Chat?.AddSystemTextTo( targetConn,
				$"[{my.Tag}] {ctx.Player.DisplayName} t'invite dans son gang. Tape /gang accept ou /gang decline (5 min).",
				"💀" );
		}
	}

	static async Task HandleAccept( ChatCommandContext ctx )
	{
		var sid = Sid( ctx );
		var mine = await GangApi.GetByMemberAsync( sid );
		if ( mine is not null ) { ctx.Reply( $"Tu es déjà dans [{mine.Tag}].", "!" ); return; }

		var invites = await GangApi.ListInvitesByTargetAsync( sid ) ?? System.Array.Empty<GangInviteDto>();
		if ( invites.Length == 0 ) { ctx.Reply( "Aucune invitation en attente.", "!" ); return; }

		var inv = invites[0]; // la plus récente
		var add = await GangApi.AddMemberAsync( inv.GangId, sid, GangRole.Member );
		if ( !add.Ok ) { ctx.Reply( $"Impossible de rejoindre : {add.Error}", "!" ); return; }

		ctx.Reply( $"✓ Tu as rejoint [{inv.GangTag}] {inv.GangName}.", "💀" );
		ctx.Broadcast( $"💀 {ctx.Player.DisplayName} rejoint le gang [{inv.GangTag}].", "·" );
	}

	static async Task HandleDecline( ChatCommandContext ctx )
	{
		var sid = Sid( ctx );
		var invites = await GangApi.ListInvitesByTargetAsync( sid ) ?? System.Array.Empty<GangInviteDto>();
		if ( invites.Length == 0 ) { ctx.Reply( "Aucune invitation en attente.", "!" ); return; }

		var inv = invites[0];
		await GangApi.PatchInviteAsync( inv.GangId, inv.Id, "declined" );
		ctx.Reply( $"✓ Invitation de [{inv.GangTag}] refusée.", "·" );
	}

	static async Task HandleLeave( ChatCommandContext ctx )
	{
		var sid = Sid( ctx );
		var my  = await GangApi.GetByMemberAsync( sid );
		if ( my is null ) { ctx.Reply( "Tu n'es dans aucun gang.", "!" ); return; }

		await GangApi.RemoveMemberAsync( my.Id, sid );
		ctx.Reply( $"✓ Tu as quitté [{my.Tag}] {my.Name}.", "·" );
		ctx.Broadcast( $"💀 {ctx.Player.DisplayName} quitte [{my.Tag}].", "·" );
	}

	static async Task HandleKick( ChatCommandContext ctx, string rest )
	{
		if ( string.IsNullOrWhiteSpace( rest ) ) { ctx.Reply( "Usage : /gang kick <player>", "!" ); return; }

		var sid = Sid( ctx );
		var my  = await GangApi.GetByMemberAsync( sid );
		if ( my is null ) { ctx.Reply( "Tu n'es dans aucun gang.", "!" ); return; }
		if ( my.MyRole != GangRole.Leader && my.MyRole != GangRole.Lieutenant )
		{
			ctx.Reply( "Seuls le chef et les lieutenants peuvent kicker.", "!" );
			return;
		}

		var members = await GangApi.ListMembersAsync( my.Id ) ?? System.Array.Empty<GangMemberDto>();
		var query   = rest.Trim().ToLowerInvariant();
		var target  = members.FirstOrDefault( m =>
			   (m.SteamName ?? "").ToLowerInvariant().Contains( query )
			|| (m.RpName    ?? "").ToLowerInvariant().Contains( query )
			|| m.SteamId == query );

		if ( target is null ) { ctx.Reply( $"Aucun membre '{rest}' dans ton gang.", "!" ); return; }
		if ( target.SteamId == sid.ToString() ) { ctx.Reply( "Tu ne peux pas te kicker toi-même (/gang leave).", "!" ); return; }
		if ( target.Role == GangRole.Leader )   { ctx.Reply( "On ne kicke pas le chef.", "!" ); return; }

		if ( long.TryParse( target.SteamId, out var targetSid ) )
		{
			await GangApi.RemoveMemberAsync( my.Id, targetSid );
			ctx.Reply( $"✓ {target.SteamName ?? target.SteamId} a été kické.", "·" );

			// Notifie la cible si online
			var onlineTarget = FindOnlinePlayer( target.SteamId );
			if ( onlineTarget.IsValid() && onlineTarget.Network.Owner is not null )
			{
				ctx.Chat?.AddSystemTextTo( onlineTarget.Network.Owner,
					$"Tu as été kické du gang [{my.Tag}].", "!" );
			}
		}
	}

	static async Task HandleSetRole( ChatCommandContext ctx, string rest, string newRole )
	{
		if ( string.IsNullOrWhiteSpace( rest ) ) { ctx.Reply( $"Usage : /gang {(newRole == GangRole.Lieutenant ? "promote" : "demote")} <player>", "!" ); return; }

		var sid = Sid( ctx );
		var my  = await GangApi.GetByMemberAsync( sid );
		if ( my is null ) { ctx.Reply( "Tu n'es dans aucun gang.", "!" ); return; }
		if ( my.MyRole != GangRole.Leader )
		{
			ctx.Reply( "Seul le chef peut changer les rôles.", "!" );
			return;
		}

		var members = await GangApi.ListMembersAsync( my.Id ) ?? System.Array.Empty<GangMemberDto>();
		var query   = rest.Trim().ToLowerInvariant();
		var target  = members.FirstOrDefault( m =>
			   (m.SteamName ?? "").ToLowerInvariant().Contains( query )
			|| (m.RpName    ?? "").ToLowerInvariant().Contains( query ) );

		if ( target is null ) { ctx.Reply( $"Aucun membre '{rest}' dans ton gang.", "!" ); return; }
		if ( target.Role == newRole ) { ctx.Reply( $"{target.SteamName} est déjà {newRole}.", "!" ); return; }
		if ( target.SteamId == sid.ToString() ) { ctx.Reply( "Tu ne peux pas modifier ton propre rôle.", "!" ); return; }

		if ( long.TryParse( target.SteamId, out var targetSid ) )
		{
			await GangApi.SetMemberRoleAsync( my.Id, targetSid, newRole );
			ctx.Reply( $"✓ {target.SteamName} est désormais {newRole}.", "·" );
		}
	}

	static async Task HandleDeposit( ChatCommandContext ctx, string rest )
	{
		if ( !long.TryParse( rest.Trim(), out var amount ) || amount <= 0 )
		{ ctx.Reply( "Usage : /gang deposit <amount>", "!" ); return; }

		var sid = Sid( ctx );
		var my  = await GangApi.GetByMemberAsync( sid );
		if ( my is null ) { ctx.Reply( "Tu n'es dans aucun gang.", "!" ); return; }

		if ( ctx.Player.Money < amount ) { ctx.Reply( "Solde insuffisant.", "!" ); return; }

		// Débit d'abord (atomicité côté joueur), puis crédit caisse
		if ( !ctx.Player.TryTakeMoney( (int)amount ) ) { ctx.Reply( "Impossible de prélever l'argent.", "!" ); return; }

		var r = await GangApi.RecordTreasuryAsync( my.Id, "deposit", amount, sid, "Dépôt manuel" );
		if ( !r.Ok )
		{
			// Rollback : on rend l'argent
			ctx.Player.GiveMoney( (int)amount );
			ctx.Reply( $"Échec dépôt : {r.Error}", "!" );
			return;
		}
		ctx.Reply( $"✓ {FmtMoney( amount )} versé à la caisse. Nouveau solde : {FmtMoney( r.Data?.BalanceAfter ?? 0 )}.", "💰" );
	}

	static async Task HandleWithdraw( ChatCommandContext ctx, string rest )
	{
		if ( !long.TryParse( rest.Trim(), out var amount ) || amount <= 0 )
		{ ctx.Reply( "Usage : /gang withdraw <amount>", "!" ); return; }

		var sid = Sid( ctx );
		var my  = await GangApi.GetByMemberAsync( sid );
		if ( my is null ) { ctx.Reply( "Tu n'es dans aucun gang.", "!" ); return; }
		if ( my.MyRole != GangRole.Leader ) { ctx.Reply( "Seul le chef peut retirer de la caisse.", "!" ); return; }

		var r = await GangApi.RecordTreasuryAsync( my.Id, "withdraw", amount, sid, "Retrait chef" );
		if ( !r.Ok ) { ctx.Reply( $"Échec retrait : {r.Error}", "!" ); return; }

		ctx.Player.GiveMoney( (int)amount );
		ctx.Reply( $"✓ {FmtMoney( amount )} retiré de la caisse. Solde restant : {FmtMoney( r.Data?.BalanceAfter ?? 0 )}.", "💰" );
	}

	static async Task HandleSetTax( ChatCommandContext ctx, string rest )
	{
		if ( !int.TryParse( rest.Trim().TrimEnd( '%' ), out var pct ) )
		{ ctx.Reply( "Usage : /gang tax <0-100>", "!" ); return; }
		pct = System.Math.Max( 0, System.Math.Min( 100, pct ) );

		var sid = Sid( ctx );
		var my  = await GangApi.GetByMemberAsync( sid );
		if ( my is null ) { ctx.Reply( "Tu n'es dans aucun gang.", "!" ); return; }
		if ( my.MyRole != GangRole.Leader ) { ctx.Reply( "Seul le chef (parrain) peut changer la taxe.", "!" ); return; }

		var ok = await GangApi.UpdateTaxAsync( my.Id, pct );
		if ( !ok ) { ctx.Reply( "Échec mise à jour taxe.", "!" ); return; }

		ctx.Reply( $"✓ Taxe sur jobs criminels : {pct}%", "·" );
		// Annonce aux membres présents
		var members = Game.ActiveScene?.GetAll<Player>().Where( p => p.IsValid() && p.Network.Owner is not null ).ToArray() ?? [];
		foreach ( var p in members )
		{
			ctx.Chat?.AddSystemTextTo( p.Network.Owner,
				$"[{my.Tag}] Ton parrain a changé la taxe sur jobs criminels à {pct}%.", "💀" );
		}
	}

	static async Task HandleDissolve( ChatCommandContext ctx )
	{
		var sid = Sid( ctx );
		var my  = await GangApi.GetByMemberAsync( sid );
		if ( my is null ) { ctx.Reply( "Tu n'es dans aucun gang.", "!" ); return; }
		if ( my.MyRole != GangRole.Leader ) { ctx.Reply( "Seul le chef peut dissoudre.", "!" ); return; }

		var ok = await GangApi.DissolveAsync( my.Id, "Dissolution par le chef" );
		if ( !ok ) { ctx.Reply( "Échec dissolution.", "!" ); return; }

		ctx.Reply( $"✓ [{my.Tag}] {my.Name} dissous.", "·" );
		ctx.Broadcast( $"💀 Le gang [{my.Tag}] {my.Name} a été dissous par son chef.", "·" );

		_ = DarkHttpClient.LogAdminActionAsync( sid, ctx.Player.DisplayName, null, null,
			"gang.dissolve", $"[{my.Tag}] {my.Name}" );
	}

	// ── /gang chat <msg> ─────────────────────────────────────────────────────

	static async Task HandleChat( ChatCommandContext ctx, string rest )
	{
		if ( string.IsNullOrWhiteSpace( rest ) ) { ctx.Reply( "Usage : /gang chat <message>", "!" ); return; }

		var sid = Sid( ctx );
		var my  = await GangApi.GetByMemberAsync( sid );
		if ( my is null ) { ctx.Reply( "Tu n'es dans aucun gang.", "!" ); return; }

		// Récupère les membres pour filtrer les joueurs online
		var members = await GangApi.ListMembersAsync( my.Id ) ?? System.Array.Empty<GangMemberDto>();
		var memberSids = new System.Collections.Generic.HashSet<string>(
			members.Select( m => m.SteamId ), System.StringComparer.OrdinalIgnoreCase );

		var line = $"[{my.Tag}] {ctx.Player.DisplayName} : {rest}";
		int sent = 0;
		foreach ( var p in Game.ActiveScene.GetAll<Player>() )
		{
			if ( !p.IsValid() || p.Network.Owner is null ) continue;
			if ( !memberSids.Contains( p.SteamId.ToString() ) ) continue;
			ctx.Chat?.AddSystemTextTo( p.Network.Owner, line, "💀" );
			sent++;
		}

		// Si l'expéditeur lui-même n'est pas online (cas rare), on lui confirme quand même
		if ( sent == 0 )
			ctx.Reply( line, "💀" );
	}

	// ── /gang online ─────────────────────────────────────────────────────────

	static async Task HandleOnline( ChatCommandContext ctx )
	{
		var sid = Sid( ctx );
		var my  = await GangApi.GetByMemberAsync( sid );
		if ( my is null ) { ctx.Reply( "Tu n'es dans aucun gang.", "!" ); return; }

		var members = await GangApi.ListMembersAsync( my.Id ) ?? System.Array.Empty<GangMemberDto>();
		var memberSids = new System.Collections.Generic.HashSet<string>(
			members.Select( m => m.SteamId ), System.StringComparer.OrdinalIgnoreCase );

		var onlinePlayers = Game.ActiveScene.GetAll<Player>()
			.Where( p => p.IsValid() && p.Network.Owner is not null
			             && memberSids.Contains( p.SteamId.ToString() ) )
			.ToArray();

		if ( onlinePlayers.Length == 0 )
		{
			ctx.Reply( $"[{my.Tag}] Aucun membre en ligne.", "💀" );
			return;
		}

		ctx.Reply( $"── [{my.Tag}] Membres en ligne ({onlinePlayers.Length}/{my.MemberCount}) ──", "💀" );
		foreach ( var p in onlinePlayers )
		{
			var member = members.FirstOrDefault( m => m.SteamId == p.SteamId.ToString() );
			var role   = member?.Role ?? "member";
			var icon   = role == GangRole.Leader ? "★" : role == GangRole.Lieutenant ? "◆" : "·";
			ctx.Reply( $"{icon} {p.DisplayName}", "·" );
		}
	}
}
