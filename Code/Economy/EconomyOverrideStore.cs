using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Récupère les overrides économie depuis le panel via darkapi et les applique
/// aux catalogues in-memory (printers + shops).
///
/// Endpoints consommés :
///   GET /economy/printers/list                    → tous les printers actifs
///   GET /economy/shops/list?category=weapon|...   → shop_items actifs filtrés
///
/// Appelé :
///   - Au boot serveur (via EconomyBootstrapper / hook DarkDatabase.Initialize)
///   - À chaque pending_action(action=refresh_economy) push par le panel admin
///
/// Les overrides remplacent les valeurs hardcodées par défaut dans les catalogues.
/// Si l'API est down, on garde les valeurs précédentes (ou les defaults au premier boot).
/// </summary>
public static class EconomyOverrideStore
{
	public static int LastPrinterCount { get; private set; }
	public static int LastShopCount    { get; private set; }
	public static DateTime LastRefresh { get; private set; }

	/// <summary>Rafraîchit tous les catalogues (printers + 4 shop categories).</summary>
	public static async Task<bool> RefreshAllAsync()
	{
		var ok = true;
		ok &= await RefreshPrintersAsync();
		ok &= await RefreshShopsAsync( "weapon" );
		ok &= await RefreshShopsAsync( "shipment" );
		ok &= await RefreshShopsAsync( "ammo" );
		ok &= await RefreshShopsAsync( "misc" );
		LastRefresh = DateTime.UtcNow;
		Log.Info( $"[EconomyOverrideStore] Refresh terminé. {LastPrinterCount} printers, {LastShopCount} shop items au total." );
		return ok;
	}

	public static async Task<bool> RefreshPrintersAsync()
	{
		var list = await DarkHttpClient.GetAsync<PrinterOverrideDto[]>( "economy/printers/list" );
		if ( list is null )
		{
			Log.Warning( "[EconomyOverrideStore] GET /economy/printers/list a échoué — overrides non appliqués." );
			return false;
		}

		MoneyPrinterDefinition.ApplyOverrides( list );
		LastPrinterCount = list.Length;
		Log.Info( $"[EconomyOverrideStore] {list.Length} printer override(s) appliqués." );
		return true;
	}

	/// <param name="category">weapon | shipment | ammo | misc</param>
	public static async Task<bool> RefreshShopsAsync( string category )
	{
		var list = await DarkHttpClient.GetAsync<ShopItemOverrideDto[]>( $"economy/shops/list?category={category}" );
		if ( list is null )
		{
			Log.Warning( $"[EconomyOverrideStore] GET /economy/shops/list?category={category} a échoué." );
			return false;
		}

		switch ( category )
		{
			case "weapon":   WeaponShopCatalog.ApplyOverrides( list );     break;
			case "shipment": WeaponShipmentCatalog.ApplyOverrides( list ); break;
			case "ammo":     AmmoShopCatalog.ApplyOverrides( list );       break;
			case "misc":     MiscShopCatalog.ApplyOverrides( list );       break;
			default:
				Log.Warning( $"[EconomyOverrideStore] Catégorie inconnue '{category}'." );
				return false;
		}

		LastShopCount = (LastShopCount + list.Length); // approximation cumulative
		Log.Info( $"[EconomyOverrideStore] {list.Length} {category} item override(s) appliqués." );
		return true;
	}
}

// ─────────────────────────────────────────────────────────── DTOs (parsing JSON)

public sealed class PrinterOverrideDto
{
	[JsonPropertyName( "id" )]               public int Id { get; set; }
	[JsonPropertyName( "code" )]             public string Code { get; set; }
	[JsonPropertyName( "prefab_path" )]      public string PrefabPath { get; set; }
	[JsonPropertyName( "resource_path" )]    public string ResourcePath { get; set; }
	[JsonPropertyName( "display_name" )]     public string DisplayName { get; set; }
	[JsonPropertyName( "description" )]      public string Description { get; set; }
	[JsonPropertyName( "price" )]            public int Price { get; set; }
	[JsonPropertyName( "money_per_tick" )]   public int MoneyPerTick { get; set; }
	[JsonPropertyName( "interval_seconds" )] public float IntervalSeconds { get; set; }
	[JsonPropertyName( "max_stored" )]       public int MaxStored { get; set; }
	[JsonPropertyName( "tint_hex" )]         public string TintHex { get; set; }
	[JsonPropertyName( "max_per_player" )]   public int MaxPerPlayer { get; set; }
	[JsonPropertyName( "is_active" )]        public bool IsActive { get; set; }
	[JsonPropertyName( "display_order" )]    public int DisplayOrder { get; set; }
}

public sealed class ShopItemOverrideDto
{
	[JsonPropertyName( "id" )]                   public int Id { get; set; }
	[JsonPropertyName( "category" )]             public string Category { get; set; }
	[JsonPropertyName( "prefab_path" )]          public string PrefabPath { get; set; }
	[JsonPropertyName( "display_name" )]         public string DisplayName { get; set; }
	[JsonPropertyName( "description" )]          public string Description { get; set; }
	[JsonPropertyName( "price" )]                public int Price { get; set; }
	[JsonPropertyName( "gun_dealer_only" )]      public bool GunDealerOnly { get; set; }
	[JsonPropertyName( "required_job_code" )]    public string RequiredJobCode { get; set; }
	[JsonPropertyName( "required_job_label" )]   public string RequiredJobLabel { get; set; }
	[JsonPropertyName( "weapons_per_shipment" )] public int? WeaponsPerShipment { get; set; }
	[JsonPropertyName( "is_active" )]            public bool IsActive { get; set; }
	[JsonPropertyName( "display_order" )]        public int DisplayOrder { get; set; }
}
