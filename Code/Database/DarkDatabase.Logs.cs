using System.Text.Json;

namespace Sandbox;

public sealed partial class DarkDatabase
{
	const int MaxLogs = 500;

	// ── Enregistrer une action admin ────────────────────────────────────
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

		_logs.Add( log );

		// Limiter la taille des logs en mémoire
		if ( _logs.Count > MaxLogs )
			_logs.RemoveRange( 0, _logs.Count - MaxLogs );

		SaveLogs();
	}

	// ── Getters ─────────────────────────────────────────────────────────
	public IReadOnlyList<AdminLog> GetRecentLogs( int count = 50 ) =>
		_logs.TakeLast( count ).ToList().AsReadOnly();

	public IReadOnlyList<AdminLog> GetLogsForPlayer( long steamId ) =>
		_logs.Where( l => l.TargetSteamId == steamId || l.AdminSteamId == steamId )
		     .ToList().AsReadOnly();

	// ── Persistance ─────────────────────────────────────────────────────
	void LoadLogs()
	{
		if ( !FileSystem.Data.FileExists( LogsFile ) ) return;
		try
		{
			var json    = FileSystem.Data.ReadAllText( LogsFile );
			var records = JsonSerializer.Deserialize<List<AdminLog>>( json ) ?? new();
			_logs.AddRange( records.TakeLast( MaxLogs ) );
			Log.Info( $"[DarkDatabase] {_logs.Count} log(s) chargé(s)." );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "[DarkDatabase] Impossible de charger les logs." );
		}
	}

	void SaveLogs()
	{
		try
		{
			var json = JsonSerializer.Serialize( _logs, new JsonSerializerOptions { WriteIndented = true } );
			FileSystem.Data.WriteAllText( LogsFile, json );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "[DarkDatabase] Impossible de sauvegarder les logs." );
		}
	}
}
