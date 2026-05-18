using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

/// <summary>
/// Gère le cycle complet des élections du maire :
///   1. PHASE CANDIDATURES (3 min) : /candidate &lt;motivation&gt; → POST darkapi
///   2. PHASE VOTE         (2 min) : /vote &lt;n°&gt; → POST darkapi
///   3. APPLY              : POST /elections/{id}/close → SetJob(maire) sur le winner
///   4. MORT DU MAIRE      : DELETE whitelist + relance cycle
///
/// Singleton scene (pattern Ensure comme JobVoteManager). Sync via Network properties.
/// </summary>
public sealed class MayorElectionManager : Component, Global.IPlayerEvents
{
	// Phases (durées par défaut, modifiables via les overloads StartElection)
	public const float DefaultCandidacySeconds = 180f; // 3 min
	public const float DefaultVotingSeconds    = 120f; // 2 min
	public const float CooldownAfterMayorDeath = 10f;  // délai avant relance auto

	public enum Phase
	{
		Idle,
		Candidacy,
		Voting,
		Closing, // transitionnel, le temps d'appeler /close
	}

	[Property, Sync( SyncFlags.FromHost )]
	public Phase CurrentPhase { get; private set; } = Phase.Idle;

	[Property, Sync( SyncFlags.FromHost )]
	public int CurrentElectionId { get; private set; }

	[Property, Sync( SyncFlags.FromHost )]
	public float PhaseEndTime { get; private set; }

	[Property, Sync( SyncFlags.FromHost )]
	public int CandidateCount { get; private set; }

	/// <summary>SteamID du maire actuel (0 si aucun).</summary>
	[Property, Sync( SyncFlags.FromHost )]
	public long CurrentMayorSteamId { get; private set; }

	// Tracking local (host-only) — pas synced car volumineux
	readonly Dictionary<long, int> _candidateNumberBySteamId = new();
	readonly HashSet<long> _votersAlready = new();
	float _cooldownStartedAt;

	public static MayorElectionManager Current => Game.ActiveScene?.Get<MayorElectionManager>();
	public float SecondsRemaining => CurrentPhase == Phase.Idle ? 0 : MathF.Max( 0, PhaseEndTime - Time.Now );

	public static MayorElectionManager Ensure( Scene scene )
	{
		if ( scene is null ) return null;
		var existing = scene.Get<MayorElectionManager>();
		if ( existing.IsValid() ) return existing;

		var go = new GameObject( true, "Mayor Election Manager" );
		var mgr = go.AddComponent<MayorElectionManager>();
		go.NetworkSpawn( null );
		go.Network.SetOwnerTransfer( OwnerTransfer.Fixed );
		return mgr;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		// Auto-relance après mort du maire (cooldown)
		if ( CurrentPhase == Phase.Idle && _cooldownStartedAt > 0 && Time.Now - _cooldownStartedAt >= CooldownAfterMayorDeath )
		{
			_cooldownStartedAt = 0;
			_ = StartElectionAsync();
			return;
		}

		// Transition fin de phase
		if ( CurrentPhase == Phase.Candidacy && SecondsRemaining <= 0 )
		{
			BeginVotingPhase();
		}
		else if ( CurrentPhase == Phase.Voting && SecondsRemaining <= 0 )
		{
			_ = CloseElectionAsync();
		}
	}

	// ═══════════════════════════════════════════════════════════════ PHASE 1 : Candidatures
	public async Task<bool> StartElectionAsync(
		float candidacySeconds = DefaultCandidacySeconds,
		float votingSeconds    = DefaultVotingSeconds )
	{
		if ( !Networking.IsHost ) return false;
		if ( CurrentPhase != Phase.Idle )
		{
			Log.Warning( "[MayorElection] Une élection est déjà en cours." );
			return false;
		}

		var resp = await DarkHttpClient.PostJsonAsync<StartElectionResponse>(
			"elections/start",
			new
			{
				target_job_id           = (int?) null, // Le panel résout via target_job_code = "mayor"
				target_job_code         = "mayor",
				candidacy_duration_secs = (int) candidacySeconds,
				voting_duration_secs    = (int) votingSeconds,
			}
		);

		if ( resp is null || resp.Id <= 0 )
		{
			Log.Warning( "[MayorElection] /elections/start a échoué." );
			BroadcastChat( "❌ Impossible de lancer l'élection (erreur serveur)." );
			return false;
		}

		CurrentElectionId        = resp.Id;
		CurrentPhase             = Phase.Candidacy;
		PhaseEndTime             = Time.Now + candidacySeconds;
		CandidateCount           = 0;
		_candidateNumberBySteamId.Clear();
		_votersAlready.Clear();

		BroadcastChat( $"🗳️ Élections du maire ouvertes ! Tape /candidate <programme> pour te présenter ({(int) candidacySeconds / 60}min)." );
		return true;
	}

