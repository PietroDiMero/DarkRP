using Sandbox.UI;

public sealed partial class Player
{
	public const int MinRoleplayNameLength = 3;
	public const int MaxRoleplayNameLength = 24;

	[Property, Sync( SyncFlags.FromHost )]
	public int Money { get; private set; } = 2500;

	[Property, Sync( SyncFlags.FromHost )]
	public string JobTitle { get; private set; } = "Citizen";

	public bool CanAfford( int amount )
	{
		return amount >= 0 && amount <= Money;
	}

	public void GiveMoney( int amount )
	{
		if ( !Networking.IsHost || amount <= 0 )
			return;

		Money += amount;
		SaveRoleplayData();                                       // backup JSON local
		DarkDatabase.Instance?.SyncMoneyFromPlayer( SteamId, Money ); // persist MySQL
	}

	public bool TryTakeMoney( int amount )
	{
		if ( !Networking.IsHost || amount < 0 || !CanAfford( amount ) )
			return false;

		Money -= amount;
		SaveRoleplayData();
		DarkDatabase.Instance?.SyncMoneyFromPlayer( SteamId, Money );
		return true;
	}

	public void SetMoney( int amount )
	{
		if ( !Networking.IsHost )
			return;

		Money = Math.Max( 0, amount );
		SaveRoleplayData();
		DarkDatabase.Instance?.SyncMoneyFromPlayer( SteamId, Money );
	}

	public void SetJobTitle( string title )
	{
		if ( !Networking.IsHost )
			return;

		JobTitle = string.IsNullOrWhiteSpace( title ) ? "Citizen" : title.Trim();
		if ( PlayerData.IsValid() )
		{
			PlayerData.JobTitle = JobTitle;
		}
	}

	public void SetRoleplayName( string roleplayName )
	{
		if ( !Networking.IsHost || !PlayerData.IsValid() )
			return;

		if ( !TryNormalizeRoleplayName( roleplayName, out var normalizedName, out _ ) )
			return;

		if ( string.Equals( PlayerData.DisplayName, normalizedName, StringComparison.Ordinal ) )
			return;

		PlayerData.DisplayName = normalizedName;
		GameObject.Name = normalizedName;
		SaveRoleplayData();
	}

	[Rpc.Host]
	public void RequestSetRoleplayName( string roleplayName )
	{
		if ( Rpc.Caller != Network.Owner )
			return;

		TryUpdateRoleplayName( roleplayName );
	}

	public bool TryUpdateRoleplayName( string roleplayName )
	{
		if ( !Networking.IsHost || Network.Owner is null )
			return false;

		if ( !TryNormalizeRoleplayName( roleplayName, out var normalizedName, out var error ) )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, error, 3 );
			return false;
		}

		if ( string.Equals( PlayerData?.DisplayName, normalizedName, StringComparison.Ordinal ) )
		{
			Notices.SendNotice( Network.Owner, "person", Color.Yellow, "You're already using this RP name.", 3 );
			return false;
		}

		if ( IsRoleplayNameTaken( normalizedName ) )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, "This RP name is already taken.", 3 );
			return false;
		}

		SetRoleplayName( normalizedName );
		Notices.SendNotice( Network.Owner, "person", Color.Green, $"RP name updated to {normalizedName}.", 3 );
		return true;
	}

	static bool TryNormalizeRoleplayName( string value, out string normalizedName, out string error )
	{
		normalizedName = null;
		error = $"RP name must be {MinRoleplayNameLength}-{MaxRoleplayNameLength} characters.";

		if ( string.IsNullOrWhiteSpace( value ) )
			return false;

		normalizedName = string.Join( " ", value.Trim().Split( ' ', StringSplitOptions.RemoveEmptyEntries ) );
		if ( normalizedName.Length < MinRoleplayNameLength || normalizedName.Length > MaxRoleplayNameLength )
			return false;

		foreach ( var character in normalizedName )
		{
			if ( char.IsLetterOrDigit( character ) || character is ' ' or '-' or '_' or '\'' )
				continue;

			error = "RP name can only contain letters, numbers, spaces, apostrophes, '-' or '_'.";
			normalizedName = null;
			return false;
		}

		error = null;
		return true;
	}

	bool IsRoleplayNameTaken( string normalizedName )
	{
		if ( string.IsNullOrWhiteSpace( normalizedName ) || Game.ActiveScene is null )
			return false;

		foreach ( var playerData in PlayerData.All )
		{
			if ( !playerData.IsValid() || playerData == PlayerData )
				continue;

			var existingName = NormalizeRoleplayNameSpacing( playerData.DisplayName );
			if ( string.Equals( existingName, normalizedName, StringComparison.OrdinalIgnoreCase ) )
				return true;
		}

		return false;
	}

	static string NormalizeRoleplayNameSpacing( string value )
	{
		return string.Join( " ", (value ?? string.Empty).Trim().Split( ' ', StringSplitOptions.RemoveEmptyEntries ) );
	}

	[Rpc.Host]
	public void RequestDropMoney( int amount )
	{
		if ( Rpc.Caller != Network.Owner )
			return;

		TryDropMoney( amount );
	}

	public bool TryDropMoney( int amount )
	{
		if ( !Networking.IsHost || Network.Owner is null )
			return false;

		if ( amount <= 0 )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, "Enter a valid amount to drop.", 3 );
			return false;
		}

		if ( !TryTakeMoney( amount ) )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, "You don't have enough money.", 3 );
			return false;
		}

		if ( MoneyStack.TrySpawn( this, amount ) )
			return true;

		GiveMoney( amount );
		Notices.SendNotice( Network.Owner, "block", Color.Red, "Unable to drop money right now.", 3 );
		return false;
	}
}
