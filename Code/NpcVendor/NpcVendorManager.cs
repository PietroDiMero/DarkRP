using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Données de position retournées par la DarkAPI.
/// </summary>
public sealed class NpcVendorPositionData
{
	[JsonPropertyName( "id" )]       public int    Id       { get; set; }
	[JsonPropertyName( "label" )]    public string Label    { get; set; } = "";
	[JsonPropertyName( "pos_x" )]    public float  PosX     { get; set; }
	[JsonPropertyName( "pos_y" )]    public float  PosY     { get; set; }
	[JsonPropertyName( "pos_z" )]    public float  PosZ     { get; set; }
	[JsonPropertyName( "angle_yaw" )] public float AngleYaw { get; set; }
}

public sealed class NpcVendorActiveResponse
{
	[JsonPropertyName( "vendor_id" )]   public int                   VendorId   { get; set; }
	[JsonPropertyName( "vendor_name" )] public string                VendorName { get; set; } = "";
	[JsonPropertyName( "type" )]        public string                Type       { get; set; } = "";
	[JsonPropertyName( "position" )]    public NpcVendorPositionData Position   { get; set; }
	[JsonPropertyName( "rotated" )]     public bool                  Rotated    { get; set; }
}

public sealed class NpcVendorConfigRow
{
	[JsonPropertyName( "id" )]   public int    Id   { get; set; }
	[JsonPropertyName( "name" )] public string Name { get; set; } = "";
	[JsonPropertyName( "type" )] public string Type { get; set; } = "printer";
	[JsonPropertyName( "is_active" )]                   public int IsActive                 { get; set; }
	[JsonPropertyName( "rotation_mode" )]               public string RotationMode          { get; set; } = "reboot";
	[JsonPropertyName( "rotation_interval_minutes" )]   public int RotationIntervalMinutes  { get; set; } = 120;
}

/// <summary>
/// Gère le cycle de vie du PNJ vendeur de printers :
///   - Au démarrage serveur : fetch la position active (trigger=reboot si mode reboot),
///     spawn le NPC.
///   - Si mode timer : vérifie toutes les minutes si une rotation est nécessaire
///     (la DarkAPI gère le vrai délai, on se contente de re-fetch).
/// </summary>
public sealed class NpcVendorManager : GameObjectSystem<NpcVendorManager>
{
	// Préfixe du GameObject NPC pour pouvoir le retrouver/détruire
	const string NpcObjectName = "__PrinterVendorNpc";
	const string NpcModelPath  = "models/citizen/citizen.vmdl";

	private TimeSince _timeSinceLastTimerCheck;

	public NpcVendorManager( Scene scene ) : base( scene )
	{
		Listen( Stage.StartupBeforeNet, 1, OnServerStartup, "NpcVendorManager.Startup" );
		Listen( Stage.UpdateWithObject, 1000, OnUpdate, "NpcVendorManager.Update" );
	}

	// ── Démarrage ─────────────────────────────────────────────────────────

	async void OnServerStartup()
	{
		if ( !Networking.IsHost ) return;
		await SpawnAllVendorsAsync( isReboot: true );
	}

	// ── Update (vérifie rotation timer toutes les 60s) ────────────────────

	void OnUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( _timeSinceLastTimerCheck < 60f ) return;
		_timeSinceLastTimerCheck = 0;

		_ = RefreshTimerVendorsAsync();
	}

	// ── Spawn / refresh ───────────────────────────────────────────────────

	/// <summary>Spawn ou repositionne tous les vendors actifs.</summary>
	public static async Task SpawnAllVendorsAsync( bool isReboot = false )
	{
		NpcVendorConfigRow[] configs;
		try
		{
			var json = await DarkHttpClient.GetStringAsync( "npc-vendors" );
			configs = JsonSerializer.Deserialize<NpcVendorConfigRow[]>( json )
			          ?? System.Array.Empty<NpcVendorConfigRow>();
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[NpcVendorManager] Impossible de lire la liste des vendors : {ex.Message}" );
			return;
		}

		foreach ( var cfg in configs )
		{
			if ( cfg.IsActive == 0 ) continue;
			await SpawnVendorAsync( cfg, isReboot );
		}
	}

	/// <summary>Re-fetch et repositionne les vendors en mode timer si besoin.</summary>
	static async Task RefreshTimerVendorsAsync()
	{
		NpcVendorConfigRow[] configs;
		try
		{
			var json = await DarkHttpClient.GetStringAsync( "npc-vendors" );
			configs = JsonSerializer.Deserialize<NpcVendorConfigRow[]>( json )
			          ?? System.Array.Empty<NpcVendorConfigRow>();
		}
		catch { return; }

		foreach ( var cfg in configs.Where( c => c.IsActive == 1 && c.RotationMode == "timer" ) )
		{
			await SpawnVendorAsync( cfg, isReboot: false );
		}
	}

	/// <summary>Fetch la position active et (re)place le NPC.</summary>
	static async Task SpawnVendorAsync( NpcVendorConfigRow cfg, bool isReboot )
	{
		var triggerParam = isReboot && cfg.RotationMode == "reboot" ? "?trigger=reboot" : "";
		NpcVendorActiveResponse resp;

		try
		{
			var json = await DarkHttpClient.GetStringAsync( $"npc-vendors/{cfg.Id}/position{triggerParam}" );
			resp = JsonSerializer.Deserialize<NpcVendorActiveResponse>( json );
			if ( resp?.Position is null ) return;
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[NpcVendorManager] Pas de position pour vendor {cfg.Id} : {ex.Message}" );
			return;
		}

		PlaceNpc( cfg.Id, cfg.Name, resp.Position );
	}

	/// <summary>Crée ou déplace le GameObject NPC pour ce vendor.</summary>
	static void PlaceNpc( int vendorId, string vendorName, NpcVendorPositionData pos )
	{
		var scene    = Game.ActiveScene;
		if ( scene is null ) return;

		var objName  = $"{NpcObjectName}_{vendorId}";

		// Supprime l'ancien NPC s'il existe
		var existing = scene.GetAllObjects( false ).FirstOrDefault( o => o.Name == objName );
		existing?.Destroy();

		var go = scene.CreateObject();
		go.Name      = objName;
		go.WorldPosition = new Vector3( pos.PosX, pos.PosY, pos.PosZ );
		go.WorldRotation = Rotation.FromYaw( pos.AngleYaw );
		go.NetworkSpawn();

		var model = go.Components.Create<SkinnedModelRenderer>();
		model.Model = Model.Load( NpcModelPath );

		var npc = go.Components.Create<PrinterVendorNpc>();
		npc.VendorId   = vendorId;
		npc.VendorName = vendorName;

		Log.Info( $"[NpcVendorManager] NPC '{vendorName}' (id={vendorId}) placé à ({pos.PosX:F0},{pos.PosY:F0},{pos.PosZ:F0})" );
	}
}
