namespace Sandbox;

public sealed partial class DarkDatabase
{
	const int MaxLogsMemory = 500; // Limite du cache en mémoire

	// ── Enregistrer une action admin ────────────────────────────────────────
	public void LogAction(
		Connection admin,
		string     targetName,
		long?      targetSteamId,
		string     action,
		string     details = "" )
	{
		var log = new AdminLog
		{
			AdminSteamId  = admin is not null ? (long)admin.SteamId.Value : 0,
			AdminName     = admin?.DisplayName ?? "Serveur",
			TargetSteamId = targetSteamId,
			TargetName    = targetName,
			Action        = action,
			Details       = details,
		};

		// Cache mémoire (limité)
		_logs.Add( log );
		if ( _logs.Count > MaxLogsMemory )
			_logs.RemoveRange( 0, _logs.Count - MaxLogsMemory );

		// Persistance MySQL async (fire-and-forget, non bloquant)
		_ = SaveLogAsync( log );
	}

	// ── Getters ─────────────────────────────────────────────────────────────
	public IReadOnlyList<AdminLog> GetRecentLogs( int count = 50 ) =>
		_logs.TakeLast( count ).ToList().AsReadOnly();

	public IReadOnlyList<AdminLog> GetLogsForPlayer( long steamId ) =>
		_logs.Where( l => l.TargetSteamId == steamId || l.AdminSteamId == steamId )
		     .ToList().AsReadOnly();

	// ── Persistance HTTP ────────────────────────────────────────────────────

	/// <summary>Charge les logs récents depuis MySQL dans le cache mémoire.</summary>
	async Task LoadLogsAsync()
	{
		var logs = await DarkHttpClient.GetAsync<List<AdminLog>>( $"logs?limit={MaxLogsMemory}" );
		if ( logs is null )
		{
			Log.Warning( "[DarkDatabase] Impossible de charger les logs depuis MySQL." );
			return;
		}

		_logs.Clear();
		// L'API retourne les logs du plus récent au plus ancien → on les remet dans l'ordre chronologique
		_logs.AddRange( logs.AsEnumerable().Reverse() );
		Log.Info( $"[DarkDatabase] {_logs.Count} log(s) chargé(s)." );
	}

	/// <summary>Enregistre un log dans MySQL.</summary>
	async Task SaveLogAsync( AdminLog log )
	{
		var payload = new
		{
			admin_steam_id  = log.AdminSteamId,
			admin_name      = log.AdminName,
			target_steam_id = log.TargetSteamId,
			target_name     = log.TargetName,
			action          = log.Action,
			details         = log.Details,
		};

		var ok = await DarkHttpClient.PostAsync( "logs", payload );
		if ( !ok )
			Log.Warning( $"[DarkDatabase] Impossible de sauvegarder le log '{log.Action}'" );
	}
}
