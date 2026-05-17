using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Cache local des jobs whitelistés accordés au joueur (admin-approved via panel).
///
/// Récupéré au login depuis darkapi GET /players/{steamId}/whitelisted_jobs.
/// Lu de manière synchrone par JobManager.CanJoin pour autoriser ou refuser un
/// changement de job WL sans appel HTTP bloquant.
///
/// Refresh manuel via <see cref="LoadWhitelistsAsync"/> (à appeler aussi quand
/// un admin accorde/révoque une WL depuis le panel — TODO via pending_action).
/// </summary>
public sealed partial class Player
{
	// Synced FromHost car les clients peuvent vouloir afficher l'info (UI scoreboard)
	[Sync( SyncFlags.FromHost )]
	public NetList<string> WhitelistedJobCodes { get; private set; } = new();

	/// <summary>True si le joueur a la WL pour ce job (lecture synchrone).</summary>
	public bool HasWhitelistFor( JobDefinition definition )
	{
		if ( definition is null ) return false;
		if ( WhitelistedJobCodes is null || WhitelistedJobCodes.Count == 0 ) return false;

		// Match par ResourcePath ou ResourceName (le panel stocke `code` qui peut être l'un ou l'autre)
		return WhitelistedJobCodes.Any( c =>
			string.Equals( c, definition.ResourcePath, System.StringComparison.OrdinalIgnoreCase )
			|| string.Equals( c, definition.ResourceName, System.StringComparison.OrdinalIgnoreCase )
		);
	}

	/// <summary>
	/// Charge depuis darkapi la liste des jobs WL accordés au joueur.
	/// Appelé au login dans GameManager (PlayerConnected) — voir TODO d'intégration.
	/// </summary>
	public async Task LoadWhitelistsAsync()
	{
		if ( !Networking.IsHost ) return;
		if ( Network.Owner is null ) return;

		var sid = Network.Owner.SteamId.Value;
		var list = await DarkHttpClient.GetAsync<WhitelistedJobDto[]>( $"players/{sid}/whitelisted_jobs" );

		WhitelistedJobCodes.Clear();
		if ( list is not null )
		{
			foreach ( var item in list )
			{
				if ( !string.IsNullOrWhiteSpace( item?.Code ) )
				{
					WhitelistedJobCodes.Add( item.Code );
				}
			}
		}

		Log.Info( $"[Player.Whitelists] {WhitelistedJobCodes.Count} job(s) WL chargés pour {DisplayName}." );
	}

	sealed class WhitelistedJobDto
	{
		[JsonPropertyName( "id" )]           public int Id { get; set; }
		[JsonPropertyName( "code" )]         public string Code { get; set; }
		[JsonPropertyName( "display_name" )] public string DisplayName { get; set; }
		[JsonPropertyName( "joined_at" )]    public string JoinedAt { get; set; }
	}
}
