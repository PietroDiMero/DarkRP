public sealed class WeaponShopItemDefinition
{
	public WeaponShopItemDefinition( string prefabPath, string title, int price, string description, bool gunDealerOnly = false )
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

public static class WeaponShopCatalog
{
	public const string GunDealerJobDefinitionPath = "jobs/gun_dealer.jobdef";

	// Defaults hardcodés — utilisés au boot avant le premier refresh, et en fallback
	// si la BDD est vide ou injoignable.
	static readonly WeaponShopItemDefinition[] _defaultItems =
	[
		// Armes accessibles à tous
		new( "weapons/crowbar/crowbar.prefab",     "Crowbar",         150,   "Une arme de mêlée basique. Bon marché et efficace au corps à corps." ),
		new( "weapons/glock/glock.prefab",         "USP",             700,   "Un pistolet fiable pour les altercations rapprochées." ),
		new( "weapons/colt1911/colt1911.prefab",   "1911",            900,   "Un pistolet plus puissant mais avec un chargeur réduit." ),

		// Armes moyennes — Gun Dealer uniquement
		new( "weapons/grenade/grenade.prefab",     "Grenade",         1500,  "Explosive à lancer pour déloger des joueurs retranchés.", true ),
		new( "weapons/mp5/mp5.prefab",             "SMG",             2200,  "Mitraillette efficace à courte portée.", true ),
		new( "weapons/shotgun/shotgun.prefab",     "Shotgun",         3000,  "Dévastatrice à bout portant.", true ),

		// Armes lourdes — Gun Dealer uniquement, investissement sérieux
		new( "weapons/m4a1/m4a1.prefab",           "M4A1",            5000,  "Fusil d'assaut équilibré, efficace dans la plupart des situations.", true ),
		new( "weapons/sniper/sniper.prefab",       "Sniper",          8000,  "Fusil de précision haute portée. Rare et cher.", true ),
		new( "weapons/rpg/rpg.prefab",             "Rocket Launcher", 20000, "Lance-roquettes. Extrêmement coûteux et dangereux.", true )
	];

	// État effectif consulté par le jeu — mutable, remplaçable via ApplyOverrides
	static List<WeaponShopItemDefinition> _items = _defaultItems.ToList();

	public static IReadOnlyList<WeaponShopItemDefinition> GetAll()
	{
		return _items;
	}

	public static WeaponShopItemDefinition Get( string prefabPath )
	{
		if ( string.IsNullOrWhiteSpace( prefabPath ) )
			return null;

		return _items.FirstOrDefault( x => string.Equals( x.PrefabPath, prefabPath, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// Remplace le catalogue par les overrides venus du panel (table shop_items).
	/// Si la liste est vide, on garde les defaults (fallback de sécurité).
	/// </summary>
	public static void ApplyOverrides( IEnumerable<ShopItemOverrideDto> overrides )
	{
		if ( overrides is null ) return;

		var active = overrides
			.Where( o => o != null && o.IsActive && !string.IsNullOrWhiteSpace( o.PrefabPath ) )
			.Select( o => new WeaponShopItemDefinition(
				o.PrefabPath,
				o.DisplayName ?? o.PrefabPath,
				o.Price,
				o.Description ?? "",
				o.GunDealerOnly
			) )
			.ToList();

		if ( active.Count == 0 )
		{
			Log.Warning( "[WeaponShopCatalog] Override list vide — fallback sur defaults." );
			_items = _defaultItems.ToList();
			return;
		}

		_items = active;
		Log.Info( $"[WeaponShopCatalog] {_items.Count} item(s) actifs après override." );
	}

	public static bool ShouldShowInShop( Player player, WeaponShopItemDefinition item )
	{
		if ( item is null )
			return false;

		// Pietro DarkRP : seul le Gun Dealer voit/achete des armes.
		return IsGunDealer( player );
	}

	public static bool CanPlayerBuy( Player player, string prefabPath, out string reason )
	{
		reason = null;

		var item = Get( prefabPath );
		if ( item is null )
		{
			reason = "Unknown weapon.";
			return false;
		}

		if ( !item.GunDealerOnly )
			return true;

		if ( player is null )
		{
			reason = "Player unavailable.";
			return false;
		}

		if ( !IsGunDealer( player ) )
		{
			reason = "Gun Dealer only.";
			return false;
		}

		return true;
	}

	public static bool IsGunDealer( Player player )
	{
		var job = player?.CurrentJobDefinition;
		if ( job is null )
			return false;

		if ( string.Equals( job.ResourcePath, GunDealerJobDefinitionPath, StringComparison.OrdinalIgnoreCase ) )
			return true;

		if ( string.Equals( job.Command, "/gundealer", StringComparison.OrdinalIgnoreCase ) )
			return true;

		return string.Equals( job.Title, "Gun Dealer", StringComparison.OrdinalIgnoreCase );
	}
}
