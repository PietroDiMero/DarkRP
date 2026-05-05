using Sandbox.UI;

public sealed partial class Player
{
	public const string CivilProtectionJobDefinitionPath = "jobs/civil_protection.jobdef";
	public const string PoliceChiefJobDefinitionPath = "jobs/police_chief.jobdef";
	public const string MayorJobDefinitionPath = "jobs/mayor.jobdef";
	const float ArrestDistance = 180.0f;
	const float ArrestDuration = 60.0f;
	const float ArrestRunSpeedMultiplier = 0.45f;
	float? preArrestRunSpeed;

	public bool IsWanted => PlayerData?.IsWanted == true;
	public bool IsArrested => PlayerData?.IsArrested == true;
	public string WantedReason => PlayerData?.WantedReason;
	public float ArrestTimeRemaining => PlayerData?.ArrestTimeRemaining ?? 0.0f;

	public bool IsCivilProtection
		=> string.Equals( JobDefinitionPath, CivilProtectionJobDefinitionPath, StringComparison.OrdinalIgnoreCase );

	public bool IsPoliceChief
		=> string.Equals( JobDefinitionPath, PoliceChiefJobDefinitionPath, StringComparison.OrdinalIgnoreCase );

	public bool IsMayor
		=> string.Equals( JobDefinitionPath, MayorJobDefinitionPath, StringComparison.OrdinalIgnoreCase );

	public bool CanArrestPlayers => IsCivilProtection || IsPoliceChief;
	public bool CanIssueWantedStatus => CanArrestPlayers || IsMayor;

	[Rpc.Host]
	public void RequestSetWantedStatus( Guid playerId, string reason, bool wanted )
	{
		if ( Rpc.Caller != Network.Owner || !CanIssueWantedStatus )
			return;

		var target = Player.For( playerId );
		if ( !target.IsValid() || target == this || !target.PlayerData.IsValid() )
			return;

		if ( wanted )
		{
			reason = reason?.Trim();
			if ( string.IsNullOrWhiteSpace( reason ) )
			{
				Notices.SendNotice( Network.Owner, "block", Color.Red, "Provide a wanted reason.", 3 );
				return;
			}

			target.SetWanted( reason, this );
			return;
		}

		target.ClearWanted( this );
	}

