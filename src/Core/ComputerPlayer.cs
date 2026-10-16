using System;
using System.Collections.Generic;
using Renegade.Core;

namespace Checkers.Core;

/// <summary>
/// The computer opponent: negamax with alpha-beta. The evaluation counts material (a king is worth
/// a bit more than one and a half men), rewards men for advancing and for holding the back row
/// (which stops enemy kings), and pieces for the centre. Easy looks one move ahead and sometimes
/// plays at random; Hard looks eight moves ahead, and jumps don't use up depth.
/// </summary>
public sealed class ComputerPlayer
{
	private const int Win = 100_000;
	private readonly Random _random;

	public Difficulty Difficulty { get; }

	public ComputerPlayer(Difficulty difficulty, int? seed = null)
	{
		Difficulty = difficulty;
		_random = seed is { } s ? new Random(s) : new Random();
	}

	private int Depth => Difficulty switch
	{
		Difficulty.Easy => 1,
		Difficulty.Medium => 4,
		_ => 12,
	};

	/// <summary>Hard thinks in deepening rounds until this much time has passed, the same on any phone.</summary>
	public const int HardBudgetMs = 350;

	private System.Diagnostics.Stopwatch? _clock;
	private bool _outOfTime;

	public Move ChooseMove(CheckersGame game)
	{
		var moves = game.LegalMoves();
		if (moves.Count == 0)
			throw new InvalidOperationException("No legal moves.");
		if (moves.Count == 1)
			return moves[0];
		if (Difficulty == Difficulty.Easy && _random.NextDouble() < 0.35)
			return moves[_random.Next(moves.Count)];

		if (Difficulty != Difficulty.Hard)
			return SearchRoot(game, moves, Depth) ?? moves[0];

		// Iterative deepening: each finished round's choice is kept; an unfinished round is discarded.
		_clock = System.Diagnostics.Stopwatch.StartNew();
		_outOfTime = false;
		Move choice = moves[0];
		for (int depth = 2; depth <= Depth; depth += 2)
		{
			var found = SearchRoot(game, moves, depth);
			if (_outOfTime || found == null)
				break;
			choice = found;
		}
		_clock = null;
		return choice;
	}

	private Move? SearchRoot(CheckersGame game, List<Move> moves, int depth)
	{
		int best = int.MinValue;
		var bestMoves = new List<Move>();
		foreach (var move in Ordered(moves))
		{
			var next = game.Clone();
			next.Apply(move);
			int score = -Search(next, depth - 1, -Win * 2, Win * 2);
			if (_outOfTime)
				return null;
			if (score > best)
			{
				best = score;
				bestMoves.Clear();
			}
			if (score == best)
				bestMoves.Add(move);
		}
		return bestMoves[_random.Next(bestMoves.Count)];
	}

	private int Search(CheckersGame game, int depth, int alpha, int beta)
	{
		if (_clock != null && _clock.ElapsedMilliseconds > HardBudgetMs)
		{
			_outOfTime = true;
			return 0;
		}
		if (game.QuietMoves >= CheckersGame.QuietLimit)
			return 0;
		var moves = game.LegalMoves();
		// No moves: the side to move has lost (sooner is worse).
		if (moves.Count == 0)
			return -Win - depth;
		// Keep searching while jumps are pending, so a capture exchange is never cut off halfway.
		if (depth <= 0 && !moves[0].IsJump)
			return Evaluate(game);

		int value = -Win * 2;
		foreach (var move in Ordered(moves))
		{
			var next = game.Clone();
			next.Apply(move);
			value = Math.Max(value, -Search(next, depth - 1, -beta, -alpha));
			alpha = Math.Max(alpha, value);
			if (alpha >= beta)
				break;
		}
		return value;
	}

	/// <summary>Score for the side to move.</summary>
	private static int Evaluate(CheckersGame game)
	{
		int score = 0;
		for (int square = 0; square < 64; square++)
		{
			if (game[square] is not { } piece)
				continue;
			int row = square / CheckersGame.Size, column = square % CheckersGame.Size;
			int value;
			if (piece.King)
			{
				value = 160;
			}
			else
			{
				int advanced = piece.Colour == Colour.Dark ? 7 - row : row;
				bool backRow = piece.Colour == Colour.Dark ? row == 7 : row == 0;
				value = 100 + advanced * 4 + (backRow ? 8 : 0);
			}
			if (column is >= 2 and <= 5 && row is >= 2 and <= 5)
				value += 4;
			score += piece.Colour == game.Turn ? value : -value;
		}
		return score;
	}

	/// <summary>Longest jump chains first: they're usually best, so alpha-beta prunes more.</summary>
	private static IEnumerable<Move> Ordered(List<Move> moves)
	{
		moves.Sort((a, b) => b.Captured.Count.CompareTo(a.Captured.Count));
		return moves;
	}
}
