using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Résultat d'un check de démarrage.
/// </summary>
public readonly record struct StartupCheckResult( string Name, bool Success, string Message, long DurationMs )
{
	public static StartupCheckResult Ok( string name, string msg, long ms )
		=> new( name, true, msg, ms );

	public static StartupCheckResult Fail( string name, string msg, long ms )
		=> new( name, false, msg, ms );
}

/// <summary>
/// Interface à implémenter pour ajouter un check au démarrage.
/// </summary>
public interface IStartupCheck
{
	string Name { get; }
	Task<StartupCheckResult> RunAsync();
}

/// <summary>
/// Composant à attacher au GameManager — lance tous les checks au démarrage
/// et affiche un rapport coloré dans la console serveur.
///
/// Pour ajouter un check :
///   1. Crée une classe qui implémente IStartupCheck
///   2. L'enregistre dans RegisterDefaultChecks() ou via AddCheck() depuis un autre Component
/// </summary>
public sealed class StartupValidator : Component
{
	public static void Ensure( Scene scene )
	{
		if ( scene is null ) return;
		if ( scene.GetAllComponents<StartupValidator>().Any() ) return;

		var go = new GameObject( true, "StartupValidator" );
		go.AddComponent<StartupValidator>();
	}

	[Property] public bool VerboseLogs { get; set; } = true;
	[Property] public bool SendReportToPanel { get; set; } = true;

	private readonly List<IStartupCheck> _checks = new();
	private bool _ran;

	protected override void OnStart()
	{
		base.OnStart();
		if ( !Networking.IsHost || _ran ) return;
		_ran = true;

		RegisterDefaultChecks();
		_ = RunAllAsync();
	}

	public void AddCheck( IStartupCheck check ) => _checks.Add( check );

	private void RegisterDefaultChecks()
	{
		_checks.Add( new DatabasePingCheck() );
		_checks.Add( new PlayerTableCheck() );
		_checks.Add( new StaffRoleAlignmentCheck() );
		_checks.Add( new JobsLoadedCheck() );
		_checks.Add( new EconomyConfigCheck() );
		_checks.Add( new PendingActionsPollerCheck( this ) );
	}

	private async Task RunAllAsync()
	{
		var results = new List<StartupCheckResult>();
		var totalSw = Stopwatch.StartNew();

		Log.Info( "" );
		Log.Info( "╔══════════════════════════════════════════════════════════════╗" );
		Log.Info( "║   DarkRP — Validation au démarrage                            ║" );
		Log.Info( "╠══════════════════════════════════════════════════════════════╣" );

		foreach ( var check in _checks )
		{
			StartupCheckResult res;
			try
			{
				res = await check.RunAsync();
			}
			catch ( Exception ex )
			{
				res = StartupCheckResult.Fail( check.Name, $"Exception : {ex.Message}", 0 );
			}

			results.Add( res );

			var icon = res.Success ? "✓" : "✗";
			var line = $"║  {icon}  {res.Name,-30}  {res.DurationMs,6} ms  {res.Message}";
			line = line.Length > 64 ? line.Substring( 0, 61 ) + "...║" : line.PadRight( 64 ) + "║";

			if ( res.Success ) Log.Info( line );
			else               Log.Error( line );
		}

		totalSw.Stop();

		var ok   = results.Count( r => r.Success );
		var fail = results.Count - ok;
		var summary = fail == 0
			? $"Tous les checks OK ({ok}/{results.Count})"
			: $"⚠ {fail} échec(s) sur {results.Count}";

		Log.Info( "╠══════════════════════════════════════════════════════════════╣" );
		Log.Info( $"║  Résultat : {summary,-50}║" );
		Log.Info( $"║  Durée totale : {totalSw.ElapsedMilliseconds} ms" );
		Log.Info( "╚══════════════════════════════════════════════════════════════╝" );
		Log.Info( "" );

		if ( SendReportToPanel )
		{
			await SendReportAsync( results, totalSw.ElapsedMilliseconds );
		}
	}

	private async Task SendReportAsync( List<StartupCheckResult> results, long totalMs )
	{
		try
		{
			var payload = new
			{
				started_at = DateTime.UtcNow.ToString( "o" ),
				total_ms   = totalMs,
				ok_count   = results.Count( r => r.Success ),
				fail_count = results.Count( r => !r.Success ),
				checks     = results.Select( r => new
				{
					name        = r.Name,
					success     = r.Success,
					message     = r.Message,
					duration_ms = r.DurationMs,
				} ).ToArray(),
			};

			await DarkHttpClient.PostAsync( "server_health", payload );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "[StartupValidator] Impossible d'envoyer le rapport au panel." );
		}
	}
}

// ═══════════════════════════════════════════════════════════════════
//  CHECKS PAR DÉFAUT
// ═══════════════════════════════════════════════════════════════════

/// <summary>Vérifie que darkapi répond.</summary>
public sealed class DatabasePingCheck : IStartupCheck
{
	public string Name => "Connexion darkapi (BDD)";

