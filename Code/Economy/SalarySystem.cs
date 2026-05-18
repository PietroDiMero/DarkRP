using System.Threading.Tasks;
using Sandbox;
using Sandbox.UI;

/// <summary>
/// Distribue le salaire de chaque joueur selon son job actuel,
/// toutes les <see cref="SalaryIntervalSeconds"/> secondes.
/// Si le joueur est dans un gang ET que son job est "Criminal",
/// une fraction (<c>gang.tax_pct_criminal</c>) part dans la caisse du gang
/// avant que le net ne soit versé au joueur.
/// Fonctionne uniquement côté host.
/// </summary>
public sealed class SalarySystem : GameObjectSystem<SalarySystem>
{
	/// <summary>Délai en secondes entre chaque versement de salaire.</summary>
	public const float SalaryIntervalSeconds = 45f;

	const string CriminalCategory = "Criminal";

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

			var isCriminal = string.Equals( job.Category?.Trim(), CriminalCategory, System.StringComparison.OrdinalIgnoreCase );
			if ( !isCriminal )
			{
				// Job non criminel : versement direct comme avant
				player.GiveMoney( job.Salary );
				Notices.SendNotice( owner, "payments", Color.Green,
					$"+${job.Salary} — Salaire ({job.Title})", 3f );
				continue;
			}

			// Job criminel : check gang + tax_pct en async (fire-and-forget)
			_ = PayCriminalWithTaxAsync( player, owner, job );
		}
	}

	static async Task PayCriminalWithTaxAsync( Player player, Connection owner, JobDefinition job )
	{
		if ( !player.IsValid() || !Networking.IsHost ) return;

		var sid = (long)owner.SteamId.Value;
		var gang = await GangApi.GetByMemberAsync( sid );

		// Pas dans un gang, ou taxe 0% : versement complet
		if ( gang is null || gang.TaxPctCriminal <= 0 )
		{
			if ( !player.IsValid() ) return;
			player.GiveMoney( job.Salary );
			Notices.SendNotice( owner, "payments", Color.Green,
				$"+${job.Salary} — Salaire ({job.Title})", 3f );
			return;
		}

		// Split : taxe → caisse du gang, net → joueur
		var tax = job.Salary * gang.TaxPctCriminal / 100;
		var net = job.Salary - tax;

		if ( !player.IsValid() ) return;
		if ( net > 0 ) player.GiveMoney( net );

		if ( tax > 0 )
		{
			_ = GangApi.RecordTreasuryAsync( gang.Id, "tax", tax, sid,
				$"Taxe {gang.TaxPctCriminal}% — {job.Title}" );
		}

		Notices.SendNotice( owner, "payments", Color.Green,
			$"+${net} — Salaire ({job.Title})  ·  -${tax} → [{gang.Tag}]", 4f );
	}
}
