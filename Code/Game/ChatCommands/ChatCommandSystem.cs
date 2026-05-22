using Sandbox.UI;

public enum ChatCommandAccess
{
	Everyone,
	Admin,
	SuperAdmin
}

public sealed class ChatCommandPreview
{
	public ChatCommandPreview( string usage, string description, string accessText = null )
	{
		Usage = usage;
		Description = description;
		AccessText = accessText;
	}

	public string Usage { get; }
	public string Description { get; }
	public string AccessText { get; }
}

public sealed class ChatCommandContext
{
	public ChatCommandContext( Connection connection, Player player, Chat chat, string commandName, string argumentsText )
	{
		Connection = connection;
		Player = player;
		Chat = chat;
		CommandName = commandName;
		ArgumentsText = argumentsText?.Trim() ?? string.Empty;
		Arguments = ChatCommandSystem.TokenizeArguments( ArgumentsText );
	}

	public Connection Connection { get; }
	public Player Player { get; }
	public Chat Chat { get; }
	public string CommandName { get; }
	public string ArgumentsText { get; }
	public IReadOnlyList<string> Arguments { get; }

	public void Reply( string message, string icon = "/" )
	{
		Chat?.AddSystemTextTo( Connection, message, icon );
	}

	public void Broadcast( string message, string icon = "/" )
	{
		Chat?.AddSystemText( message, icon );
	}
}

