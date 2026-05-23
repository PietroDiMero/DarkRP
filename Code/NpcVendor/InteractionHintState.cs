namespace Sandbox;

/// <summary>
/// Slot partagé pour les hints d'interaction in-world.
/// Un composant qui veut afficher un hint appelle <see cref="Show"/> chaque frame.
/// La UI (<c>InteractionHintPanel</c>) lit <see cref="IsActive"/> et <see cref="Label"/>.
/// Si personne n'appelle Show pendant plus de 200 ms, le hint disparaît automatiquement.
/// </summary>
public static class InteractionHintState
{
	static RealTimeSince _lastSet;
	const float MaxAge = 0.2f;

	/// <summary>Libellé à afficher (ex. "Ouvrir la boutique").</summary>
	public static string Label  { get; private set; }

	/// <summary>Action S&amp;Box dont on affiche le glyph (ex. "use" → touche E / bouton A).</summary>
	public static string Action { get; private set; }

	/// <summary>True si le hint doit être visible ce frame.</summary>
	public static bool IsActive => (float)_lastSet < MaxAge && !string.IsNullOrEmpty( Label );

	/// <summary>
	/// Appeler chaque frame quand un joueur est en position d'interagir.
	/// </summary>
	public static void Show( string label, string action = "use" )
	{
		Label   = label;
		Action  = action;
		_lastSet = 0;
	}
}
