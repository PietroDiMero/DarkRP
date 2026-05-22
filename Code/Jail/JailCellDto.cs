using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// DTO renvoyé par GET /jail-cells.
/// </summary>
public sealed class JailCellDto
{
	[JsonPropertyName( "id" )]        public int    Id       { get; set; }
	[JsonPropertyName( "label" )]     public string Label    { get; set; } = "";
	[JsonPropertyName( "pos_x" )]     public float  PosX     { get; set; }
	[JsonPropertyName( "pos_y" )]     public float  PosY     { get; set; }
	[JsonPropertyName( "pos_z" )]     public float  PosZ     { get; set; }
	[JsonPropertyName( "angle_yaw" )] public float  AngleYaw { get; set; }
	[JsonPropertyName( "is_active" )] public bool   IsActive { get; set; } = true;
}
