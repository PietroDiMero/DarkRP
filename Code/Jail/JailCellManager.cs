using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Charge les cellules de prison depuis la DarkAPI et crée dynamiquement
/// les JailCellMarker correspondants dans la scène (s'ajoutent aux markers
/// placés manuellement dans l'éditeur).
///
/// Gère aussi la ré-application de la peine lors du reconnect d'un joueur
/// dont is_jailed=1 est encore valide en BDD.
/// </summary>
public sealed class JailCellManager : GameObjectSystem<JailCellManager>
{
	// GameObjects créés dynamiquement depuis la BDD (pour pouvoir les détruire au reload)
	readonly List<GameObject> _spawnedCells = new();

	public JailCellManager( Scene scene ) : base( scene )
	{
		if ( !Networking.IsHost ) return;
		_ = LoadCellsAsync();
	}

	// ── Chargement depuis la DarkAPI ────────────────────────────────────────

	async Task LoadCellsAsync()
	{
		await Task.Yield();

		var cells = await DarkHttpClient.GetAsync<List<JailCellDto>>( "jail-cells" );

		// Supprimer les anciens GameObjects issus de la BDD (pas les markers de scène)
		foreach ( var go in _spawnedCells )
		{
			if ( go.IsValid() ) go.Destroy();
		}
		_spawnedCells.Clear();

		if ( cells is null || cells.Count == 0 )
		{
			Log.Info( "[JailCellManager] Aucune cellule en BDD (ou API inaccessible). "
			        + "Les JailCellMarkers placés dans la scène restent actifs." );
			return;
		}

		int count = 0;

		foreach ( var cell in cells )
		{
			if ( !cell.IsActive ) continue;

			var goName = string.IsNullOrEmpty( cell.Label )
				? $"JailCell_DB_{cell.Id}"
				: $"JailCell_DB_{cell.Label}";

			var go = new GameObject( true, goName )
			{
				WorldPosition = new Vector3( cell.PosX, cell.PosY, cell.PosZ ),
				WorldRotation = Rotation.FromYaw( cell.AngleYaw ),
			};

			var marker = go.AddComponent<JailCellMarker>();
			marker.EnabledForArrests = true;

			_spawnedCells.Add( go );
			count++;
		}

		Log.Info( $"[JailCellManager] ✅ {count} cellule(s) DB chargée(s)." );
	}

	/// <summary>
	/// Recharge les cellules depuis la BDD.
	/// Appelé par PendingActionsPoller après une action panel "reload_jail_cells".
	/// </summary>
	public Task ReloadAsync() => LoadCellsAsync();

	// ── Reconnect : ré-application de la peine ──────────────────────────────

	/// <summary>
	/// Vérifie si un joueur connecté avait une peine active en BDD et la ré-applique.
	/// À appeler depuis DarkDatabase.SyncPlayerDataOnConnectAsync juste après SetMoney.
	/// </summary>
	public static void ApplyJailOnConnect( Player player, PlayerRecord record )
	{
		if ( !Networking.IsHost || !player.IsValid() || !record.IsJailed )
			return;

		// Peine expirée pendant la déconnexion ?
		if ( record.JailUntil.HasValue && record.JailUntil.Value <= System.DateTime.UtcNow )
		{
			Log.Info( $"[JailCellManager] Peine expirée pour {record.SteamName} ({record.SteamId}) — nettoyage BDD." );
			DarkDatabase.Instance?.SetJail( record.SteamId, false );
			return;
		}

		float remaining;
		if ( record.JailUntil.HasValue )
		{
			remaining = (float)(record.JailUntil.Value - System.DateTime.UtcNow).TotalSeconds;
		}
		else
		{
			// Jail sans durée définie → on applique 1h
			remaining = 3600f;
		}

		if ( remaining <= 0f )
		{
			DarkDatabase.Instance?.SetJail( record.SteamId, false );
			return;
		}

		Log.Info( $"[JailCellManager] {record.SteamName} reconnecté — peine restante : {remaining:0}s." );
		player.BeginArrest( null, remaining );
	}
}
