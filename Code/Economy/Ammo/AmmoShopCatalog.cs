public sealed class AmmoShopItemDefinition
{
	public AmmoShopItemDefinition( string prefabPath, string title, int price, string description, bool gunDealerOnly = false )
	{
		PrefabPath = prefabPath;
		Title = title;
		Price = price;
		Description = description;
		GunDealerOnly = gunDealerOnly;
	}

	public string PrefabPath { get; }
	public string Title { get; }
	public int Price { get; }
	public string Description { get; }
	public bool GunDealerOnly { get; }
}

public static class AmmoShopCatalog
{
	static readonly AmmoShopItemDefinition[] _defaultItems =
	[
		new( "entities/pickup/ammo_9mm.prefab",    "Munitions Pistolet", 200,  "Pack de 30 balles pour pistolet." ),
		new( "entities/pickup/ammo_rifle.prefab",  "Munitions Fusil",    400,  "Pack de 60 balles pour fusil." ),
		new( "entities/pickup/ammo_shotgun.prefab","Munitions Shotgun",  350,  "Pack de 18 cartouches pour shotgun." ),
		new( "entities/pickup/ammo_rocket.prefab", "Roquettes",          3000, "Deux roquettes pour lance-roquettes.", true )
	];

	static List<AmmoShopItemDefinition> _items = _defaultItems.ToList();

	public static IReadOnlyList<AmmoShopItemDefinition> GetAll()
	{
		return _items;
	}

	public static AmmoShopItemDefinition Get( string prefabPath )
	{
		if ( string.IsNullOrWhiteSpace( prefabPath ) )
			return null;

		return _items.FirstOrDefault( x => string.Equals( x.PrefabPath, prefabPath, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>Remplace le catalogue par les overrides venus du panel (category=ammo).</summary>
	public static void ApplyOverrides( IEnumerable<ShopItemOverrideDto> overrides )
	{
		if ( overrides is null ) return;

		var active = overrides
			.Where( o => o != null && o.IsActive && !string.IsNullOrWhiteSpace( o.PrefabPath ) )
			.Select( o => new AmmoShopItemDefinition(
				o.PrefabPath,
				o.DisplayName ?? o.PrefabPath,
				o.Price,
				o.Description ?? "",
				o.GunDealerOnly
			) )
			.ToList();

		if ( active.Count == 0 )
		{
			Log.Warning( "[AmmoShopCatalog] Override list vide — fallback sur defaults." );
			_items = _defaultItems.ToList();
			return;
		}

		_items = active;
		Log.Info( $"[AmmoShopCatalog] {_items.Count} ammo(s) actifs après override." );
	}

	public static bool ShouldShowInShop( Player player, AmmoShopItemDefinition item )
	{
		if ( item is null )
			return false;

		// Pietro DarkRP : munitions reservees au Gun Dealer.
		return WeaponShopCatalog.IsGunDealer( player );
	}

	public static bool CanPlayerBuy( Player player, string prefabPath, out string reason )
	{
		reason = null;

		var item = Get( prefabPath );
		if ( item is null )
		{
			reason = "Unknown ammo.";
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

	public static bool TryGetPickupAmmo( string prefabPath, out AmmoResource ammoType, out int ammoAmount )
	{
		ammoType = null;
		ammoAmount = 0;

		var prefab = GameObject.GetPrefab( prefabPath );
		var pickup = prefab?.GetComponent<AmmoPickup>( true );
		if ( !pickup.IsValid() || pickup.AmmoType is null || pickup.AmmoAmount <= 0 )
			return false;

		ammoType = pickup.AmmoType;
		ammoAmount = pickup.AmmoAmount;
		return true;
	}
}