public sealed class ChatCommandDefinition
{
	public ChatCommandDefinition(
		string name,
		string usage,
		string description,
		Action<ChatCommandContext> handler,
		ChatCommandAccess access = ChatCommandAccess.Everyone,
		string[] aliases = null,
		string accessText = null,
		Func<Player, bool> canUse = null )
	{
		Name = ChatCommandSystem.NormalizeCommandName( name );
		Usage = usage;
		Description = description;
		Handler = handler;
		Access = access;
		Aliases = (aliases ?? [])
			.Select( ChatCommandSystem.NormalizeCommandName )
			.Where( x => !string.IsNullOrWhiteSpace( x ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToArray();
		AccessText = accessText;
		CanUse = canUse;
	}

	public string Name { get; }
	public string Usage { get; }
	public string Description { get; }
	public Action<ChatCommandContext> Handler { get; }
	public ChatCommandAccess Access { get; }
	public IReadOnlyList<string> Aliases { get; }
	public string AccessText { get; }
	public Func<Player, bool> CanUse { get; }

	public IEnumerable<string> Names
	{
		get
		{
			yield return Name;

			foreach ( var alias in Aliases )
			{
				yield return alias;
			}
		}
	}

	public bool Matches( string commandName )
	{
		return Names.Any( x => string.Equals( x, commandName, StringComparison.OrdinalIgnoreCase ) );
	}
}

public static class ChatCommandSystem
{
	const int MaxSuggestions = 6;

	static readonly ChatCommandDefinition[] StaticCommands =
	[
		new( "advert", "/advert <message>", "Broadcast an advert message.", AdvertCommand, aliases: ["ad"] ),
		new( "ooc", "/ooc <message>", "Talk out of character.", OocCommand, aliases: ["//"] ),
		new( "me", "/me <action>", "Describe a roleplay action.", MeCommand ),
		new( "pm", "/pm <player> <message>", "Send a private message.", PrivateMessageCommand, aliases: ["msg", "tell", "w"] ),
		new( "dropmoney", "/dropmoney <amount>", "Drop money in front of you.", DropMoneyCommand, aliases: ["dropcash"] ),
		new( "name", "/name <rp name>", "Change your roleplay name.", NameCommand, aliases: ["rpname", "nick"] ),
		new( "kick", "/kick <player> <reason>", "Kicker un joueur du serveur.", KickCommand,
			canUse: p => p?.StaffRole >= StaffRole.SubModerator, accessText: "sub-modo+" ),
		// /ban et /unban retirés : uniquement via le panel web (/bans).
		new( "setadmin", "/setadmin <player> <none|admin|superadmin>", "Change a player's staff role.", SetAdminCommand, ChatCommandAccess.SuperAdmin, accessText: "superadmin" ),
		new( "givemoney", "/givemoney <player> <amount>", "Give money to a player.", GiveMoneyCommand, ChatCommandAccess.Admin, accessText: "admin" ),
		new( "setmoney", "/setmoney <player> <amount>", "Set a player's money.", SetMoneyCommand, ChatCommandAccess.Admin, accessText: "admin" ),

		// ── Commandes panel ─────────────────────────────────────────────
		new( "report", "/report <message>", "Signaler un problème au staff (envoyé sur le panel web).", ReportCommand ),

		new( "warn", "/warn <player> <reason>", "Avertir un joueur (apparaît dans son casier).", WarnCommand,
			canUse: p => p?.StaffRole >= StaffRole.SubModerator, accessText: "sub-modo+" ),

		new( "jail", "/jail <player> <minutes> <reason>", "Mettre un joueur en jail.", JailCommand,
			canUse: p => p?.StaffRole >= StaffRole.SubModerator, accessText: "sub-modo+" ),

		new( "tpto", "/tpto <player>", "Te téléporter à un joueur.", TptoCommand,
			canUse: p => p?.StaffRole >= StaffRole.Support, accessText: "staff", aliases: ["goto"] ),

		new( "noclip", "/noclip", "Activer noclip + immortalité (vol + invincible).", NoclipOnCommand,
			canUse: p => p?.StaffRole >= StaffRole.SubModerator, accessText: "sub-modo+" ),

		new( "unclip", "/unclip", "Désactiver noclip + immortalité.", NoclipOffCommand,
			canUse: p => p?.StaffRole >= StaffRole.SubModerator, accessText: "sub-modo+" ),

		new( "bring", "/bring <player>", "Téléporter un joueur vers toi.", BringCommand,
			canUse: p => p?.StaffRole >= StaffRole.Support, accessText: "staff" ),

		new( "position", "/position", "Affiche tes coordonnées exactes (position + angles).", PositionCommand,
			canUse: p => p?.StaffRole >= StaffRole.Support, accessText: "staff", aliases: ["pos", "coords"] ),

		// ── Élections du maire ──────────────────────────────────────────
		new( "candidate", "/candidate <programme>", "Se présenter aux élections du maire (pendant la phase candidatures).", CandidateCommand ),
		new( "vote", "/vote <numéro>", "Voter pour un candidat aux élections (pendant la phase vote).", VoteCommand ),
		new( "election", "/election [start]", "Forcer le démarrage d'une élection du maire.", ElectionCommand,
			canUse: p => p?.StaffRole >= StaffRole.Admin, accessText: "admin" ),

		// ── Whitelist ───────────────────────────────────────────────────
		new( "wl-apply", "/wl-apply <job_code> <motivation>", "Postuler pour un job whitelisté (validé par le staff).", WhitelistApplyCommand, aliases: ["wl", "wlapply"] ),

		// ── Gangs / factions joueurs ────────────────────────────────────
		new( "gang", "/gang <create|info|invite|accept|leave|kick|deposit|tax|...>",
			"Gestion des gangs joueurs (tape /gang help pour la liste complète).",
			GangCommands.Handle )
	];

	public static IReadOnlyList<string> TokenizeArguments( string argumentsText )
	{
		if ( string.IsNullOrWhiteSpace( argumentsText ) )
			return [];

		return argumentsText.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
	}

	public static string NormalizeCommandName( string commandName )
	{
		var value = (commandName ?? string.Empty).Trim();
		if ( value == "//" )
			return value;

		return value.TrimStart( '/' ).Trim().ToLowerInvariant();
	}

	public static bool IsCommandInput( string input )
	{
		return !string.IsNullOrWhiteSpace( input ) && input.TrimStart().StartsWith( "/" );
	}

	public static bool TryExecute( Connection caller, string input )
	{
		if ( !Networking.IsHost )
			return false;

		var chat = Game.ActiveScene?.Get<Chat>();
		if ( !TryParseCommand( input, out var commandName, out var argumentsText ) )
			return false;

		if ( string.IsNullOrWhiteSpace( commandName ) )
		{
			chat?.AddSystemTextTo( caller, "Start typing a command to see suggestions.", "/" );
			return true;
		}

		var player = Player.FindForConnection( caller );
		if ( !player.IsValid() )
			return true;

		var context = new ChatCommandContext( caller, player, chat, commandName, argumentsText );
		var command = FindStaticCommand( commandName );
		if ( command is not null )
		{
			if ( !CanUseCommand( player, command ) )
			{
				context.Reply( "You do not have access to that command.", "!" );
				return true;
			}

			command.Handler( context );
			return true;
		}

		context.Reply( "Unknown command.", "!" );
		return true;
	}

	public static IReadOnlyList<ChatCommandPreview> GetPreviews( string input, Player player )
	{
		if ( !IsCommandInput( input ) )
			return [];

		if ( !HasCommandPreviewQuery( input ) )
			return [];

		if ( !TryParseCommand( input, out var commandName, out var argumentsText ) )
			return [];

		// Si la commande est complète et attend un joueur, on suggère les noms en ligne
		// (détection : il y a un espace dans l'input ET la commande existe ET prend un player)
		if ( input.Contains( ' ' ) && CommandTakesPlayer( commandName ) )
		{
			var command = FindStaticCommand( commandName );
			if ( command is not null && CanUseCommand( player, command ) )
			{
				return BuildPlayerArgPreviews( player, command, argumentsText, MaxSuggestions );
			}
		}

		return BuildVisiblePreviews( player, commandName, MaxSuggestions );
	}

	/// <summary>Liste blanche des commandes qui acceptent un nom de joueur en 1er argument.</summary>
	static bool CommandTakesPlayer( string commandName )
	{
		return commandName switch
		{
			"kick" or "warn" or "jail" or "tpto" or "goto" or "bring"
				or "pm" or "msg" or "tell" or "w"
				or "givemoney" or "setmoney" or "setadmin" => true,
			_ => false,
		};
	}

	/// <summary>Suggère les joueurs connectés correspondant au début du 1er argument.</summary>
	static IReadOnlyList<ChatCommandPreview> BuildPlayerArgPreviews(
		Player caller, ChatCommandDefinition command, string args, int limit )
	{
		var partial = (args ?? "").Split( ' ' ).FirstOrDefault() ?? "";

		var players = Game.ActiveScene?.GetAll<Player>()
			.Where( p => p.IsValid() && p.Network.Owner is not null && p != caller )
			.ToArray() ?? [];

		var matches = string.IsNullOrEmpty( partial )
			? players
			: players.Where( p => p.DisplayName.Contains( partial, StringComparison.OrdinalIgnoreCase ) ).ToArray();

		// Indice sur les args restants (raison, montant…)
		var remainingHint = "";
		var usage = command.Usage;
		var firstArgEnd = usage.IndexOf( '>' );
		if ( firstArgEnd > 0 && firstArgEnd + 1 < usage.Length )
		{
			remainingHint = usage[(firstArgEnd + 1)..].Trim();
		}

		var previews = matches
			.Take( limit )
			.Select( p =>
			{
				var hint = $"/{command.Name} {p.DisplayName}";
				if ( !string.IsNullOrWhiteSpace( remainingHint ) ) hint += " " + remainingHint;
				return new ChatCommandPreview( hint, command.Description, GetAccessText( command ) );
			} )
			.ToArray();

		// Si aucun joueur ne match, on retombe sur l'usage générique
		if ( previews.Length == 0 )
		{
			return [new ChatCommandPreview( command.Usage, command.Description, GetAccessText( command ) )];
		}

		return previews;
	}

	static bool HasCommandPreviewQuery( string input )
	{
		var text = input?.TrimStart();
		if ( string.IsNullOrWhiteSpace( text ) || !text.StartsWith( "/" ) )
			return false;

		return text.Skip( 1 ).Any( char.IsLetter );
	}

	static IReadOnlyList<ChatCommandPreview> BuildVisiblePreviews( Player player, string commandName, int limit )
	{
		var query = NormalizeCommandName( commandName );
		var previews = new List<ChatCommandPreview>();

		foreach ( var command in StaticCommands )
		{
			if ( !CanUseCommand( player, command ) )
				continue;

			if ( !MatchesQuery( command.Names, query ) )
				continue;

			previews.Add( new ChatCommandPreview( command.Usage, command.Description, GetAccessText( command ) ) );
		}

		return previews
			.OrderBy( x => x.Usage )
			.Take( limit )
			.ToArray();
	}

	static bool TryParseCommand( string input, out string commandName, out string argumentsText )
	{
		commandName = null;
		argumentsText = string.Empty;

		var text = input?.TrimStart();
		if ( string.IsNullOrWhiteSpace( text ) || !text.StartsWith( "/" ) )
			return false;

		if ( text.StartsWith( "//" ) )
		{
			commandName = "//";
			argumentsText = text[2..].Trim();
			return true;
		}

		text = text[1..].TrimStart();
		if ( string.IsNullOrWhiteSpace( text ) )
		{
			commandName = string.Empty;
			return true;
		}

		var separator = text.IndexOf( ' ' );
		if ( separator < 0 )
		{
			commandName = NormalizeCommandName( text );
			return true;
		}

		commandName = NormalizeCommandName( text[..separator] );
		argumentsText = text[(separator + 1)..].Trim();
		return true;
	}

	static ChatCommandDefinition FindStaticCommand( string commandName )
	{
		return StaticCommands.FirstOrDefault( x => x.Matches( commandName ) );
	}

	static bool CanUseCommand( Player player, ChatCommandDefinition command )
	{
		if ( command.Access == ChatCommandAccess.Admin && player?.HasAdminAccess != true )
			return false;

		if ( command.Access == ChatCommandAccess.SuperAdmin && player?.HasSuperAdminAccess != true )
			return false;

		return command.CanUse?.Invoke( player ) ?? true;
	}

	static string GetAccessText( ChatCommandDefinition command )
	{
		if ( !string.IsNullOrWhiteSpace( command.AccessText ) )
			return command.AccessText;

		return command.Access switch
		{
			ChatCommandAccess.Admin => "admin",
			ChatCommandAccess.SuperAdmin => "superadmin",
			_ => null
		};
	}

	static bool MatchesQuery( IEnumerable<string> names, string query )
	{
		if ( string.IsNullOrWhiteSpace( query ) )
			return true;

		return names.Any( name =>
		{
			name = NormalizeCommandName( name );
			return name.StartsWith( query, StringComparison.OrdinalIgnoreCase )
				|| name.Contains( query, StringComparison.OrdinalIgnoreCase );
		} );
	}

	static bool TryFindPlayer( string query, out Player player, out string error )
	{
		player = null;
		error = null;

		if ( string.IsNullOrWhiteSpace( query ) )
		{
			error = "Enter a player name or SteamID.";
			return false;
		}

		var players = Game.ActiveScene.GetAll<Player>()
			.Where( x => x.IsValid() && x.Network.Owner is not null )
			.ToArray();

		if ( long.TryParse( query, out var steamId ) )
		{
			// Si le nombre est petit (< 100000), on essaie d abord le PublicId
			// assigne par PlayerIdSystem (ex: /tpto 521 ).
			// Les SteamIDs reels sont des nombres de 17 chiffres, donc pas de conflit.
			if ( steamId is > 0 and < 100000 )
			{
				var byPublicId = PlayerIdSystem.GetPlayerById( (int)steamId );
				if ( byPublicId.IsValid() )
				{
					player = byPublicId;
					return true;
				}
			}

			player = players.FirstOrDefault( x => x.SteamId == steamId );
			if ( player.IsValid() )
				return true;
		}

		player = players.FirstOrDefault( x => string.Equals( x.DisplayName, query, StringComparison.OrdinalIgnoreCase ) )
			?? players.FirstOrDefault( x => string.Equals( x.Network.Owner.DisplayName, query, StringComparison.OrdinalIgnoreCase ) );

		if ( player.IsValid() )
			return true;

		var matches = players
			.Where( x => x.DisplayName.Contains( query, StringComparison.OrdinalIgnoreCase )
				|| x.Network.Owner.DisplayName.Contains( query, StringComparison.OrdinalIgnoreCase ) )
			.Take( 5 )
			.ToArray();

		if ( matches.Length == 1 )
		{
			player = matches[0];
			return true;
		}

		error = matches.Length == 0
			? "Player not found."
			: $"Multiple players match: {string.Join( ", ", matches.Select( x => x.DisplayName ) )}.";
		return false;
	}

	static bool TryReadPlayerAndRest( ChatCommandContext context, out Player target, out string rest )
	{
		if ( TryFindPlayerAndRest( context.Arguments, out target, out rest, out var error ) )
			return true;

		context.Reply( error, "!" );
		return false;
	}

	static bool TryFindPlayerAndRest( IReadOnlyList<string> arguments, out Player target, out string rest, out string error )
	{
		target = null;
		rest = string.Empty;
		error = "Player not found.";

		if ( arguments.Count == 0 )
		{
			error = "Enter a target player.";
			return false;
		}

		for ( var length = arguments.Count; length >= 1; length-- )
		{
			var targetQuery = string.Join( " ", arguments.Take( length ) );
			if ( TryFindPlayer( targetQuery, out target, out var playerError ) )
			{
				rest = string.Join( " ", arguments.Skip( length ) );
				return true;
			}

			if ( length == 1 )
				error = playerError;
		}

		return false;
	}

	static bool SplitFirst( string text, out string first, out string rest )
	{
		first = null;
		rest = string.Empty;

		text = text?.Trim();
		if ( string.IsNullOrWhiteSpace( text ) )
			return false;

		var separator = text.IndexOf( ' ' );
		if ( separator < 0 )
		{
			first = text;
			return true;
		}

		first = text[..separator].Trim();
		rest = text[(separator + 1)..].Trim();
		return !string.IsNullOrWhiteSpace( first );
	}

	static bool TryParsePositiveInt( string value, out int amount )
	{
		return int.TryParse( value, out amount ) && amount > 0;
	}

	static void AdvertCommand( ChatCommandContext context )
	{
		if ( string.IsNullOrWhiteSpace( context.ArgumentsText ) )
		{
			context.Reply( "Usage: /advert <message>", "!" );
			return;
		}

		context.Broadcast( $"[Advert] {context.Player.DisplayName}: {context.ArgumentsText}", "AD" );
	}

	static void OocCommand( ChatCommandContext context )
	{
		if ( string.IsNullOrWhiteSpace( context.ArgumentsText ) )
		{
			context.Reply( "Usage: /ooc <message>", "!" );
			return;
		}

		context.Broadcast( $"[OOC] {context.Player.DisplayName}: {context.ArgumentsText}", "OOC" );
	}

	static void MeCommand( ChatCommandContext context )
	{
		if ( string.IsNullOrWhiteSpace( context.ArgumentsText ) )
		{
			context.Reply( "Usage: /me <action>", "!" );
			return;
		}

		context.Broadcast( $"* {context.Player.DisplayName} {context.ArgumentsText}", "*" );
	}

	static void PrivateMessageCommand( ChatCommandContext context )
	{
		if ( !TryReadPlayerAndRest( context, out var target, out var message ) )
			return;

		if ( string.IsNullOrWhiteSpace( message ) )
		{
			context.Reply( "Usage: /pm <player> <message>", "!" );
			return;
		}

		context.Chat?.AddSystemTextTo( context.Connection, $"PM to {target.DisplayName}: {message}", "PM" );
		if ( target.Network.Owner != context.Connection )
		{
			context.Chat?.AddSystemTextTo( target.Network.Owner, $"PM from {context.Player.DisplayName}: {message}", "PM" );
		}
	}

	static void DropMoneyCommand( ChatCommandContext context )
	{
		if ( context.Arguments.Count < 1 || !TryParsePositiveInt( context.Arguments[0], out var amount ) )
		{
			context.Reply( "Usage: /dropmoney <amount>", "!" );
			return;
		}

		context.Player.TryDropMoney( amount );
	}

	static void NameCommand( ChatCommandContext context )
	{
		if ( string.IsNullOrWhiteSpace( context.ArgumentsText ) )
		{
			context.Reply( "Usage: /name <rp name>", "!" );
			return;
		}

		context.Player.TryUpdateRoleplayName( context.ArgumentsText );
	}

	static void KickCommand( ChatCommandContext context )
	{
		if ( !TryReadPlayerAndRest( context, out var target, out var reason ) )
			return;

		var connection = target.Network.Owner;
		if ( connection is null || connection.IsHost || connection == context.Connection )
		{
			context.Reply( "You cannot kick that player.", "!" );
			return;
		}

		GameManager.Current?.Kick( connection, string.IsNullOrWhiteSpace( reason ) ? "Kicked" : reason );
		Notices.SendNotice( context.Connection, "person_remove", Color.Green, $"{target.DisplayName} was kicked.", 3 );
	}

	static void BanCommand( ChatCommandContext context )
	{
		if ( context.Arguments.Count == 0 )
		{
			context.Reply( "Usage: /ban <player|steamid> [reason]", "!" );
			return;
		}

		if ( TryFindPlayerAndRest( context.Arguments, out var target, out var reason, out _ ) )
		{
			var finalReason = string.IsNullOrWhiteSpace( reason ) ? "Banned" : reason;
			var connection = target.Network.Owner;
			if ( connection is null || connection.IsHost || connection == context.Connection )
			{
				context.Reply( "You cannot ban that player.", "!" );
				return;
			}

			BanSystem.Current?.Ban( connection, finalReason );
			Notices.SendNotice( context.Connection, "gavel", Color.Green, $"{target.DisplayName} was banned.", 3 );
			return;
		}

		SplitFirst( context.ArgumentsText, out var targetQuery, out reason );
		var finalOfflineReason = string.IsNullOrWhiteSpace( reason ) ? "Banned" : reason;
		if ( !ulong.TryParse( targetQuery, out var steamIdValue ) )
		{
			context.Reply( "Player not found. Use a SteamID to ban offline players.", "!" );
			return;
		}

		BanSystem.Current?.Ban( steamIdValue, finalOfflineReason );
		Notices.SendNotice( context.Connection, "gavel", Color.Green, $"{steamIdValue} was banned.", 3 );
	}

	static void UnbanCommand( ChatCommandContext context )
	{
		if ( context.Arguments.Count < 1 || !ulong.TryParse( context.Arguments[0], out var steamIdValue ) )
		{
			context.Reply( "Usage: /unban <steamid>", "!" );
			return;
		}

		BanSystem.Current?.Unban( steamIdValue );
		Notices.SendNotice( context.Connection, "gavel", Color.Green, $"{steamIdValue} was unbanned.", 3 );
	}

	static void SetAdminCommand( ChatCommandContext context )
	{
		if ( !TryReadPlayerAndRest( context, out var target, out var rest ) )
			return;

		if ( !SplitFirst( rest, out var roleText, out _ ) || !TryParseAdminRole( roleText, out var role ) )
		{
			context.Reply( "Usage: /setadmin <player> <none|admin|superadmin>", "!" );
			return;
		}

		var connection = target.Network.Owner;
		if ( connection?.IsHost == true )
		{
			context.Reply( "You cannot change the host role.", "!" );
			return;
		}

		AdminSystem.Current?.SetRole( connection.SteamId, role, connection.DisplayName );
		Notices.SendNotice( context.Connection, "security", Color.Green, $"{target.DisplayName} role set to {role}.", 3 );
	}

	static bool TryParseAdminRole( string roleText, out AdminRole role )
	{
		role = AdminRole.None;
		switch ( roleText?.Trim().ToLowerInvariant() )
		{
			case "none":
			case "user":
			case "remove":
				role = AdminRole.None;
				return true;
			case "admin":
				role = AdminRole.Admin;
				return true;
			case "superadmin":
			case "super":
			case "owner":
				role = AdminRole.SuperAdmin;
				return true;
			default:
				return false;
		}
	}

	static void GiveMoneyCommand( ChatCommandContext context )
	{
		if ( !TryReadPlayerAndRest( context, out var target, out var rest ) )
			return;

		if ( !SplitFirst( rest, out var amountText, out _ ) || !TryParsePositiveInt( amountText, out var amount ) )
		{
			context.Reply( "Usage: /givemoney <player> <amount>", "!" );
			return;
		}

		target.GiveMoney( amount );
		Notices.SendNotice( context.Connection, "$", Color.Green, $"Gave ${amount:n0} to {target.DisplayName}.", 3 );
	}

	static void SetMoneyCommand( ChatCommandContext context )
	{
		if ( !TryReadPlayerAndRest( context, out var target, out var rest ) )
			return;

		if ( !SplitFirst( rest, out var amountText, out _ ) || !int.TryParse( amountText, out var amount ) || amount < 0 )
		{
			context.Reply( "Usage: /setmoney <player> <amount>", "!" );
			return;
		}

		target.SetMoney( amount );
		Notices.SendNotice( context.Connection, "$", Color.Green, $"{target.DisplayName} now has ${amount:n0}.", 3 );
	}

	// ════════════════════════════════════════════════════════════════
	//  Commandes panel — /report /warn /jail /tpto /bring
	// ════════════════════════════════════════════════════════════════

	/// <summary>/report &lt;message&gt; — n'importe quel joueur envoie un signalement au staff (panel).</summary>
	static void ReportCommand( ChatCommandContext context )
	{
		if ( string.IsNullOrWhiteSpace( context.ArgumentsText ) )
		{
			context.Reply( "Usage : /report <message>", "!" );
			return;
		}

		var message = context.ArgumentsText.Trim();
		if ( message.Length < 3 )
		{
			context.Reply( "Message trop court (3 caractères minimum).", "!" );
			return;
		}
		if ( message.Length > 500 ) message = message[..500];

		var reporterSid = (long) context.Connection.SteamId.Value;

		// Fire-and-forget POST vers darkapi
		_ = DarkHttpClient.PostAsync( "reports", new
		{
			reporter_steam_id = reporterSid,
			target_steam_id   = reporterSid, // self-report : le message décrit le contexte, staff lira
			reason            = message,
		} );

		// Log dans admin_logs aussi
		_ = DarkHttpClient.LogAdminActionAsync(
			reporterSid, context.Player?.DisplayName ?? context.Connection.DisplayName,
			null, null, "report", message );

		context.Reply( "✓ Report envoyé au staff. Merci !", "📨" );
	}

	/// <summary>/warn &lt;player&gt; &lt;reason&gt; — incrément compteur warn + log staff.</summary>
	static void WarnCommand( ChatCommandContext context )
	{
		if ( !TryReadPlayerAndRest( context, out var target, out var reason ) ) return;
		if ( string.IsNullOrWhiteSpace( reason ) || reason.Length < 3 )
		{
			context.Reply( "Usage : /warn <player> <reason>", "!" );
			return;
		}
		if ( reason.Length > 500 ) reason = reason[..500];

		var adminSid = (long) context.Connection.SteamId.Value;
		var adminName = context.Player?.DisplayName ?? context.Connection.DisplayName;

		// POST /warnings côté darkapi (insère + incrémente players.warnings)
		_ = DarkHttpClient.PostAsync( "warnings", new
		{
			steam_id       = (long) target.SteamId,
			reason,
			admin_steam_id = adminSid,
			admin_name     = adminName,
		} );

		_ = DarkHttpClient.LogAdminActionAsync(
			adminSid, adminName, (long) target.SteamId, target.DisplayName, "warn", reason );

		Notices.SendNotice( target.Network.Owner, "warning", Color.Yellow,
			$"⚠ Avertissement : {reason}", 6 );
		Notices.SendNotice( context.Connection, "warning", Color.Green,
			$"{target.DisplayName} averti.", 3 );
	}

	/// <summary>/jail &lt;player&gt; &lt;minutes&gt; &lt;reason&gt; — arrest temporaire.</summary>
	static void JailCommand( ChatCommandContext context )
	{
		if ( !TryReadPlayerAndRest( context, out var target, out var rest ) ) return;
		if ( !SplitFirst( rest, out var minutesText, out var reason )
			|| !TryParsePositiveInt( minutesText, out var minutes )
			|| string.IsNullOrWhiteSpace( reason )
			|| reason.Length < 3 )
		{
			context.Reply( "Usage : /jail <player> <minutes> <reason>", "!" );
			return;
		}
		if ( reason.Length > 500 ) reason = reason[..500];

		// BeginArrest avec officer = null (admin)
		target.BeginArrest( null );

		var adminSid = (long) context.Connection.SteamId.Value;
		_ = DarkHttpClient.LogAdminActionAsync(
			adminSid, context.Player?.DisplayName ?? context.Connection.DisplayName,
			(long) target.SteamId, target.DisplayName,
			"jail", $"{minutes}min · {reason}" );

		Notices.SendNotice( context.Connection, "gavel", Color.Green,
			$"{target.DisplayName} jailed pour {minutes}min.", 3 );
	}

	/// <summary>/noclip — active vol + immortalité pour le caller (sub-modo+).</summary>
	static void NoclipOnCommand( ChatCommandContext context )
	{
		var player = context.Player;
		if ( !player.IsValid() )
		{
			context.Reply( "Tu dois être en jeu.", "!" );
			return;
		}
		player.SetNoclip( true );
		if ( player.PlayerData.IsValid() ) player.PlayerData.IsGodMode = true;
		// Restore HP/armor max au cas ou
		player.Health = player.MaxHealth;
		player.Armour = player.MaxArmour;

		Notices.SendNotice( context.Connection, "flight_takeoff", Color.Cyan,
			"Noclip + immortalité activés", 3 );
	}

	/// <summary>/unclip — désactive vol + immortalité.</summary>
	static void NoclipOffCommand( ChatCommandContext context )
	{
		var player = context.Player;
		if ( !player.IsValid() )
		{
			context.Reply( "Tu dois être en jeu.", "!" );
			return;
		}
		player.SetNoclip( false );
		if ( player.PlayerData.IsValid() ) player.PlayerData.IsGodMode = false;

		Notices.SendNotice( context.Connection, "flight_land", Color.Yellow,
			"Noclip + immortalité désactivés", 3 );
	}

	/// <summary>/position — affiche les coordonnées exactes du caller.</summary>
	static void PositionCommand( ChatCommandContext context )
	{
		var player = context.Player;
		if ( !player.IsValid() )
		{
			context.Reply( "Tu dois être en jeu.", "!" );
			return;
		}

		var pos = player.WorldPosition;
		var ang = player.WorldRotation.Angles();

		context.Reply( $"Position : X={pos.x:F1}  Y={pos.y:F1}  Z={pos.z:F1}", "📍" );
		context.Reply( $"Angles   : Pitch={ang.pitch:F1}  Yaw={ang.yaw:F1}  Roll={ang.roll:F1}", "🧭" );
	}

	/// <summary>/tpto &lt;player&gt; — téléporte le caller vers la cible.</summary>
	static void TptoCommand( ChatCommandContext context )
	{
		if ( !TryReadPlayerAndRest( context, out var target, out _ ) ) return;
		if ( !context.Player.IsValid() )
		{
			context.Reply( "Tu dois être en jeu pour utiliser /tpto.", "!" );
			return;
		}

		var ok = context.Player.ServerTeleportToPlayer( target );
		if ( !ok )
		{
			context.Reply( "Téléportation impossible.", "!" );
			return;
		}

		var adminSid = (long) context.Connection.SteamId.Value;
		_ = DarkHttpClient.LogAdminActionAsync(
			adminSid, context.Player.DisplayName,
			(long) target.SteamId, target.DisplayName,
			"teleport", $"tpto {target.DisplayName}" );

		Notices.SendNotice( context.Connection, "person_pin", Color.Green,
			$"Téléporté vers {target.DisplayName}.", 2 );
	}

	/// <summary>/bring &lt;player&gt; — téléporte la cible vers le caller.</summary>
	static void BringCommand( ChatCommandContext context )
	{
		if ( !TryReadPlayerAndRest( context, out var target, out _ ) ) return;
		if ( !context.Player.IsValid() )
		{
			context.Reply( "Tu dois être en jeu pour utiliser /bring.", "!" );
			return;
		}

		var ok = target.ServerTeleportToPlayer( context.Player );
		if ( !ok )
		{
			context.Reply( "Téléportation impossible.", "!" );
			return;
		}

		var adminSid = (long) context.Connection.SteamId.Value;
		_ = DarkHttpClient.LogAdminActionAsync(
			adminSid, context.Player.DisplayName,
			(long) target.SteamId, target.DisplayName,
			"teleport", $"bring {target.DisplayName}" );

		Notices.SendNotice( context.Connection, "person_pin", Color.Green,
			$"{target.DisplayName} amené à toi.", 2 );
		Notices.SendNotice( target.Network.Owner, "person_pin", Color.Yellow,
			$"Tu as été téléporté par {context.Player.DisplayName}.", 3 );
	}

	// ════════════════════════════════════════════════════════════════════
	//  ÉLECTIONS DU MAIRE
	// ════════════════════════════════════════════════════════════════════

	/// <summary>/candidate &lt;programme&gt; — se présenter aux élections (phase candidatures).</summary>
	static void CandidateCommand( ChatCommandContext context )
	{
		if ( !context.Player.IsValid() )
		{
			context.Reply( "Tu dois être en jeu pour te candidater.", "!" );
			return;
		}

		var mgr = MayorElectionManager.Current;
		if ( mgr is null )
		{
			context.Reply( "Système d'élections indisponible.", "!" );
			return;
		}

		var motivation = context.ArgumentsText?.Trim();
		if ( string.IsNullOrWhiteSpace( motivation ) || motivation.Length < 3 )
		{
			context.Reply( "Usage : /candidate <programme> (minimum 3 caractères)", "!" );
			return;
		}
		if ( motivation.Length > 300 ) motivation = motivation[..300];

		_ = HandleCandidateAsync( context, mgr, motivation );
	}

	static async Task HandleCandidateAsync( ChatCommandContext context, MayorElectionManager mgr, string motivation )
	{
		var (ok, message) = await mgr.RegisterCandidateAsync( context.Player, motivation );
		context.Reply( message, ok ? "🗳️" : "!" );
	}

	/// <summary>/vote &lt;numéro&gt; — voter pour un candidat (phase vote).</summary>
	static void VoteCommand( ChatCommandContext context )
	{
		if ( !context.Player.IsValid() )
		{
			context.Reply( "Tu dois être en jeu pour voter.", "!" );
			return;
		}

		var mgr = MayorElectionManager.Current;
		if ( mgr is null )
		{
			context.Reply( "Système d'élections indisponible.", "!" );
			return;
		}

		if ( context.Arguments.Count < 1 || !int.TryParse( context.Arguments[0], out var num ) || num < 1 )
		{
			context.Reply( "Usage : /vote <numéro du candidat>", "!" );
			return;
		}

		_ = HandleVoteAsync( context, mgr, num );
	}

	static async Task HandleVoteAsync( ChatCommandContext context, MayorElectionManager mgr, int num )
	{
		var (ok, message) = await mgr.RegisterVoteAsync( context.Player, num );
		context.Reply( message, ok ? "✓" : "!" );
	}

	/// <summary>/election — force le démarrage d'une élection (admin).</summary>
	static void ElectionCommand( ChatCommandContext context )
	{
		var mgr = MayorElectionManager.Ensure( Game.ActiveScene );
		if ( mgr is null )
		{
			context.Reply( "Impossible de créer le manager d'élections.", "!" );
			return;
		}

		_ = HandleElectionStartAsync( context, mgr );
	}

	static async Task HandleElectionStartAsync( ChatCommandContext context, MayorElectionManager mgr )
	{
		var started = await mgr.StartElectionAsync();
		context.Reply(
			started ? "✓ Élection lancée." : "Échec : une élection est peut-être déjà en cours.",
			started ? "🗳️" : "!"
		);
	}

	// ════════════════════════════════════════════════════════════════════
	//  WHITELIST
	// ════════════════════════════════════════════════════════════════════

	/// <summary>/wl-apply &lt;job_code&gt; &lt;motivation&gt; — soumet une candidature WL au staff.</summary>
	static void WhitelistApplyCommand( ChatCommandContext context )
	{
		if ( !context.Player.IsValid() )
		{
			context.Reply( "Tu dois être en jeu pour postuler à une whitelist.", "!" );
			return;
		}

		if ( context.Arguments.Count < 1 )
		{
			context.Reply( "Usage : /wl-apply <job_code> <motivation>", "!" );
			return;
		}

		var jobCode = context.Arguments[0];

		// Vérifie que le job existe et qu'il est WL
		var allJobs = JobDefinition.GetAll();
		var def = allJobs.FirstOrDefault( j =>
			string.Equals( j.ResourcePath, jobCode, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( j.ResourceName, jobCode, StringComparison.OrdinalIgnoreCase )
		);
		if ( def is null )
		{
			context.Reply( $"Job '{jobCode}' introuvable. Tape /jobs pour voir la liste.", "!" );
			return;
		}
		if ( !def.RequiresVote )
		{
			context.Reply( $"Le job '{def.Title}' n'est pas whitelisté — tu peux le prendre directement.", "!" );
			return;
		}
		if ( context.Player.HasWhitelistFor( def ) )
		{
			context.Reply( $"Tu as déjà la whitelist pour {def.Title}.", "!" );
			return;
		}

		// Motivation = tout ce qui suit le job_code
		var motivation = string.Join( " ", context.Arguments.Skip( 1 ) ).Trim();
		if ( motivation.Length < 10 )
		{
			context.Reply( "Motivation trop courte (10 caractères minimum).", "!" );
			return;
		}
		if ( motivation.Length > 1000 ) motivation = motivation[..1000];

		_ = HandleWhitelistApplyAsync( context, def, motivation );
	}

	static async Task HandleWhitelistApplyAsync( ChatCommandContext context, JobDefinition def, string motivation )
	{
		var sid = (long) context.Connection.SteamId.Value;

		// Le code attendu côté panel correspond à `jobs.code` BDD. JobSync push le ResourcePath
		// comme code, donc on envoie ResourcePath (cohérent avec la BDD).
		var ok = await DarkHttpClient.PostAsync( "whitelist/applications", new
		{
			steam_id   = sid,
			job_code   = def.ResourcePath,
			motivation = motivation,
		} );

		if ( ok )
		{
			context.Reply(
				$"✓ Candidature envoyée pour {def.Title}. Le staff l'examinera.",
				"📋"
			);
		}
		else
		{
			context.Reply(
				"Erreur lors de l'envoi (réseau ou candidature déjà en cours ?).",
				"!"
			);
		}
	}
}