	/// <summary>Appelée par la commande chat /candidate. Server-side.</summary>
	public async Task<(bool ok, string message)> RegisterCandidateAsync( Player player, string motivation )
	{
		if ( !Networking.IsHost ) return (false, "Host-only.");
		if ( CurrentPhase != Phase.Candidacy ) return (false, "Pas de phase de candidatures en cours.");
		if ( player?.Network.Owner is null ) return (false, "Joueur invalide.");

		var steamId = player.Network.Owner.SteamId.Value;

		if ( _candidateNumberBySteamId.ContainsKey( (long) steamId ) )
			return (false, "Tu es déjà candidat.");

		var resp = await DarkHttpClient.PostJsonAsync<CandidateResponse>(
			"elections/candidates",
			new
			{
				election_id = CurrentElectionId,
				steam_id    = steamId,
				display_name = player.DisplayName,
				motivation  = motivation ?? "",
			}
		);

		if ( resp is null || resp.Id <= 0 )
			return (false, "Erreur lors de l'enregistrement.");

		CandidateCount++;
		_candidateNumberBySteamId[(long) steamId] = CandidateCount; // n° d'ordre = position

		BroadcastChat( $"📋 Candidat #{CandidateCount} : {player.DisplayName} — {motivation}" );
		return (true, $"Tu es candidat #{CandidateCount}. Bonne chance !");
	}

	// ═══════════════════════════════════════════════════════════════ PHASE 2 : Vote
	void BeginVotingPhase()
	{
		if ( !Networking.IsHost ) return;
		if ( CandidateCount == 0 )
		{
			BroadcastChat( "🙅 Aucun candidat, élection annulée." );
			Reset();
			return;
		}

		CurrentPhase = Phase.Voting;
		PhaseEndTime = Time.Now + DefaultVotingSeconds;
		_votersAlready.Clear();

		BroadcastChat( $"🗳️ Vote ouvert ! Tape /vote <numéro> pour voter parmi {CandidateCount} candidats ({DefaultVotingSeconds / 60}min)." );

		// Liste les candidats avec leurs numéros
		foreach ( var (sid, num) in _candidateNumberBySteamId.OrderBy( kv => kv.Value ) )
		{
			var cand = FindPlayer( sid );
			var name = cand?.DisplayName ?? $"SteamID:{sid}";
			BroadcastChat( $"   {num}. {name}" );
		}
	}

	/// <summary>Appelée par la commande chat /vote. Server-side.</summary>
	public async Task<(bool ok, string message)> RegisterVoteAsync( Player voter, int candidateNumber )
	{
		if ( !Networking.IsHost ) return (false, "Host-only.");
		if ( CurrentPhase != Phase.Voting ) return (false, "Pas de vote en cours.");
		if ( voter?.Network.Owner is null ) return (false, "Joueur invalide.");

		var voterSid = (long) voter.Network.Owner.SteamId.Value;

		if ( _votersAlready.Contains( voterSid ) )
			return (false, "Tu as déjà voté.");

		// Résout numéro → SteamID candidat
		var match = _candidateNumberBySteamId.FirstOrDefault( kv => kv.Value == candidateNumber );
		if ( match.Key == 0 )
			return (false, $"Aucun candidat n°{candidateNumber}.");

		var resp = await DarkHttpClient.PostJsonAsync<VoteResponse>(
			"elections/votes",
			new
			{
				election_id        = CurrentElectionId,
				voter_steam_id     = voterSid,
				candidate_steam_id = match.Key,
			}
		);

		if ( resp is null || !resp.Ok )
			return (false, "Erreur lors du vote.");

		_votersAlready.Add( voterSid );
		return (true, $"Vote enregistré pour le candidat n°{candidateNumber}.");
	}

