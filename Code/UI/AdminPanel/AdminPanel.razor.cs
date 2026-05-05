using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Panneau d'administration — ouvert avec F3.
/// Visible uniquement si le joueur a un rôle ≥ Support.
/// </summary>
public partial class AdminPanel : PanelComponent
{
	// ── Onglets ─────────────────────────────────────────────────────────
	public enum Tab { Players, Bans, Logs }
	Tab _tab = Tab.Players;

	// ── État UI ─────────────────────────────────────────────────────────
	bool     _open        = false;
	string   _search      = "";
	string   _banReason   = "";
	string   _banDuration = "0";  // 0 = permanent, sinon minutes
	string   _kickReason  = "";
	string   _moneyAmount = "0";
	long     _selected    = 0;

	// ── Données locales ─────────────────────────────────────────────────
	List<PlayerInfo>  _onlinePlayers  = new();
	List<BanRecord>   _activeBans     = new();
	List<AdminLog>    _recentLogs     = new();
	StaffRole         _myRole         = StaffRole.Player;
	int               _revision       = 0;

	// ── Mise à jour ─────────────────────────────────────────────────────
	protected override void OnUpdate()
	{
		// Touche F3 — brute ou via action bindée "AdminPanel"
		if ( Input.Pressed( "AdminPanel" ) || Input.Keyboard.Pressed( "F3" ) )
			TogglePanel();

		SetClass( "open", _open );

		// Bloquer le jeu si le panel est ouvert
		if ( Panel is not null )
			Panel.AcceptsFocus = _open;
	}

	protected override void OnStart()
	{
		// Rafraîchir toutes les 2 secondes
		GameTask.RunInThreadAsync( async () =>
		{
			while ( this.IsValid() )
			{
				await Task.Delay( 2000 );
				await GameTask.MainThread();
				if ( _open ) Refresh();
			}
		} );
	}

	void TogglePanel()
	{
		var local = Player.FindLocalPlayer();
		if ( local is null ) return;

		var myConn = Connection.Local;
		if ( myConn is null ) return;

		_myRole = DarkDatabase.Instance?.GetRole( (long)myConn.SteamId.Value ) ?? StaffRole.Player;

		if ( !_myRole.HasPanelAccess() )
		{
			Notices.AddNotice( "block", Color.Red, "Accès refusé.", 3f );
			return;
		}

		_open = !_open;
		if ( _open ) Refresh();

		StateHasChanged();
	}

	void Refresh()
	{
		_onlinePlayers = Connection.All.Select( c => new PlayerInfo
		{
			SteamId     = (long)c.SteamId.Value,
			DisplayName = c.DisplayName,
			IsHost      = c.IsHost,
			Role        = DarkDatabase.Instance?.GetRole( (long)c.SteamId.Value ) ?? StaffRole.Player,
		} ).ToList();

		_activeBans  = DarkDatabase.Instance?.GetActiveBans()?.ToList() ?? new();
		_recentLogs  = DarkDatabase.Instance?.GetRecentLogs( 50 )?.ToList() ?? new();
		_revision++;

		StateHasChanged();
	}

	// ── Actions (RPC vers le host) ───────────────────────────────────────
	void DoKick( long steamId )
	{
		var target = Connection.All.FirstOrDefault( c => (long)c.SteamId.Value == steamId );
		if ( target is null ) return;

		var local = Player.FindLocalPlayer();
		local?.RequestKickPlayer( steamId, _kickReason );
		_kickReason = "";
		StateHasChanged();
	}

	void DoBan( long steamId )
	{
		var target = Connection.All.FirstOrDefault( c => (long)c.SteamId.Value == steamId );
		if ( target is null ) return;

		var minutes = int.TryParse( _banDuration, out var m ) ? m : 0;
		AdminPanel_RpcBan( steamId, _banReason, minutes );

		_banReason = ""; _banDuration = "0";
		StateHasChanged();
	}

	void DoUnban( Guid banId )
	{
		var ban = _activeBans.FirstOrDefault( b => b.Id == banId );
		if ( ban is null || !ban.SteamId.HasValue ) return;
		AdminPanel_RpcUnban( ban.SteamId.Value );
		Refresh();
	}

	void DoSetMoney( long steamId )
	{
		if ( !int.TryParse( _moneyAmount, out var amount ) ) return;
		AdminPanel_RpcSetMoney( steamId, amount );
		_moneyAmount = "0";
	}

	void DoSetJob( long steamId, string jobPath )
	{
		AdminPanel_RpcSetJob( steamId, jobPath );
	}

	void DoSetRole( long steamId, StaffRole role )
	{
		AdminPanel_RpcSetRole( steamId, (int)role );
	}

	void DoGiveVip( long steamId, bool state )
	{
		AdminPanel_RpcSetVip( steamId, state );
		Refresh();
	}

