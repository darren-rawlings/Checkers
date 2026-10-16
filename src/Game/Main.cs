using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Checkers.Core;
using Godot;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Core;
using Renegade.Dev;
using Renegade.Looks;
using Renegade.Play;
using Renegade.Ui;
using static Checkers.Game.BoardLayout;

namespace Checkers.Game;

/// <summary>
/// Checkers. Tap one of your pieces, then a glowing square to move there (a whole jump chain in one
/// tap). Every piece you jump is knocked clean off the board and clatters onto the table, where the
/// captured pieces pile up (the signature effect); a crowned piece gets a second disc dropped on top
/// with a burst of sparks. Drag sideways to spin the board.
/// </summary>
public partial class Main : Node3D
{
	public const string Title = "Checkers";

	/// <summary>Google's test ad units until the real ones exist in AdMob (see the release playbook).</summary>
	private static readonly AdMobConfig AdUnits = AdMobConfig.Test;

	private const Colour Human = Colour.Dark;

	private readonly CheckersGame _game = new();
	private readonly Scoreboard _wins = new();
	private readonly Checker?[] _pieces = new Checker?[64];
	private readonly MeshInstance3D[] _hints = new MeshInstance3D[64];
	private readonly MeshInstance3D[] _movable = new MeshInstance3D[64];
	private readonly List<RigidBody3D> _knockedOut = new();
	private readonly ImpactBank<Style> _impacts = new(Styles.Impact);
	private readonly ComputerTurn _computer = new();

	private GameSettings _settings = null!;
	private Node3D _rig = null!;
	private Camera3D _camera = null!;
	private SoundPool _sound = null!;
	private GameHud _hud = null!;
	private IAdService _ads = null!;
	private AdSchedule? _adSchedule;
	private SpinControl _spin = null!;
	private MeshInstance3D _slab = null!;
	private MeshInstance3D _selectRing = null!;
	private int _selected = -1;
	private bool _busy;
	private bool _roundCounted;

	private bool _demo;
	private bool _quitAfterDemo;
	private float _initialSpin;

	private bool VsComputer => _settings.Mode == GameMode.PlayerVsComputer || _demo;

	private bool IsComputerTurn => !_game.IsOver && (_demo || (VsComputer && _game.Turn != Human));

	private PieceSet PieceColours => Styles.Pieces(_settings.Pieces);

	public override void _Ready()
	{
		DevLog.Tag = "chk";
		AudioWatch.Attach(this, Title);
		_settings = GameSettings.Load(board: Style.Wood, pieces: Style.Plastic);
		ReadDeveloperArgs();
		AudioServer.SetBusMute(0, _settings.Muted);
		BuildWorld();

		_sound = new SoundPool();
		AddChild(_sound);
		_adSchedule ??= new AdSchedule(_settings.GamesSinceAd);
		var theme = HudTheme.Warm;
		_ads = AdServices.Create(this, AdUnits, _demo, new PlaceholderAds
		{
			TextColor = theme.Text,
			RewardTitle = "Watch to undo your move",
			ClaimText = "Claim undo",
			SkipText = "Skip (no undo)",
		});

		_hud = new GameHud(theme);
		AddChild(_hud);
		_hud.Build(new HudOptions(Title, "Tap a piece, then where to go  ·  Drag to spin",
			PieceName: s => $"{Styles.Pieces(s).NameOne} & {Styles.Pieces(s).NameTwo}"),
			_settings.Muted, _settings.Mode, _settings.Difficulty, _settings.Board, _settings.Pieces);
		_hud.NewGamePressed += OnNewGamePressed;
		_hud.UndoPressed += OnUndoPressed;
		_hud.MuteToggled += muted =>
		{
			_settings.Muted = muted;
			AudioServer.SetBusMute(0, muted);
			_settings.Save();
		};
		_hud.ModeChosen += (mode, difficulty) =>
		{
			_settings.Mode = mode;
			_settings.Difficulty = difficulty;
			_settings.Save();
			_wins.Reset();
			_ = StartGame();
		};
		_hud.BoardChosen += style =>
		{
			_settings.Board = style;
			_settings.Save();
			_slab.MaterialOverride = Materials.For(Styles.BoardLook(style));
		};
		_hud.PiecesChosen += style =>
		{
			_settings.Pieces = style;
			_settings.Save();
			foreach (var piece in _pieces)
				piece?.ApplyStyle(PieceColours);
		};

		_spin = new SpinControl(_rig, () => !_busy);
		_spin.Tapped += OnTapped;
		_ = StartGame();
	}

