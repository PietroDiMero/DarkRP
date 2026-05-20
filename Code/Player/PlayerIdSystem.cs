using System.Collections.Concurrent;

namespace Sandbox;

/// <summary>
/// Assigne des IDs sequentiels (1, 2, 3...) aux joueurs connectes pour usage RP/commandes.
/// L ID est libere quand le joueur quitte (le prochain join reprendra l ID le plus bas dispo).
/// Reset complet au boot du serveur (compteur repart de 1).
///
/// Usage cote game :
///   var id = PlayerIdSystem.AssignFor( connection );
///   var conn = PlayerIdSystem.GetConnectionById( 521 );
///   PlayerIdSystem.Release( connection );
/// </summary>
public static class PlayerIdSystem
{
	private static readonly ConcurrentDictionary<int, long> _idToSteam = new();
	private static readonly ConcurrentDictionary<long, int> _steamToId = new();
	private static int _nextId = 1;
	private static readonly object _lock = new();

	/// <summary>Assigne le plus petit ID dispo au joueur, ou retourne l existant.</summary>
	public static int AssignFor( Connection conn )
	{
		if ( conn is null ) return 0;
		var steam = (long)conn.SteamId.Value;

		if ( _steamToId.TryGetValue( steam, out var existing ) )
			return existing;

		lock ( _lock )
		{
			// Cherche le plus petit ID libre (au cas ou certains ont ete liberes)
			int candidate = 1;
			while ( _idToSteam.ContainsKey( candidate ) ) candidate++;
			if ( candidate >= _nextId ) _nextId = candidate + 1;

			_idToSteam[candidate] = steam;
			_steamToId[steam]     = candidate;

			// Push l ID sur le composant Player pour qu il soit synced aux clients
			var player = Player.FindForConnection( conn );
			if ( player is not null ) player.PublicId = candidate;

			Log.Info( $"[PlayerId] {conn.DisplayName} → ID #{candidate}" );
			return candidate;
		}
	}

	/// <summary>Libere l ID quand le joueur quitte.</summary>
	public static void Release( Connection conn )
	{
		if ( conn is null ) return;
		var steam = (long)conn.SteamId.Value;
		if ( _steamToId.TryRemove( steam, out var id ) )
		{
			_idToSteam.TryRemove( id, out _ );
			Log.Info( $"[PlayerId] ID #{id} libere ({conn.DisplayName})" );
		}
	}

	public static int GetIdForSteam( long steamId ) =>
		_steamToId.TryGetValue( steamId, out var id ) ? id : 0;

	public static long GetSteamById( int id ) =>
		_idToSteam.TryGetValue( id, out var steam ) ? steam : 0;

	public static Connection GetConnectionById( int id )
	{
		var steam = GetSteamById( id );
		if ( steam == 0 ) return null;
		return Connection.All.FirstOrDefault( c => (long)c.SteamId.Value == steam );
	}

	public static Player GetPlayerById( int id )
	{
		var conn = GetConnectionById( id );
		return conn is null ? null : Player.FindForConnection( conn );
	}

	/// <summary>Snapshot pour push vers darkapi (heartbeat) ou debug.</summary>
	public static IReadOnlyDictionary<int, long> Snapshot() =>
		new Dictionary<int, long>( _idToSteam );
}
