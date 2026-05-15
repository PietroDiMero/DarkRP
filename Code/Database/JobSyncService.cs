using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Synchronisation bidirectionnelle Jobs ↔ Panel.
///
/// Au démarrage :
///   1. Push tous les <see cref="JobDefinition"/> du gamemode vers la BDD (création si manquant).
///   2. Fetch l'état actuel de la BDD (avec les overrides admin du panel).
///   3. Applique les overrides aux JobDefinition en mémoire.
///
/// Ensuite, peut être appelé périodiquement pour re-fetcher (sans push) — utile pour
/// récupérer les modifs panel sans redémarrer.
/// </summary>
public static class JobSyncService
{
	/// <summary>Synchronisation complète : push + fetch + apply.</summary>
	public static async Task<int> SyncAsync()
	{
		var jobs = JobDefinition.GetAll();
		if ( jobs is null || jobs.Count == 0 )
		{
			Log.Warning( "[JobSync] Aucun JobDefinition trouvé." );
			return 0;
		}

		// 1. Push initial : crée les rows manquantes côté BDD
		await PushJobsAsync( jobs );

		// 2. Fetch BDD (avec overrides admin)
		var bddJobs = await DarkHttpClient.GetAsync<JobBddRow[]>( "jobs/list" );
		if ( bddJobs is null )
		{
			Log.Warning( "[JobSync] GET /jobs/list a échoué." );
			return -1;
		}

		// 3. Apply overrides aux JobDefinition (en mémoire)
		var applied = ApplyOverridesToLocalJobs( jobs, bddJobs );

		Log.Info( $"[JobSync] {bddJobs.Length} job(s) BDD · {applied} override(s) appliqué(s) localement." );
		return bddJobs.Length;
	}

	/// <summary>Fetch + apply uniquement (pour refresh périodique pendant la partie).</summary>
	public static async Task<int> RefreshOverridesAsync()
	{
		var bddJobs = await DarkHttpClient.GetAsync<JobBddRow[]>( "jobs/list" );
		if ( bddJobs is null ) return -1;

		return ApplyOverridesToLocalJobs( JobDefinition.GetAll(), bddJobs );
	}

	/// <summary>Push C# → BDD (création des rows manquantes seulement).</summary>
	private static async Task PushJobsAsync( IReadOnlyList<JobDefinition> jobs )
	{
		var payload = new
		{
			jobs = jobs.Select( j => new
			{
				code            = j.ResourcePath,
				display_name    = j.Title ?? j.ResourcePath,
				category        = string.IsNullOrWhiteSpace( j.Category ) ? "Default" : j.Category,
				base_salary     = j.Salary,
				max_slots       = j.MaxPlayers,
				is_whitelisted  = j.RequiresVote,
				description     = j.Description,
				color_hex       = ColorToHex( j.GetDisplayColor() ),
			} ).ToArray()
		};

		try
		{
			await DarkHttpClient.PostAsync( "jobs/sync", payload );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( ex, "[JobSync] Push échoué." );
		}
	}

	/// <summary>
	/// Pour chaque job BDD, retrouve le JobDefinition local correspondant par code (ResourcePath)
	/// et écrase ses propriétés avec les valeurs BDD (overrides admin via le panel).
	/// </summary>
	private static int ApplyOverridesToLocalJobs( IReadOnlyList<JobDefinition> localJobs, JobBddRow[] bddJobs )
	{
		var count = 0;
		foreach ( var bdd in bddJobs )
		{
			if ( string.IsNullOrWhiteSpace( bdd.Code ) ) continue;

			var def = localJobs.FirstOrDefault( j =>
				string.Equals( j.ResourcePath, bdd.Code, System.StringComparison.OrdinalIgnoreCase ) );

			if ( def is null )
			{
				if ( bdd.IsActive ) Log.Info( $"[JobSync] BDD a un job '{bdd.Code}' sans définition locale." );
				continue;
			}

			// Apply overrides — seulement si la valeur BDD est définie (non-null/non-vide)
			if ( !string.IsNullOrWhiteSpace( bdd.DisplayName ) ) def.Title       = bdd.DisplayName;
			if ( !string.IsNullOrWhiteSpace( bdd.Description ) ) def.Description = bdd.Description;
			def.Salary     = bdd.BaseSalary;
			def.MaxPlayers = bdd.MaxSlots ?? 0;
			def.RequiresVote = bdd.IsWhitelisted;

			// Inventaire de départ — écrase la liste si la BDD en définit une
			if ( bdd.StartingItems is not null && bdd.StartingItems.Length > 0 )
			{
				def.StartingItems = bdd.StartingItems;
			}

			// Commande chat
			if ( !string.IsNullOrWhiteSpace( bdd.Command ) ) def.Command = bdd.Command;

			// Ordre d'affichage
			def.Order = bdd.DisplayOrder;

			// Couleur d'accent (override sur faction par défaut)
			if ( !string.IsNullOrWhiteSpace( bdd.ColorHex ) && HexToColor( bdd.ColorHex, out var col ) )
			{
				def.AccentColor = col;
			}

			count++;
		}

		return count;
	}

	// ─────────────────────────── Helpers ────────────────────────────
	private static string ColorToHex( Color c )
	{
		int r = (int) System.Math.Clamp( c.r * 255f, 0, 255 );
		int g = (int) System.Math.Clamp( c.g * 255f, 0, 255 );
		int b = (int) System.Math.Clamp( c.b * 255f, 0, 255 );
		return $"#{r:X2}{g:X2}{b:X2}";
	}

	private static bool HexToColor( string hex, out Color color )
	{
		color = Color.White;
		if ( string.IsNullOrWhiteSpace( hex ) ) return false;
		hex = hex.TrimStart( '#' );
		if ( hex.Length != 6 ) return false;
		if ( !int.TryParse( hex.Substring( 0, 2 ), System.Globalization.NumberStyles.HexNumber, null, out var r ) ) return false;
		if ( !int.TryParse( hex.Substring( 2, 2 ), System.Globalization.NumberStyles.HexNumber, null, out var g ) ) return false;
		if ( !int.TryParse( hex.Substring( 4, 2 ), System.Globalization.NumberStyles.HexNumber, null, out var b ) ) return false;
		color = new Color( r / 255f, g / 255f, b / 255f );
		return true;
	}

	// ─────────────────────────── DTO BDD ─────────────────────────────
	private sealed class JobBddRow
	{
		[JsonPropertyName( "code" )]            public string Code { get; set; }
		[JsonPropertyName( "display_name" )]    public string DisplayName { get; set; }
		[JsonPropertyName( "base_salary" )]     public int BaseSalary { get; set; }
		[JsonPropertyName( "is_whitelisted" )]  public bool IsWhitelisted { get; set; }
		[JsonPropertyName( "max_slots" )]       public int? MaxSlots { get; set; }
		[JsonPropertyName( "description" )]     public string Description { get; set; }
		[JsonPropertyName( "is_active" )]       public bool IsActive { get; set; }
		[JsonPropertyName( "starting_items" )]  public string[] StartingItems { get; set; }
		[JsonPropertyName( "color_hex" )]       public string ColorHex { get; set; }
		[JsonPropertyName( "command" )]         public string Command { get; set; }
		[JsonPropertyName( "display_order" )]   public int DisplayOrder { get; set; }
	}
}