	// ═══════════════════════════════════════════════════════════════ PHASE 3 : Close + Apply
	async Task CloseElectionAsync()
	{
		if ( !Networking.IsHost ) return;
		if ( CurrentPhase != Phase.Voting ) return;

		CurrentPhase = Phase.Closing;
		BroadcastChat( "⏳ Dépouillement en cours..." );

		var resp = await DarkHttpClient.PostJsonAsync<CloseElectionResponse>(
			$"elections/{CurrentElectionId}/close",
			new { }
		);

		if ( resp is null )
		{
			Log.Warning( "[MayorElection] /elections/close a échoué." );
			BroadcastChat( "❌ Échec du dépouillement, élection annulée." );
			Reset();
			return;
		}

		if ( resp.WinnerSteamId == 0 )
		{
			BroadcastChat( $"🤷 Aucun vote pour le maire. Élection annulée." );
			Reset();
			return;
		}

		var winner = FindPlayer( resp.WinnerSteamId );
		BroadcastChat( $"🎉 Maire élu : {resp.WinnerName ?? winner?.DisplayName ?? "Inconnu"} ({resp.WinnerVoteCount} votes)" );

		if ( winner is null )
		{
			Log.Warning( $"[MayorElection] Winner {resp.WinnerSteamId} non connecté, impossible de SetJob." );
			Reset();
			return;
		}

		// Apply SetJob(maire)
		var mayorDef = JobDefinition.Get( Player.MayorJobDefinitionPath );
		if ( mayorDef is null )
		{
			Log.Warning( "[MayorElection] JobDefinition mayor introuvable." );
			Reset();
			return;
		}

		winner.SetJobDefinition( mayorDef );
		CurrentMayorSteamId = resp.WinnerSteamId;

		Notices.SendNotice( winner.Network.Owner, "campaign", Color.Yellow,
			"Tu as été élu Maire ! Représente la ville dignement.", 8 );

		Reset( keepMayor: true );
	}

	// ═══════════════════════════════════════════════════════════════ Mort du maire
	void Global.IPlayerEvents.OnPlayerDied( Player player, PlayerDiedParams args )
	{
		if ( !Networking.IsHost ) return;
		if ( player?.Network.Owner is null ) return;

		var sid = (long) player.Network.Owner.SteamId.Value;
		if ( CurrentMayorSteamId == 0 || sid != CurrentMayorSteamId ) return;

		// C'est le maire qui meurt. Révoque sa whitelist mayor + déclenche cooldown auto-relance.
		_ = OnMayorDiedAsync( player );
	}

	async Task OnMayorDiedAsync( Player exMayor )
	{
		var sid = CurrentMayorSteamId;
		BroadcastChat( $"⚰️ Le maire {exMayor.DisplayName} est mort. Nouvelles élections dans {(int) CooldownAfterMayorDeath}s." );

		// Récupère le job_id du maire pour le DELETE whitelist
		var mayorJobId = await DarkHttpClient.GetAsync<JobIdResponse>( $"jobs?code=mayor" );
		if ( mayorJobId is not null && mayorJobId.Id > 0 )
		{
			_ = DarkHttpClient.DeleteAsync( $"players/{sid}/whitelisted_jobs/{mayorJobId.Id}" );
		}

		// Réassigne le job par défaut au ex-maire
		var defaultDef = JobDefinition.Get( JobDefinition.DefaultResourcePath ) ?? JobDefinition.GetDefault();
		if ( defaultDef is not null ) exMayor.SetJobDefinition( defaultDef );

		CurrentMayorSteamId = 0;
		_cooldownStartedAt  = Time.Now;
	}

	// ═══════════════════════════════════════════════════════════════ Helpers
	void Reset( bool keepMayor = false )
	{
		CurrentPhase             = Phase.Idle;
		CurrentElectionId        = 0;
		PhaseEndTime             = 0;
		CandidateCount           = 0;
		_candidateNumberBySteamId.Clear();
		_votersAlready.Clear();
		if ( !keepMayor ) CurrentMayorSteamId = 0;
	}

	static Player FindPlayer( long steamId )
	{
		return Game.ActiveScene?
			.GetAllComponents<Player>()
			.FirstOrDefault( p => p.Network.Owner?.SteamId.Value == (ulong) steamId );
	}

	static void BroadcastChat( string message )
	{
		var chat = Game.ActiveScene?.Get<Chat>();
		chat?.AddSystemText( message, "campaign" );
	}

	// ═══════════════════════════════════════════════════════════════ DTOs
	sealed class StartElectionResponse
	{
		[JsonPropertyName( "id" )] public int Id { get; set; }
	}
	sealed class CandidateResponse
	{
		[JsonPropertyName( "id" )] public int Id { get; set; }
	}
	sealed class VoteResponse
	{
		[JsonPropertyName( "ok" )] public bool Ok { get; set; }
	}
	sealed class CloseElectionResponse
	{
		[JsonPropertyName( "winner_steam_id" )]  public long WinnerSteamId { get; set; }
		[JsonPropertyName( "winner_name" )]      public string WinnerName { get; set; }
		[JsonPropertyName( "winner_vote_count" )] public int WinnerVoteCount { get; set; }
	}
	sealed class JobIdResponse
	{
		[JsonPropertyName( "id" )] public int Id { get; set; }
	}
}
