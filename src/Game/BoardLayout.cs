using Checkers.Core;
using Godot;

namespace Checkers.Game;

/// <summary>Where everything is, in board space: y up, the playing surface at y = 0, row 0 at the far edge.</summary>
public static class BoardLayout
{
	public const float Cell = 1.0f;
	public const float Border = 0.4f;
	public const float Half = CheckersGame.Size * Cell / 2f + Border;
	public const float Thickness = 0.45f;

	public const float PieceRadius = 0.4f;
	public const float PieceHeight = 0.16f;

	public static Vector3 SquareCenter(int square) =>
		new((square % CheckersGame.Size - 3.5f) * Cell, 0, (square / CheckersGame.Size - 3.5f) * Cell);

	/// <summary>The square under a board-space point, or -1 off the grid.</summary>
	public static int SquareAt(Vector3 local)
	{
		int column = Mathf.FloorToInt(local.X / Cell + 4f);
		int row = Mathf.FloorToInt(local.Z / Cell + 4f);
		if (column < 0 || column >= CheckersGame.Size || row < 0 || row >= CheckersGame.Size)
			return -1;
		return row * CheckersGame.Size + column;
	}
}
