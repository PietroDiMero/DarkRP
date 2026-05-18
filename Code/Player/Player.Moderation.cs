namespace Sandbox;

/// <summary>
/// Partial class Player : actions de modération volatiles (non persistées au reload).
/// - <see cref="IsFrozen"/> : empêche tout input/mouvement (set par le panel via PendingActionDispatcher).
/// - <see cref="IsChatMuted"/> : empêche le joueur d'envoyer des messages chat.
///
/// Les états mute_voice / mute_chat / is_frozen sont persistés côté BDD par le panel
/// (table `players`) AVANT que la pending_action ne soit poussée. Côté in-game on
/// applique juste l'effet immédiat. Au reload serveur, le re-sync se ferait via
/// PlayerData.OnConnect (à implémenter si besoin de durabilité).
/// </summary>
public sealed partial class Player
{
	[Property, Sync( SyncFlags.FromHost )]
	public bool IsFrozen { get; set; }

	[Property, Sync( SyncFlags.FromHost )]
	public bool IsChatMuted { get; set; }

	/// <summary>Set l'état "gelé" du joueur. Server-side uniquement.</summary>
	public void SetFrozen( bool frozen )
	{
		if ( !Networking.IsHost ) return;
		IsFrozen = frozen;

		if ( frozen && Controller.IsValid() && Controller.Body.IsValid() )
		{
			// Stoppe net la vélocité actuelle pour l'effet immédiat
			Controller.Body.Velocity = Vector3.Zero;
		}
	}

	/// <summary>Set l'état "muet chat" du joueur. Server-side uniquement.</summary>
	public void SetChatMuted( bool muted )
	{
		if ( !Networking.IsHost ) return;
		IsChatMuted = muted;
	}
}
