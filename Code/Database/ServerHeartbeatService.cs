using System.Linq;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Push toutes les N secondes l'état du serveur vers darkapi :
///   - nombre + liste des joueurs connectés (steam_id, nom, job, money)
///   - nom de la map
///   - uptime
///
/// Le panel lit cette info pour son widget "joueurs en ligne".
///
/// À attacher sur le GameManager (Component) côté éditeur S&Box.
/// </summary>
public sealed class ServerHeartbeatService : Component
{
	[Property, Range( 5f, 300f )]
	public float HeartbeatIntervalSeconds { get; set; } = 30.0f;

	[Property]
	public bool VerboseLogs { get; set; } = false;

	private TimeSince _sinceLastHeartbeat;
	private bool _heartbeatInFlight;
	private TimeSince _sinceServerStart;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		_sinceLastHeartbeat = float.MaxValue; // push immédiat au démarrage
		_sinceServerStart   = 0;
		Log.Info( "[Heartbeat] Démarré." );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( _heartbeatInFlight ) return;
		if ( _sinceLastHeartbeat < HeartbeatIntervalSeconds ) return;

		_sinceLastHeartbeat = 0;
		_heartbeatInFlight = true;
		_ = SendHeartbeatAsync();
	}

	private async Task SendHeartbeatAsync()
	{
		try
		{
			var players = Connection.All
				.Where( c => !c.IsHost )
				.Select( c =>
				{
					var player = Player.FindForConnection( c );
					return new
					{
						steam_id  = (long) c.SteamId.Value,
						name      = c.DisplayName,
						rp_name   = player?.PlayerData?.DisplayName,
						job       = player?.JobTitle,
						money     = player?.Money ?? 0,
						staff_role = player is null ? 0 : (int) player.AdminRole,
					};
				} )
				.ToArray();

			var payload = new
			{
				player_count    = players.Length,
				players,
				map_name        = Game.ActiveScene?.Title ?? "unknown",
				uptime_seconds  = (int) _sinceServerStart,
			};

			var ok = await DarkHttpClient.PostAsync( "server/heartbeat", payload );
			if ( VerboseLogs )
				Log.Info( $"[Heartbeat] {players.Length} joueur(s) → panel : {(ok ? "ok" : "fail")}" );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( ex, "[Heartbeat] Échec du push." );
		}
		finally
		{
			_heartbeatInFlight = false;
		}
	}
}
