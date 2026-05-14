using System.Linq;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Synchronise tous les <see cref="JobDefinition"/> définis dans le gamemode
/// vers la BDD via darkapi (POST /api/jobs/sync).
///
/// Appelé au démarrage du serveur — la page /jobs du panel reflète alors
/// exactement ce qui est codé in-game.
/// </summary>
public static class JobSyncService
{
	/// <summary>Pousse tous les jobs/factions vers la BDD. Retourne le nombre de jobs synchronisés (-1 si échec).</summary>
	public static async Task<int> SyncAsync()
	{
		var jobs = JobDefinition.GetAll();
		if ( jobs is null || jobs.Count == 0 )
		{
			Log.Warning( "[JobSync] Aucun JobDefinition trouvé." );
			return 0;
		}

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
			var resp = await DarkHttpClient.PostJsonAsync<JobSyncResult>( "jobs/sync", payload );
			if ( resp is null || resp.Status != "ok" )
			{
				Log.Warning( "[JobSync] La sync a échoué (réponse vide ou erreur)." );
				return -1;
			}

			Log.Info( $"[JobSync] {resp.JobsSynced} job(s) et {resp.FactionsCount} faction(s) synchronisés." );
			return resp.JobsSynced;
		}
		catch ( System.Exception ex )
		{
			Log.Error( ex, "[JobSync] Exception pendant la sync." );
			return -1;
		}
	}

	private static string ColorToHex( Color c )
	{
		int r = (int) System.Math.Clamp( c.r * 255f, 0, 255 );
		int g = (int) System.Math.Clamp( c.g * 255f, 0, 255 );
		int b = (int) System.Math.Clamp( c.b * 255f, 0, 255 );
		return $"#{r:X2}{g:X2}{b:X2}";
	}

	private sealed class JobSyncResult
	{
		[System.Text.Json.Serialization.JsonPropertyName( "status" )]
		public string Status { get; set; }

		[System.Text.Json.Serialization.JsonPropertyName( "jobs_synced" )]
		public int JobsSynced { get; set; }

		[System.Text.Json.Serialization.JsonPropertyName( "factions_count" )]
		public int FactionsCount { get; set; }
	}
}
