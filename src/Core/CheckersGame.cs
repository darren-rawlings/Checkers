using System;
using System.Collections.Generic;
using System.Linq;

namespace Checkers.Core;

/// <summary>Dark moves first, from the near side; against the computer, that's you.</summary>
public enum Colour
{
	Dark,
	Light,
}

public readonly record struct Piece(Colour Colour, bool King);

/// <summary>
/// One turn: a step, or a chain of jumps. <see cref="Path"/> lists every square landed on in
/// order (the last is where the piece ends); <see cref="Captured"/> the squares jumped, in order.
/// </summary>
public sealed record Move(int From, IReadOnlyList<int> Path, IReadOnlyList<int> Captured)
{
	public int To => Path[^1];
	public bool IsJump => Captured.Count > 0;
}

/// <summary>What a move did, so the game can animate it.</summary>
public sealed record MoveResult(Colour Mover, Move Move, IReadOnlyList<Piece> CapturedPieces, bool Crowned, bool GameOver);

/// <summary>
/// American checkers (English draughts) on an 8×8 board, squares numbered row * 8 + column with
/// row 0 at the far edge; pieces stand only on dark squares. Men move one square diagonally
/// forward, kings either way; jumping is compulsory and a jump must continue while it can; a man
/// reaching the far row is crowned, which ends the move. You lose with no pieces or no moves.
/// A game with 50 moves in a row without a capture or a man moving is a draw.
/// </summary>
public sealed class CheckersGame
{
	public const int Size = 8;
	public const int QuietLimit = 50;

	private readonly Piece?[] _board = new Piece?[64];
	private readonly Stack<(Piece?[] Board, Colour Turn, bool Over, Colour? Winner, int Quiet)> _history = new();

	public Colour Turn { get; private set; }
	public bool IsOver { get; private set; }

	/// <summary>The winner once the game is over; null for a draw.</summary>
	public Colour? Winner { get; private set; }

	/// <summary>Moves in a row with no capture and no man moving (for the draw rule).</summary>
	public int QuietMoves { get; private set; }

	public CheckersGame() => Reset();

	private CheckersGame(CheckersGame other)
	{
		Array.Copy(other._board, _board, 64);
		Turn = other.Turn;
		IsOver = other.IsOver;
		Winner = other.Winner;
		QuietMoves = other.QuietMoves;
	}

	public CheckersGame Clone() => new(this);

	public void Reset()
	{
		Array.Clear(_board);
		for (int square = 0; square < 64; square++)
		{
			if (!IsDark(square))
				continue;
			int row = square / Size;
			if (row < 3)
				_board[square] = new Piece(Colour.Light, false);
			else if (row > 4)
				_board[square] = new Piece(Colour.Dark, false);
		}
		Turn = Colour.Dark;
		IsOver = false;
		Winner = null;
		QuietMoves = 0;
		_history.Clear();
	}

	public Piece? this[int square] => _board[square];

	public int MovesMade => _history.Count;

	public Colour? LastMover => _history.Count == 0 ? null : _history.Peek().Turn;

	public static bool IsDark(int square) => (square / Size + square % Size) % 2 == 1;

	public static Colour Opponent(Colour colour) => colour == Colour.Dark ? Colour.Light : Colour.Dark;

	public int Count(Colour colour) => _board.Count(p => p?.Colour == colour);

	/// <summary>All legal moves for the side to move. If any jump exists, only jumps are legal.</summary>
	public List<Move> LegalMoves()
	{
		var jumps = new List<Move>();
		var steps = new List<Move>();
		if (IsOver)
			return steps;
		for (int square = 0; square < 64; square++)
		{
			if (_board[square] is not { } piece || piece.Colour != Turn)
				continue;
			AddJumps(square, piece, square, new List<int>(), new List<int>(), jumps);
			if (jumps.Count == 0)
			{
				foreach (var (dr, dc) in Directions(piece))
				{
					int to = Offset(square, dr, dc);
					if (to >= 0 && _board[to] == null)
						steps.Add(new Move(square, new[] { to }, Array.Empty<int>()));
				}
			}
		}
		return jumps.Count > 0 ? jumps : steps;
	}