	// ── RPCs ────────────────────────────────────────────────────────────
	[Rpc.Host]
	static void AdminPanel_RpcBan( long targetSteamId, string reason, int durationMinutes )
	{
		var caller  = Rpc.Caller;
		var callerRole = DarkDatabase.Instance?.GetRole( (long)caller.SteamId.Value ) ?? StaffRole.Player;

		bool perm = durationMinutes <= 0;
		if ( perm && !callerRole.CanAdmin() )
		{
			Notices.SendNotice( caller, "block", Color.Red, "Accès refusé.", 3 );
			return;
		}
		if ( !perm && !callerRole.CanTempBan() )
		{
			Notices.SendNotice( caller, "block", Color.Red, "Accès refusé.", 3 );
			return;
		}

		var target = Connection.All.FirstOrDefault( c => (long)c.SteamId.Value == targetSteamId );
		TimeSpan? duration = perm ? null : TimeSpan.FromMinutes( durationMinutes );

		if ( target is not null )
			DarkDatabase.Instance?.BanPlayer( target, reason, caller, duration );
		else
			DarkDatabase.Instance?.BanSteamId( targetSteamId, targetSteamId.ToString(), reason, caller, duration );
	}

	[Rpc.Host]
	static void AdminPanel_RpcUnban( long steamId )
	{
		var caller     = Rpc.Caller;
		var callerRole = DarkDatabase.Instance?.GetRole( (long)caller.SteamId.Value ) ?? StaffRole.Player;

		if ( !callerRole.CanAdmin() )
		{
			Notices.SendNotice( caller, "block", Color.Red, "Accès refusé.", 3 );
			return;
		}

		DarkDatabase.Instance?.Unban( steamId, caller );
		Notices.SendNotice( caller, "gavel", Color.Green, "Joueur débanni.", 3 );
	}

	[Rpc.Host]
	static void AdminPanel_RpcSetMoney( long targetSteamId, int amount )
	{
		var caller     = Rpc.Caller;
		var callerRole = DarkDatabase.Instance?.GetRole( (long)caller.SteamId.Value ) ?? StaffRole.Player;

		if ( !callerRole.CanAdmin() )
		{
			Notices.SendNotice( caller, "block", Color.Red, "Accès refusé.", 3 );
			return;
		}

		DarkDatabase.Instance?.SetMoney( targetSteamId, amount );
		DarkDatabase.Instance?.LogAction( caller,
			targetSteamId.ToString(), targetSteamId, "SET_MONEY", $"${amount}" );
		Notices.SendNotice( caller, "payments", Color.Green, $"Argent défini à ${amount}.", 3 );
	}

	[Rpc.Host]
	static void AdminPanel_RpcSetJob( long targetSteamId, string jobPath )
	{
		var caller     = Rpc.Caller;
		var callerRole = DarkDatabase.Instance?.GetRole( (long)caller.SteamId.Value ) ?? StaffRole.Player;

		if ( !callerRole.CanAdmin() )
		{
			Notices.SendNotice( caller, "block", Color.Red, "Accès refusé.", 3 );
			return;
		}

		var target = Connection.All.FirstOrDefault( c => (long)c.SteamId.Value == targetSteamId );
		if ( target is null ) return;

		var player = Player.FindForConnection( target );
		if ( !player.IsValid() ) return;

		var job = JobDefinition.Get( jobPath );
		if ( job is null ) return;

		player.SetJobDefinition( job );
		_ = player.ApplyCurrentJobAfterSpawnAsync();

		DarkDatabase.Instance?.LogAction( caller,
			target.DisplayName, targetSteamId, "SET_JOB", job.Title );
		Notices.SendNotice( caller, "badge", Color.Green, $"Job de {target.DisplayName} changé en {job.Title}.", 3 );
	}

	[Rpc.Host]
	static void AdminPanel_RpcSetRole( long targetSteamId, int roleInt )
	{
		var caller     = Rpc.Caller;
		var callerRole = DarkDatabase.Instance?.GetRole( (long)caller.SteamId.Value ) ?? StaffRole.Player;

		if ( !callerRole.CanAdmin() )
		{
			Notices.SendNotice( caller, "block", Color.Red, "Accès refusé.", 3 );
			return;
		}

		var role = (StaffRole)roleInt;

		// Seul un fondateur peut promouvoir en Admin ou Fondateur
		if ( role >= StaffRole.Admin && !callerRole.IsFounder() )
		{
			Notices.SendNotice( caller, "block", Color.Red, "Seul un fondateur peut promouvoir au rang Admin+.", 3 );
			return;
		}

		DarkDatabase.Instance?.SetRole( targetSteamId, role, caller );
	}

	[Rpc.Host]
	static void AdminPanel_RpcSetVip( long targetSteamId, bool isVip )
	{
		var caller     = Rpc.Caller;
		var callerRole = DarkDatabase.Instance?.GetRole( (long)caller.SteamId.Value ) ?? StaffRole.Player;

		if ( !callerRole.CanAdmin() )
		{
			Notices.SendNotice( caller, "block", Color.Red, "Accès refusé.", 3 );
			return;
		}

		DarkDatabase.Instance?.SetVip( targetSteamId, isVip );
		DarkDatabase.Instance?.LogAction( caller,
			targetSteamId.ToString(), targetSteamId, "SET_VIP", isVip ? "ON" : "OFF" );
		Notices.SendNotice( caller, "star", Color.Yellow, $"VIP {(isVip ? "activé" : "désactivé")}.", 3 );
	}

	// ── DTO local ───────────────────────────────────────────────────────
	public class PlayerInfo
	{
		public long       SteamId     { get; set; }
		public string     DisplayName { get; set; }
		public bool       IsHost      { get; set; }
		public StaffRole  Role        { get; set; }
	}
}
