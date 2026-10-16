using Godot;
using Renegade.Looks;
using static Checkers.Game.BoardLayout;

namespace Checkers.Game;

/// <summary>
/// A checker: a ridged disc, and a second one stacked on top once it's crowned. The meshes are
/// kept separate from any physics so a captured piece can be handed to a rigid body and knocked
/// off the board.
/// </summary>
public partial class Checker : Node3D
{
	private static readonly CylinderMesh Disc = new() { TopRadius = PieceRadius, BottomRadius = PieceRadius * 0.95f, Height = PieceHeight, RadialSegments = 40 };
	private static readonly TorusMesh Ridge = new() { InnerRadius = PieceRadius * 0.62f, OuterRadius = PieceRadius * 0.74f, Rings = 32, RingSegments = 8 };

	private MeshInstance3D _base = null!;
	private MeshInstance3D _ridge = null!;
	private MeshInstance3D? _crown;
	private MeshInstance3D? _crownRidge;
	private Look _look = null!;

	public bool IsDark { get; private set; }
	public bool King => _crown != null;

	public void Setup(bool dark, PieceSet set, bool king)
	{
		IsDark = dark;
		_base = new MeshInstance3D { Mesh = Disc, Position = new Vector3(0, PieceHeight / 2, 0) };
		_ridge = new MeshInstance3D { Mesh = Ridge, Position = new Vector3(0, PieceHeight, 0), Scale = new Vector3(1, 0.35f, 1) };
		AddChild(_base);
		AddChild(_ridge);
		ApplyStyle(set);
		if (king)
			AddCrown();
	}

	public void ApplyStyle(PieceSet set)
	{
		_look = set.LookOf(IsDark);
		var material = Materials.For(_look, isDisc: true, variant: (int)GetInstanceId());
		foreach (var mesh in Meshes)
			mesh.MaterialOverride = material;
	}

	public System.Collections.Generic.IEnumerable<MeshInstance3D> Meshes
	{
		get
		{
			yield return _base;
			yield return _ridge;
			if (_crown != null)
				yield return _crown;
			if (_crownRidge != null)
				yield return _crownRidge;
		}
	}

	/// <summary>Stacks a second disc on top: a king. Returns the crown so it can be animated.</summary>
	public MeshInstance3D AddCrown()
	{
		var material = Materials.For(_look, isDisc: true, variant: (int)GetInstanceId() + 1);
		_crown = new MeshInstance3D { Mesh = Disc, Position = new Vector3(0, PieceHeight * 1.5f, 0), MaterialOverride = material };
		_crownRidge = new MeshInstance3D { Mesh = Ridge, Position = new Vector3(0, PieceHeight * 2, 0), Scale = new Vector3(1, 0.35f, 1), MaterialOverride = material };
		AddChild(_crown);
		AddChild(_crownRidge);
		return _crown;
	}
}
