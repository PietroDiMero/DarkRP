using Sandbox.UI;

/// <summary>
/// Distribue le salaire de chaque joueur selon son job actuel,
/// toutes les <see cref="SalaryIntervalSeconds"/> secondes.
/// Fonctionne uniquement côté host.
/// </summary>
public sealed class SalarySystem : GameObjectSystem<SalarySystem>
{
	/// <summary>Délai en secondes entre chaque versement de salaire.</summary>
	public const float SalaryIntervalSeconds = 45f;

	TimeUntil _nextSalary;

	public SalarySystem( Scene scene ) : base( scene )
	{
		_nextSalary = SalaryIntervalSeconds;
		Listen( Stage.StartUpdate, 0, OnUpdate, "SalarySystem" );
	}

	void OnUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( !_nextSalary ) return;

		_nextSalary = SalaryIntervalSeconds;
		PaySalaries();
	}

	void PaySalaries()
	{
		foreach ( var player in Scene.GetAll<Player>() )
		{
			if ( !player.IsValid() ) continue;
			if ( player.Network.Owner is not { } owner ) continue;

			var job = player.CurrentJobDefinition;
			if ( job is null || job.Salary <= 0 ) continue;

			player.GiveMoney( job.Salary );

			Notices.SendNotice( owner, "payments", Color.Green,
				$"+${job.Salary} — Salaire ({job.Title})", 3f );
		}
	}
}
