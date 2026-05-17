[AssetType( Name = "DarkRP Printer", Extension = "pdef", Category = "DarkRP", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class MoneyPrinterDefinition : GameResource, IDefinitionResource
{
	public const int MaxOwnedPerPlayer = 5;
	public const string DefaultResourcePath = "entities/printer/bronze.pdef";

	[Property]
	public PrefabFile Prefab { get; set; }

	[Property]
	public string Title { get; set; }

	[Property]
	public string Description { get; set; }

	[Property]
	public int Price { get; set; }

	[Property]
	public int MoneyPerTick { get; set; } = 25;

	[Property]
	public float Interval { get; set; } = 20.0f;

	[Property]
	public int MaxStoredMoney { get; set; } = 8000;

	[Property]
	public Color Tint { get; set; } = Color.White;

	public static IReadOnlyList<MoneyPrinterDefinition> GetAll()
	{
		return ResourceLibrary.GetAll<MoneyPrinterDefinition>()
			.Where( x => x.Prefab is not null )
			.OrderBy( x => x.Price )
			.ToArray();
	}

	public static MoneyPrinterDefinition Get( string resourcePath )
	{
		if ( string.IsNullOrWhiteSpace( resourcePath ) )
			return null;

		return ResourceLibrary.Get<MoneyPrinterDefinition>( resourcePath );
	}

	/// <summary>
	/// Applique les overrides reçus depuis le panel admin sur les .pdef en mémoire.
	/// Match par ResourcePath (clé stable côté admin). Modifie les properties en place
	/// — les MoneyPrinter spawn après l'override liront les nouvelles valeurs.
	/// Les printers déjà placés en jeu conservent leurs paramètres jusqu'au prochain spawn.
	/// </summary>
	public static void ApplyOverrides( IEnumerable<PrinterOverrideDto> overrides )
	{
		if ( overrides is null ) return;

		var byPath = ResourceLibrary.GetAll<MoneyPrinterDefinition>()
			.Where( d => !string.IsNullOrWhiteSpace( d.ResourcePath ) )
			.ToDictionary( d => d.ResourcePath, StringComparer.OrdinalIgnoreCase );

		var appliedCount = 0;
		foreach ( var o in overrides )
		{
			if ( !o.IsActive ) continue;
			if ( string.IsNullOrWhiteSpace( o.ResourcePath ) ) continue;
			if ( !byPath.TryGetValue( o.ResourcePath, out var def ) ) continue;

			// On override seulement les valeurs numériques / textuelles, pas le Prefab.
			if ( !string.IsNullOrWhiteSpace( o.DisplayName ) ) def.Title = o.DisplayName;
			if ( !string.IsNullOrWhiteSpace( o.Description ) ) def.Description = o.Description;
			def.Price          = o.Price;
			def.MoneyPerTick   = o.MoneyPerTick;
			def.Interval       = o.IntervalSeconds > 0 ? o.IntervalSeconds : def.Interval;
			def.MaxStoredMoney = o.MaxStored > 0 ? o.MaxStored : def.MaxStoredMoney;

			if ( !string.IsNullOrWhiteSpace( o.TintHex ) && Color.TryParse( o.TintHex, out var c ) )
			{
				def.Tint = c;
			}

			appliedCount++;
		}

		Log.Info( $"[MoneyPrinterDefinition] {appliedCount} override(s) appliqués sur {byPath.Count} définition(s) connue(s)." );
	}

	public override Bitmap RenderThumbnail( ThumbnailOptions options )
	{
		if ( Prefab is null )
			return default;

		var bitmap = new Bitmap( options.Width, options.Height );
		bitmap.Clear( Color.Transparent );

		SceneUtility.RenderGameObjectToBitmap( Prefab.GetScene(), bitmap );
		return bitmap;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "$", width, height, "#35B851" );
	}
}
