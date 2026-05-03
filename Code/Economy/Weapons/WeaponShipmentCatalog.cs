public sealed class WeaponShipmentItemDefinition
{
	public WeaponShipmentItemDefinition( string weaponPrefabPath, string title, int price, string description, int weaponsPerShipment = 10, bool gunDealerOnly = true )
	{
		WeaponPrefabPath = weaponPrefabPath;
		Title = title;
		Price = price;
		Description = description;
		WeaponsPerShipment = weaponsPerShipment;
		GunDealerOnly = gunDealerOnly;
	}

	public string WeaponPrefabPath { get; }
	public string Title { get; }
	public int Price { get; }
	public string Description { get; }
	public int WeaponsPerShipment { get; }
	public bool GunDealerOnly { get; }
}

public static class WeaponShipmentCatalog
{
	static readonly WeaponShipmentItemDefinition[] Items =
	[
		// Caisses légères
		new( "weapons/glock/glock.prefab",     "Caisse USP",     5500,  "Une caisse de 10 pistolets USP à revendre.", 10, true ),
		new( "weapons/colt1911/colt1911.prefab","Caisse 1911",   7000,  "Une caisse de 10 Colt 1911 à revendre.", 10, true ),

		// Caisses moyennes
		new( "weapons/mp5/mp5.prefab",         "Caisse SMG",    16000, "Une caisse de 10 SMGs.", 10, true ),
		new( "weapons/shotgun/shotgun.prefab",  "Caisse Shotgun",22000, "Une caisse de 10 shotguns.", 10, true ),

		// Caisses lourdes
		new( "weapons/m4a1/m4a1.prefab",       "Caisse M4A1",   38000, "Une caisse de 10 M4A1.", 10, true ),
		new( "weapons/sniper/sniper.prefab",    "Caisse Sniper", 60000, "Une caisse de 10 snipers.", 10, true ),
		new( "weapons/rpg/rpg.prefab",          "Caisse RPG",   150000, "Une caisse de 10 lance-roquettes. Extrêmement rare.", 10, true )
	];

	public static IReadOnlyList<WeaponShipmentItemDefinition> GetAll()
	{
		return Items;
	}

	public static WeaponShipmentItemDefinition Get( string weaponPrefabPath )
	{
		if ( string.IsNullOrWhiteSpace( weaponPrefabPath ) )
			return null;

		return Items.FirstOrDefault( x => string.Equals( x.WeaponPrefabPath, weaponPrefabPath, StringComparison.OrdinalIgnoreCase ) );
	}

	public static bool ShouldShowInShop( Player player, WeaponShipmentItemDefinition item )
	{
		if ( item is null )
			return false;

		return !item.GunDealerOnly || WeaponShopCatalog.IsGunDealer( player );
	}

	public static bool CanPlayerBuy( Player player, string weaponPrefabPath, out string reason )
	{
		reason = null;

		var item = Get( weaponPrefabPath );
		if ( item is null )
		{
			reason = "Unknown shipment.";
			return false;
		}

		if ( !item.GunDealerOnly )
			return true;

		if ( player is null )
		{
			reason = "Player unavailable.";
			return false;
		}

		if ( !WeaponShopCatalog.IsGunDealer( player ) )
		{
			reason = "Gun Dealer only.";
			return false;
		}

		return true;
	}
}