	// ---------------------------------------------------------------- World

	private void BuildWorld()
	{
		_camera = new Camera3D { Position = new Vector3(0, 10.6f, 6.6f), Fov = 52, KeepAspect = Camera3D.KeepAspectEnum.Width };
		AddChild(_camera);
		_camera.LookAt(new Vector3(0, -0.4f, 0.3f));
		Stage.Build(this, -Thickness, new Color("1a0f09"));

		_rig = new Node3D();
		AddChild(_rig);
		_rig.RotateY(_initialSpin);

		var body = new StaticBody3D();
		_slab = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(Half * 2, Thickness, Half * 2) },
			Position = new Vector3(0, -Thickness / 2, 0),
			MaterialOverride = Materials.For(Styles.BoardLook(_settings.Board)),
		};
		body.AddChild(_slab);
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Half * 2, Thickness, Half * 2) }, Position = new Vector3(0, -Thickness / 2, 0) });
		_rig.AddChild(body);

		// The dark squares: a translucent shade over the board material, so every style reads as a checkerboard.
		var shade = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.02f, 0f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		var tile = new PlaneMesh { Size = new Vector2(Cell, Cell) };
		for (int square = 0; square < 64; square++)
		{
			if (CheckersGame.IsDark(square))
				_rig.AddChild(new MeshInstance3D { Mesh = tile, MaterialOverride = shade, Position = SquareCenter(square) + new Vector3(0, 0.002f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		}

		var glow = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.78f, 0.35f, 0.6f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
		var dot = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = 0.01f, RadialSegments = 24 };
		// Rings round the pieces that can move (a dot would hide under the piece).
		var moveRing = new TorusMesh { InnerRadius = PieceRadius + 0.01f, OuterRadius = PieceRadius + 0.06f, Rings = 40, RingSegments = 8 };
		for (int square = 0; square < 64; square++)
		{
			_hints[square] = new MeshInstance3D { Mesh = dot, MaterialOverride = glow, Position = SquareCenter(square) + new Vector3(0, 0.006f, 0), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			_rig.AddChild(_hints[square]);
			_movable[square] = new MeshInstance3D { Mesh = moveRing, MaterialOverride = glow, Position = SquareCenter(square) + new Vector3(0, 0.01f, 0), Scale = new Vector3(1, 0.3f, 1), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			_rig.AddChild(_movable[square]);
		}
		_selectRing = new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = PieceRadius + 0.02f, OuterRadius = PieceRadius + 0.08f, Rings = 40, RingSegments = 8 },
			MaterialOverride = glow,
			Scale = new Vector3(1, 0.3f, 1),
			Visible = false,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_rig.AddChild(_selectRing);
		Stage.WarmUp(this, -Thickness, Sparkle.Halo);
	}

	private Checker AddChecker(int square, Piece piece)
	{
		var checker = new Checker();
		checker.Setup(piece.Colour == Colour.Dark, PieceColours, piece.King);
		checker.Position = SquareCenter(square);
		checker.RotateY((float)GD.RandRange(0, Mathf.Tau));
		_rig.AddChild(checker);
		_pieces[square] = checker;
		return checker;
	}

	// ---------------------------------------------------------------- Input

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_hud.MenuOpen)
			_spin.HandleInput(@event);
	}

	private void OnTapped(Vector2 screen)
	{
		if (_busy || IsComputerTurn || _game.IsOver)
			return;
		var from = _camera.ProjectRayOrigin(screen);
		var direction = _camera.ProjectRayNormal(screen);
		if (direction.Y > -0.01f)
			return;
		var world = from + direction * ((PieceHeight - from.Y) / direction.Y);
		int square = SquareAt(_rig.ToLocal(world));
		if (square < 0)
		{
			Select(-1);
			return;
		}
		var moves = _game.LegalMoves();
		if (_selected >= 0 && moves.FirstOrDefault(m => m.From == _selected && m.To == square) is { } chosen)
		{
			_ = PlayMove(chosen);
			return;
		}
		Select(moves.Any(m => m.From == square) ? square : -1);
	}

	private void Select(int square)
	{
		_selected = square;
		RefreshHints();
	}

	// ---------------------------------------------------------------- Turns

	private async Task PlayMove(Move move)
	{
		_busy = true;
		_spin.Stop();
		_hud.HideHint();
		Select(-1);
		var result = _game.Play(move);
		DevLog.Write($"{result.Mover} {move.From}->{move.To}, captured {move.Captured.Count}, crowned={result.Crowned}, over={result.GameOver}");
		RefreshUi(result.Mover);

		var checker = _pieces[move.From]!;
		_pieces[move.From] = null;
		var at = SquareCenter(move.From);
		for (int step = 0; step < move.Path.Count; step++)
		{
			var to = SquareCenter(move.Path[step]);
			// A hop: up and over, a little higher for a jump.
			float height = move.IsJump ? 0.9f : 0.45f;
			var hop = CreateTween();
			var start = at;
			hop.TweenMethod(Callable.From<float>(t =>
			{
				checker.Position = start.Lerp(to, t) + new Vector3(0, height * 4 * t * (1 - t), 0);
			}), 0f, 1f, move.IsJump ? 0.34f : 0.26f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			if (move.IsJump)
			{
				int victim = move.Captured[step];
				GetTree().CreateTimer(0.17).Timeout += () => KnockOff(victim, (SquareCenter(victim) - start).Normalized());
			}
			await ToSignal(hop, Tween.SignalName.Finished);
			at = to;
			PlayClack(checker.GlobalPosition, 0.8f);
		}
		_pieces[move.To] = checker;

		if (result.Crowned)
			await Crown(checker);
		if (_game.IsOver && !_roundCounted)
		{
			_roundCounted = true;
			if (_game.Winner is { } winner)
				_wins.Win(first: winner == Colour.Dark);
		}
		_busy = false;
		RefreshUi();
	}

	/// <summary>
	/// The signature: a jumped piece is knocked clean off the board. It becomes a real physics body,
	/// flung outward and spinning, and clatters onto the table, where captured pieces pile up.
	/// </summary>
	private void KnockOff(int square, Vector3 hopDirection)
	{
		var victim = _pieces[square];
		if (victim == null)
			return;
		_pieces[square] = null;
		var transform = victim.GlobalTransform;
		_rig.RemoveChild(victim);

		var body = new RigidBody3D { Mass = 0.05f, ContinuousCd = true, ContactMonitor = true, MaxContactsReported = 2, GravityScale = 2.5f };
		body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = PieceRadius, Height = PieceHeight * (victim.King ? 2 : 1) }, Position = new Vector3(0, PieceHeight * (victim.King ? 1 : 0.5f), 0) });
		body.PhysicsMaterialOverride = new PhysicsMaterial { Bounce = Styles.Bounce(_settings.Pieces), Friction = 0.6f };
		AddChild(body);
		body.GlobalTransform = transform;
		victim.Transform = Transform3D.Identity;
		body.AddChild(victim);

		// Away from the capturing side, off the nearest edge, with a spin.
		var outward = _rig.GlobalBasis * hopDirection;
		outward.Y = 0;
		outward = outward.Normalized();
		// Hard enough to clear the edge from anywhere on the board (about 0.5 s in the air).
		body.LinearVelocity = outward * (float)GD.RandRange(10.0, 12.0) + new Vector3(0, 6.5f, 0);
		body.AngularVelocity = new Vector3((float)GD.RandRange(-14, 14), (float)GD.RandRange(-6, 6), (float)GD.RandRange(-14, 14));
		int hits = 0;
		body.BodyEntered += _ =>
		{
			if (hits++ < 3)
				PlayClack(body.GlobalPosition, hits == 1 ? 0.9f : 0.4f, onTable: true);
		};
		foreach (var mesh in victim.Meshes)
			mesh.MaterialOverlay = Sparkle.Halo;
		GetTree().CreateTimer(0.25).Timeout += () =>
		{
			foreach (var mesh in victim.Meshes)
				mesh.MaterialOverlay = null;
		};
		Sparkle.Burst(this, transform.Origin + new Vector3(0, 0.2f, 0));
		_knockedOut.Add(body);
		PlayClack(transform.Origin, 1f);
	}

	/// <summary>A second disc drops onto the piece from above, with sparks: crowned.</summary>
	private async Task Crown(Checker checker)
	{
		var crown = checker.AddCrown();
		var rest = crown.Position;
		crown.Position = rest + new Vector3(0, 1.6f, 0);
		var drop = CreateTween();
		drop.TweenProperty(crown, "position", rest, 0.3f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		await ToSignal(drop, Tween.SignalName.Finished);
		PlayClack(checker.GlobalPosition, 1f);
		Sparkle.Burst(_rig, checker.Position + new Vector3(0, 0.4f, 0));
		_hud.SetStatus("Crowned!", PieceColours.UiColorOf(checker.IsDark));
		await Wait(0.5f);
	}

	private void PlayClack(Vector3 at, float strength, bool onTable = false)
	{
		var pieces = _settings.Pieces;
		_sound.PlayImpact(_impacts.Random(pieces), at, strength, Styles.GainDb(pieces) + 2f);
		var surface = onTable ? Style.Table : _settings.Board;
		_sound.PlayImpact(_impacts.Random(surface), at, strength * 0.6f, Styles.GainDb(surface));
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_spin.Process(dt);
		if (_busy || !IsComputerTurn)
			return;
		if (!_computer.Thinking)
		{
			var player = new ComputerPlayer(_demo ? Difficulty.Medium : _settings.Difficulty);
			var snapshot = _game.Clone();
			var chosen = default(Move);
			_computer.MinThinkTime = _demo ? 0.2f : 0.7f;
			_pendingMove = null;
			_computer.Start(() =>
			{
				chosen = player.ChooseMove(snapshot);
				_pendingMove = chosen;
				return chosen.From;
			}, $"computer ({player.Difficulty})");
			RefreshUi();
			return;
		}
		if (_computer.Poll(dt) is not null && _pendingMove is { } move)
			_ = PlayMove(move);
	}

	private Move? _pendingMove;

	// ---------------------------------------------------------------- Games, undo, ads

	private void OnNewGamePressed()
	{
		if (_busy)
			return;
		bool adDue = _adSchedule!.GameFinished();
		_settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		_settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			_ads.ShowInterstitial(() => _ = StartGame());
		}
		else
		{
			_ = StartGame();
		}
	}

	private async Task StartGame()
	{
		_computer.Cancel();
		_busy = true;
		_roundCounted = false;
		_game.Reset();
		await RebuildPieces();
		_busy = false;
		RefreshUi();
	}

	/// <summary>Clears the board and the captured pile, then sets every piece where the rules have it.</summary>
	private async Task RebuildPieces()
	{
		foreach (var body in _knockedOut)
		{
			var shrink = CreateTween();
			shrink.TweenProperty(body, "scale", Vector3.One * 0.01f, 0.25f);
			shrink.TweenCallback(Callable.From(body.QueueFree));
		}
		_knockedOut.Clear();
		for (int square = 0; square < 64; square++)
		{
			_pieces[square]?.QueueFree();
			_pieces[square] = null;
			if (_game[square] is { } piece)
			{
				var checker = AddChecker(square, piece);
				checker.Scale = Vector3.One * 0.01f;
				CreateTween().TweenProperty(checker, "scale", Vector3.One, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
			}
		}
		await Wait(0.3f);
	}

	private bool CanUndo => VsComputer && !_demo && !_busy && _game.MovesMade > 0 && (_game.IsOver || _game.Turn == Human);

	private void OnUndoPressed()
	{
		if (!CanUndo)
			return;
		DevLog.Write("ad: rewarded (undo)");
		_hud.SetUndoVisible(false);
		_ads.ShowRewarded(rewarded: () => _ = UndoLastTurn(), closed: () => RefreshUi());
	}

	private async Task UndoLastTurn()
	{
		_computer.Cancel();
		if (_game.IsOver && _roundCounted && _game.Winner is { } winner)
			_wins.Unwin(first: winner == Colour.Dark);
		_roundCounted = false;
		while (_game.LastMover == CheckersGame.Opponent(Human))
			_game.Undo();
		if (_game.LastMover == Human)
			_game.Undo();
		_busy = true;
		RefreshUi();
		await RebuildPieces();
		_busy = false;
		RefreshUi();
	}

	// ---------------------------------------------------------------- UI

	private string NameOf(Colour colour) => VsComputer && !_demo
		? (colour == Human ? "You" : "Computer")
		: PieceColours.NameOf(colour == Colour.Dark);

	private Color ColorOf(Colour colour) => PieceColours.UiColorOf(colour == Colour.Dark);

	/// <summary>
	/// Glowing squares: where the selected piece can go; with nothing selected, the pieces that
	/// can move (only those that can jump, when a jump is compulsory).
	/// </summary>
	private void RefreshHints()
	{
		foreach (var hint in _hints)
			hint.Visible = false;
		foreach (var ring in _movable)
			ring.Visible = false;
		_selectRing.Visible = false;
		if (_busy || IsComputerTurn || _game.IsOver)
			return;
		var moves = _game.LegalMoves();
		if (_selected >= 0)
		{
			foreach (var move in moves.Where(m => m.From == _selected))
				_hints[move.To].Visible = true;
			_selectRing.Position = SquareCenter(_selected) + new Vector3(0, 0.01f, 0);
			_selectRing.Visible = true;
		}
		else
		{
			foreach (int from in moves.Select(m => m.From).Distinct())
				_movable[from].Visible = true;
		}
	}

	private void RefreshUi(Colour? mover = null)
	{
		_hud.SetScore($"{NameOf(Colour.Dark)}  {_game.Count(Colour.Dark)}   —   {_game.Count(Colour.Light)}  {NameOf(Colour.Light)}");
		_hud.SetNewGameVisible(_game.IsOver && !_busy);
		_hud.SetUndoVisible(CanUndo);
		RefreshHints();

		if (_game.IsOver)
		{
			if (_busy)
				return;
			string wins = $"(wins {_wins.First}–{_wins.Second})";
			string text = _game.Winner switch
			{
				null => $"A draw: 50 moves without progress {wins}",
				{ } w when NameOf(w) == "You" => $"You win! {wins}",
				{ } w => $"{NameOf(w)} wins {wins}",
			};
			_hud.SetStatus(text, _game.Winner is { } side ? ColorOf(side) : Colors.White);
			if (_demo && _quitAfterDemo)
				GetTree().CreateTimer(1.5).Timeout += () => GetTree().Quit();
			return;
		}

		var turn = mover ?? _game.Turn;
		bool mustJump = mover == null && _game.LegalMoves().FirstOrDefault()?.IsJump == true;
		string status = mover == null && IsComputerTurn && !_demo ? "Computer is thinking…"
			: NameOf(turn) == "You" ? (mustJump ? "Your turn: you must jump" : "Your turn")
			: $"{NameOf(turn)}'s turn";
		_hud.SetStatus(status, ColorOf(turn));
	}

	private SignalAwaiter Wait(float seconds) => ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	// ---------------------------------------------------------------- Developer flags

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo` (the
	/// computer plays both sides), `--quit-after-demo`, `--games-since-ad=4`, `--board=Wood`,
	/// `--pieces=Plastic`, `--spin=30` (degrees). Style flags aren't saved.
	/// </summary>
	private void ReadDeveloperArgs()
	{
		foreach (var (name, value) in DevArgs.Read())
		{
			switch (name)
			{
				case "--log":
					DevLog.Enabled = true;
					break;
				case "--demo":
					_demo = true;
					_settings.Muted = false;
					break;
				case "--quit-after-demo":
					_quitAfterDemo = true;
					break;
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--board" when System.Enum.TryParse(value, out Style board):
					_settings.Board = board;
					break;
				case "--pieces" when System.Enum.TryParse(value, out Style pieces):
					_settings.Pieces = pieces;
					break;
				case "--spin":
					_initialSpin = Mathf.DegToRad(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
					break;
			}
		}
	}
}