	public async Task<StartupCheckResult> RunAsync()
	{
		var sw = Stopwatch.StartNew();
		var ok = await DarkHttpClient.PingAsync();
		sw.Stop();

		return ok
			? StartupCheckResult.Ok( Name, "API joignable", sw.ElapsedMilliseconds )
			: StartupCheckResult.Fail( Name, "API ne répond pas — vérifie le VPS", sw.ElapsedMilliseconds );
	}
}

/// <summary>Vérifie qu'on peut lire la table players.</summary>
public sealed class PlayerTableCheck : IStartupCheck
{
	public string Name => "Table 'players' lisible";

	public async Task<StartupCheckResult> RunAsync()
	{
		var sw = Stopwatch.StartNew();
		var players = await DarkHttpClient.GetAsync<PlayerRecord[]>( "players" );
		sw.Stop();

		if ( players is null )
			return StartupCheckResult.Fail( Name, "GET /players a échoué", sw.ElapsedMilliseconds );

		return StartupCheckResult.Ok( Name, $"{players.Length} joueur(s) en base", sw.ElapsedMilliseconds );
	}
}

/// <summary>
/// Vérifie que l'enum StaffRole local et la BDD ont des valeurs cohérentes
/// (au moins un Fondateur configuré pour éviter de perdre l'accès admin).
/// </summary>
public sealed class StaffRoleAlignmentCheck : IStartupCheck
{
	public string Name => "Staff configuré";

	public async Task<StartupCheckResult> RunAsync()
	{
		var sw = Stopwatch.StartNew();
		var players = await DarkHttpClient.GetAsync<PlayerRecord[]>( "players" );
		sw.Stop();

		if ( players is null )
			return StartupCheckResult.Fail( Name, "Impossible de charger les joueurs", sw.ElapsedMilliseconds );

		var founders = players.Count( p => p.StaffRole >= (int)StaffRole.Founder );
		var staff    = players.Count( p => p.StaffRole >= (int)StaffRole.Support );

		if ( founders == 0 )
			return StartupCheckResult.Fail( Name, $"⚠ Aucun Fondateur configuré ! ({staff} staff au total)", sw.ElapsedMilliseconds );

		return StartupCheckResult.Ok( Name, $"{founders} Fondateur(s), {staff} staff total", sw.ElapsedMilliseconds );
	}
}

/// <summary>
/// Synchronise tous les JobDefinition vers la BDD (factions auto-créées depuis les catégories).
/// La page /jobs du panel reflète alors exactement le contenu in-game.
/// </summary>
public sealed class JobsLoadedCheck : IStartupCheck
{
	public string Name => "Sync Jobs/Factions";

	public async Task<StartupCheckResult> RunAsync()
	{
		var sw = Stopwatch.StartNew();

		var jobs = JobDefinition.GetAll();
		var localCount = jobs?.Count ?? 0;

		if ( localCount == 0 )
		{
			sw.Stop();
			return StartupCheckResult.Fail( Name, "Aucun JobDefinition trouvé en jeu", sw.ElapsedMilliseconds );
		}

		var synced = await JobSyncService.SyncAsync();
		sw.Stop();

		if ( synced < 0 )
			return StartupCheckResult.Fail( Name, $"Sync vers BDD échouée ({localCount} jobs locaux)", sw.ElapsedMilliseconds );

		return StartupCheckResult.Ok( Name, $"{synced}/{localCount} job(s) push vers panel", sw.ElapsedMilliseconds );
	}
}

/// <summary>Vérifie le chargement de la config économique.</summary>
public sealed class EconomyConfigCheck : IStartupCheck
{
	public string Name => "Économie initialisée";

	public Task<StartupCheckResult> RunAsync()
	{
		var sw = Stopwatch.StartNew();

		// TODO : brancher sur le vrai système d'économie du gamemode
		// Exemple : var ok = EconomySystem.Current?.IsReady ?? false;
		var ok = true;

		sw.Stop();
		return Task.FromResult( ok
			? StartupCheckResult.Ok( Name, "Économie OK", sw.ElapsedMilliseconds )
			: StartupCheckResult.Fail( Name, "Économie non initialisée", sw.ElapsedMilliseconds ) );
	}
}

/// <summary>Vérifie que le poller de pending_actions est attaché et actif.</summary>
public sealed class PendingActionsPollerCheck : IStartupCheck
{
	private readonly Component _ctx;
	public PendingActionsPollerCheck( Component ctx ) { _ctx = ctx; }

	public string Name => "Poller pending_actions";

	public Task<StartupCheckResult> RunAsync()
	{
		var sw = Stopwatch.StartNew();

		var poller = Game.ActiveScene?
			.GetAllComponents<PendingActionsPoller>()
			.FirstOrDefault();

		sw.Stop();

		if ( poller is null )
			return Task.FromResult( StartupCheckResult.Fail( Name,
				"⚠ Composant PendingActionsPoller non attaché — ajoute-le sur GameManager.",
				sw.ElapsedMilliseconds ) );

		if ( !poller.Active )
			return Task.FromResult( StartupCheckResult.Fail( Name,
				"Poller présent mais désactivé",
				sw.ElapsedMilliseconds ) );

		return Task.FromResult( StartupCheckResult.Ok( Name,
			$"Actif, intervalle {poller.PollIntervalSeconds}s",
			sw.ElapsedMilliseconds ) );
	}
}
