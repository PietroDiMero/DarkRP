using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Garde une trace de l'identifiant de session BDD par SteamID
/// pour pouvoir appeler EndSession à la déconnexion.
///
/// Appelé depuis GameManager.OnActive et GameManager.OnDisconnected.
/// </summary>
public static class PlayerSessionsTracker
{
	// SteamId → session_id en BDD
	private static readonly ConcurrentDictionary<long, int> _sessions = new();

	/// <summary>Appelé quand un joueur arrive. Log la session BDD et stocke l'ID retourné.</summary>
	public static async Task StartAsync( Connection conn )
	{
		if ( conn is null ) return;
		var steamId = (long)conn.SteamId.Value;
		var ip      = conn.Address ?? "0.0.0.0";

		try
		{
			var result = await DarkHttpClient.StartSessionAsync( steamId, ip );
			if ( result is not null && result.SessionId > 0 )
			{
				_sessions[steamId] = result.SessionId;
				Log.Info( $"[PlayerSessions] Session BDD #{result.SessionId} ouverte pour {steamId} (IP {ip})" );
			}
			else
			{
				Log.Warning( $"[PlayerSessions] StartSession a échoué pour {steamId}" );
			}
		}
		catch ( System.Exception ex )
		{
			Log.Warning( ex, $"[PlayerSessions] Échec StartSession pour {steamId}" );
		}
	}

	/// <summary>Appelé quand un joueur part. Termine la session BDD si on en a une.</summary>
	public static async Task EndAsync( Connection conn )
	{
		if ( conn is null ) return;
		var steamId = (long)conn.SteamId.Value;

		if ( !_sessions.TryRemove( steamId, out var sessionId ) ) return;

		try
		{
			await DarkHttpClient.EndSessionAsync( sessionId );
			Log.Info( $"[PlayerSessions] Session BDD #{sessionId} fermée pour {steamId}" );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( ex, $"[PlayerSessions] Échec EndSession #{sessionId} pour {steamId}" );
		}
	}
}