	[Rpc.Host]
	public void RequestArrestPlayer( Guid playerId )
	{
		if ( Rpc.Caller != Network.Owner || !CanArrestPlayers )
			return;

		var target = Player.For( playerId );
		if ( !target.IsValid() || target == this || !target.PlayerData.IsValid() )
			return;

		if ( target.IsArrested )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, "Target is already arrested.", 3 );
			return;
		}

		if ( WorldPosition.Distance( target.WorldPosition ) > ArrestDistance )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, "Get closer to arrest that player.", 3 );
			return;
		}

		target.BeginArrest( this );
	}

	[Rpc.Host]
	public void RequestReleaseArrestPlayer( Guid playerId )
	{
		if ( Rpc.Caller != Network.Owner || !CanArrestPlayers )
			return;

		var target = Player.For( playerId );
		if ( !target.IsValid() || target == this || !target.PlayerData.IsValid() )
			return;

		if ( !target.IsArrested )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, "Target is not arrested.", 3 );
			return;
		}

		if ( WorldPosition.Distance( target.WorldPosition ) > ArrestDistance )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, "Get closer to release that player.", 3 );
			return;
		}

		target.ReleaseFromArrest();

		if ( Network.Owner is { } actorConnection )
		{
			Notices.SendNotice( actorConnection, "check_circle", Color.Green, $"Released {target.DisplayName} from arrest.", 3 );
		}
	}

	[Rpc.Host]
	public void RequestDebugArrestSelf()
	{
		if ( Rpc.Caller != Network.Owner )
			return;

		if ( !PlayerData.IsValid() )
			return;

		if ( IsArrested )
		{
			ReleaseFromArrest();
			return;
		}

		BeginArrest( this );
	}

	[ConCmd( "darkrp.debug.arrestself", ConVarFlags.Cheat )]
	public static void DebugArrestSelfCommand()
	{
		var player = FindLocalPlayer();
		if ( !player.IsValid() )
			return;

		player.RequestDebugArrestSelf();
	}

	public void SetWanted( string reason, Player actor )
	{
		if ( !Networking.IsHost || !PlayerData.IsValid() )
			return;

		PlayerData.IsWanted = true;
		PlayerData.WantedReason = reason?.Trim();
		GameObject.Tags.Add( "wanted" );

		if ( Network.Owner is { } targetConnection )
		{
			Notices.SendNotice( targetConnection, "warning", Color.Orange, $"You are wanted: {PlayerData.WantedReason}", 4 );
		}

		if ( actor?.Network.Owner is { } actorConnection )
		{
			Notices.SendNotice( actorConnection, "gavel", Color.Green, $"{DisplayName} is now wanted.", 3 );
		}

		Scene.Get<Chat>()?.AddSystemText( $"{DisplayName} is now wanted: {PlayerData.WantedReason}", "🚨" );
	}

	public void ClearWanted( Player actor )
	{
		if ( !Networking.IsHost || !PlayerData.IsValid() || !PlayerData.IsWanted )
			return;

		PlayerData.IsWanted = false;
		PlayerData.WantedReason = null;
		GameObject.Tags.Remove( "wanted" );

		if ( Network.Owner is { } targetConnection )
		{
			Notices.SendNotice( targetConnection, "check_circle", Color.Green, "You are no longer wanted.", 3 );
		}

		if ( actor?.Network.Owner is { } actorConnection )
		{
			Notices.SendNotice( actorConnection, "check_circle", Color.Green, $"Cleared wanted status for {DisplayName}.", 3 );
		}
	}

	public void BeginArrest( Player officer )
	{
		if ( !Networking.IsHost || !PlayerData.IsValid() || IsArrested )
			return;

		PlayerData.IsArrested = true;
		PlayerData.ArrestTimeRemaining = ArrestDuration;
		PlayerData.IsWanted = false;
		PlayerData.WantedReason = null;
		GameObject.Tags.Remove( "wanted" );

		ApplyPrisonerClothingForCurrentArrest();

		var inventory = GetComponent<PlayerInventory>();
		if ( inventory.IsValid() )
		{
			foreach ( var weapon in inventory.Weapons.ToArray() )
			{
				if ( !weapon.IsValid() || weapon.IsJobLocked )
					continue;

				inventory.Remove( weapon );
			}

			inventory.SwitchWeapon( null, true );
		}

		if ( TryFindArrestLocation( out var arrestLocation ) )
		{
			ApplyPlayerTeleport( arrestLocation );

			if ( officer?.Network.Owner is { } officerSuccessConnection )
			{
				Notices.SendNotice( officerSuccessConnection, "check_circle", Color.Green, $"Moved {DisplayName} to a jail cell marker.", 3 );
			}
		}
		else if ( officer?.Network.Owner is { } officerNoticeConnection )
		{
			Notices.SendNotice( officerNoticeConnection, "block", Color.Orange, "No JailCellMarker found. Player was arrested in place.", 4 );
		}

		if ( Network.Owner is { } targetConnection )
		{
			Notices.SendNotice( targetConnection, "gavel", Color.Orange, $"You were arrested for {ArrestDuration:0} seconds.", 4 );
		}

		if ( officer?.Network.Owner is { } officerConnection )
		{
			Notices.SendNotice( officerConnection, "gavel", Color.Green, $"Arrested {DisplayName}.", 3 );
		}

		Scene.Get<Chat>()?.AddSystemText( $"{DisplayName} was arrested by {officer?.DisplayName ?? "the law"}.", "⛓" );
	}

	async void ApplyPrisonerClothingForCurrentArrest()
	{
		await ApplyPrisonerClothingAsync();

		if ( !IsArrested )
		{
			await RestoreJobClothingAsync();
		}
	}

	public void ReleaseFromArrest()
	{
		if ( !Networking.IsHost || !PlayerData.IsValid() || !PlayerData.IsArrested )
			return;

		PlayerData.IsArrested = false;
		PlayerData.ArrestTimeRemaining = 0.0f;

		_ = RestoreJobClothingAsync();

		if ( preArrestRunSpeed.HasValue && Controller.IsValid() )
		{
			Controller.RunSpeed = preArrestRunSpeed.Value;
		}

		preArrestRunSpeed = null;

		var releaseLocation = GameManager.Current?.FindSpawnLocation().WithScale( 1.0f ) ?? WorldTransform.WithScale( 1.0f );
		ApplyPlayerTeleport( releaseLocation );

		if ( Network.Owner is { } targetConnection )
		{
			Notices.SendNotice( targetConnection, "check_circle", Color.Green, "You have been released from arrest.", 3 );
		}
	}

	bool TryFindArrestLocation( out Transform arrestLocation )
	{
		arrestLocation = WorldTransform;

		var jailCells = Scene.GetAllComponents<JailCellMarker>()
			.Where( x => x.IsValid() && x.Enabled && x.EnabledForArrests && x.GameObject.IsValid() )
			.ToArray();

		if ( jailCells.Length == 0 )
			return false;

		arrestLocation = Random.Shared.FromArray( jailCells ).GetCellTransform().WithScale( 1.0f );
		return true;
	}

	async void ApplyPlayerTeleport( Transform targetLocation )
	{
		if ( !Networking.IsHost || !GameObject.IsValid() )
			return;

		var eyeAngles = targetLocation.Rotation.Angles();

		for ( var i = 0; i < 4; i++ )
		{
			if ( !GameObject.IsValid() )
				return;

			WorldPosition = targetLocation.Position;
			WorldRotation = targetLocation.Rotation;
			GameObject.Transform.ClearInterpolation();

			if ( Controller.IsValid() )
			{
				Controller.EyeAngles = eyeAngles;
				if ( Controller.Body.IsValid() )
				{
					Controller.Body.Velocity = Vector3.Zero;
				}
			}

			ForceTeleportOwner( targetLocation.Position, eyeAngles );

			if ( i < 3 )
				await Task.Delay( 50 );
		}
	}

	[Rpc.Owner( NetFlags.HostOnly | NetFlags.Reliable )]
	void ForceTeleportOwner( Vector3 position, Angles eyeAngles )
	{
		if ( !GameObject.IsValid() )
			return;

		WorldPosition = position;
		WorldRotation = Rotation.From( eyeAngles );
		GameObject.Transform.ClearInterpolation();

		if ( Controller.IsValid() )
		{
			Controller.EyeAngles = eyeAngles;
			if ( Controller.Body.IsValid() )
			{
				Controller.Body.Velocity = Vector3.Zero;
			}
		}
	}

	bool HandleArrestControl()
	{
		if ( !IsArrested )
		{
			if ( preArrestRunSpeed.HasValue && Controller.IsValid() )
			{
				Controller.RunSpeed = preArrestRunSpeed.Value;
			}

			preArrestRunSpeed = null;
			return false;
		}

		if ( Controller.IsValid() )
		{
			preArrestRunSpeed ??= Controller.RunSpeed;
			Controller.RunSpeed = preArrestRunSpeed.Value * ArrestRunSpeedMultiplier;
			if ( Controller.Body.IsValid() )
			{
				Controller.Body.Velocity = Controller.Body.Velocity.ClampLength( Controller.RunSpeed );
			}
		}

		Controller.UseInputControls = true;
		GetComponent<PlayerInventory>()?.SwitchWeapon( null, true );
		return false;
	}
}
