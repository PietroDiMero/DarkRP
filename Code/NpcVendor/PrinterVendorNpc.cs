namespace Sandbox;

/// <summary>
/// Composant attaché au GameObject du PNJ vendeur de printers.
/// Détecte la touche E d'un joueur qui regarde le NPC et ouvre le menu.
/// </summary>
public sealed class PrinterVendorNpc : Component
{
	[Property, Sync] public int    VendorId   { get; set; }
	[Property, Sync] public string VendorName { get; set; } = "Vendeur";

	const float InteractDistance = 120f;
	const float InteractDotMin   = 0.5f;  // ~60° de cône

	protected override void OnUpdate()
	{
		if ( !IsLocalPlayer() ) return;

		var player = Player.FindLocalPlayer();
		if ( player is null ) return;

		var dist = WorldPosition.Distance( player.WorldPosition );
		if ( dist > InteractDistance ) return;

		// Vérifie que le joueur regarde vers le NPC
		var toNpc = (WorldPosition - player.EyeTransform.Position).Normal;
		var look  = player.EyeTransform.Rotation.Forward;
		if ( Vector3.Dot( toNpc, look ) < InteractDotMin ) return;

		InteractionHintState.Show( VendorName );

		if ( !Input.Pressed( "use" ) ) return;

		Input.Clear( "use" );
		PrinterShopUi.Open( VendorId );
	}

	static bool IsLocalPlayer()
	{
		return !Application.IsDedicatedServer;
	}
}