	/// <summary>Follows every jump chain from <paramref name="at"/>, adding each finished chain.</summary>
	private void AddJumps(int start, Piece piece, int at, List<int> path, List<int> captured, List<Move> into)
	{
		bool extended = false;
		foreach (var (dr, dc) in Directions(piece))
		{
			int over = Offset(at, dr, dc);
			int land = Offset(at, 2 * dr, 2 * dc);
			if (over < 0 || land < 0 || captured.Contains(over))
				continue;
			if (_board[over] is not { } victim || victim.Colour == piece.Colour)
				continue;
			// The moving piece has left its start square, so that counts as empty.
			if (_board[land] != null && land != start)
				continue;
			extended = true;
			path.Add(land);
			captured.Add(over);
			// Crowning ends the move: a man that reaches the far row stops there.
			if (!piece.King && IsCrowningRow(land, piece.Colour))
				into.Add(new Move(start, path.ToArray(), captured.ToArray()));
			else
				AddJumps(start, piece, land, path, captured, into);
			path.RemoveAt(path.Count - 1);
			captured.RemoveAt(captured.Count - 1);
		}
		if (!extended && path.Count > 0)
			into.Add(new Move(start, path.ToArray(), captured.ToArray()));
	}

	public MoveResult Play(Move move)
	{
		var legal = LegalMoves().FirstOrDefault(m => m.From == move.From && m.Path.SequenceEqual(move.Path));
		if (legal == null)
			throw new InvalidOperationException($"Illegal move from {move.From}.");
		_history.Push(((Piece?[])_board.Clone(), Turn, IsOver, Winner, QuietMoves));

		var mover = Turn;
		var piece = _board[legal.From]!.Value;
		_board[legal.From] = null;
		var taken = new List<Piece>();
		foreach (int square in legal.Captured)
		{
			taken.Add(_board[square]!.Value);
			_board[square] = null;
		}
		bool crowned = !piece.King && IsCrowningRow(legal.To, mover);
		_board[legal.To] = crowned ? piece with { King = true } : piece;
		QuietMoves = legal.IsJump || !piece.King ? 0 : QuietMoves + 1;

		Turn = Opponent(mover);
		if (LegalMoves().Count == 0)
		{
			IsOver = true;
			Winner = mover;
		}
		else if (QuietMoves >= QuietLimit)
		{
			IsOver = true;
			Winner = null;
		}
		return new MoveResult(mover, legal, taken, crowned, IsOver);
	}

	/// <summary>
	/// For the computer's search: applies a move known to be legal, without history, validation or the
	/// end-of-game check (the search sees "no moves" itself). Much faster than <see cref="Play"/>.
	/// </summary>
	internal void Apply(Move move)
	{
		var piece = _board[move.From]!.Value;
		_board[move.From] = null;
		foreach (int square in move.Captured)
			_board[square] = null;
		bool crowned = !piece.King && IsCrowningRow(move.To, piece.Colour);
		_board[move.To] = crowned ? piece with { King = true } : piece;
		QuietMoves = move.IsJump || !piece.King ? 0 : QuietMoves + 1;
		Turn = Opponent(piece.Colour);
	}

	public bool Undo()
	{
		if (_history.Count == 0)
			return false;
		var (board, turn, over, winner, quiet) = _history.Pop();
		Array.Copy(board, _board, 64);
		Turn = turn;
		IsOver = over;
		Winner = winner;
		QuietMoves = quiet;
		return true;
	}

	/// <summary>Dark heads for row 0, Light for row 7.</summary>
	public static bool IsCrowningRow(int square, Colour colour) => colour == Colour.Dark ? square / Size == 0 : square / Size == Size - 1;

	private static IEnumerable<(int, int)> Directions(Piece piece)
	{
		int forward = piece.Colour == Colour.Dark ? -1 : 1;
		yield return (forward, -1);
		yield return (forward, 1);
		if (piece.King)
		{
			yield return (-forward, -1);
			yield return (-forward, 1);
		}
	}

	private static int Offset(int square, int dr, int dc)
	{
		int row = square / Size + dr, column = square % Size + dc;
		return row is >= 0 and < Size && column is >= 0 and < Size ? row * Size + column : -1;
	}
}
