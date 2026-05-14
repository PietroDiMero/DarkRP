using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Service serveur qui poll la file `pending_actions` toutes les N secondes
/// et exécute les actions distribuées par le panel web (ban, kick, jail…).
///
/// À attacher en tant que Component sur le GameManager (ou un GameObject racine côté serveur).
/// </summary>
public sealed class PendingActionsPoller : Component
{
	/// <summary>
	/// S'assure que le composant existe dans la scène. À appeler depuis
	/// GameManager.OnHostInitialize pour éviter d'avoir à l'attacher manuellement
	/// dans l'éditeur S&Box.
	/// </summary>
	public static void Ensure( Scene scene )
	{
		if ( scene is null ) return;
		if ( scene.GetAllComponents<PendingActionsPoller>().Any() ) return;

		var go = new GameObject( true, "PendingActionsPoller" );
		go.AddComponent<PendingActionsPoller>();
	}

	[Property, Range( 1f, 30f )]
	public float PollIntervalSeconds { get; set; } = 2.0f;

	[Property]
	public bool VerboseLogs { get; set; } = false;

	private TimeSince _sinceLastPoll;
	private bool _pollInFlight;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		_sinceLastPoll = 0;
		Log.Info( "[PendingActionsPoller] Démarré." );
	}

	protected override void OnFixedUpdate()
	{
		// Server-only
		if ( !Networking.IsHost ) return;
		if ( _pollInFlight ) return;
		if ( _sinceLastPoll < PollIntervalSeconds ) return;

		_sinceLastPoll = 0;
		_pollInFlight = true;
		_ = PollOnceAsync();
	}

	private async Task PollOnceAsync()
	{
		try
		{
			var actions = await DarkHttpClient.GetUnprocessedActionsAsync();
			if ( actions is null || actions.Length == 0 )
			{
				if ( VerboseLogs ) Log.Info( "[PendingActionsPoller] Rien à traiter." );
				return;
			}

			if ( VerboseLogs ) Log.Info( $"[PendingActionsPoller] {actions.Length} action(s) à traiter." );

			foreach ( var action in actions )
			{
				var ok = await PendingActionDispatcher.DispatchAsync( action );
				var result = ok ? "ok" : "failed";
				await DarkHttpClient.MarkActionProcessedAsync( action.Id, result );

				Log.Info( $"[PendingActionsPoller] Action #{action.Id} '{action.Action}' sur {action.TargetSteamId} → {result}" );
			}
		}
		catch ( System.Exception ex )
		{
			Log.Warning( ex, "[PendingActionsPoller] Erreur pendant le poll." );
		}
		finally
		{
			_pollInFlight = false;
		}
	}
}
