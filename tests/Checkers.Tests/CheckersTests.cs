using System.Linq;
using Checkers.Core;
using Renegade.Core;
using Xunit;

namespace Checkers.Tests;

public class CheckersTests
{
	private static int Sq(int row, int column) => row * CheckersGame.Size + column;

	/// <summary>An empty board with the given pieces, Dark to move.</summary>
	private static CheckersGame Setup(params (int Square, Piece Piece)[] pieces)
	{
		var game = new CheckersGame();
		var field = typeof(CheckersGame).GetField("_board", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
		var board = (Piece?[])field.GetValue(game)!;
		System.Array.Clear(board);
		foreach (var (square, piece) in pieces)
			board[square] = piece;
		return game;
	}

	private static readonly Piece DarkMan = new(Colour.Dark, false);
	private static readonly Piece LightMan = new(Colour.Light, false);

	[Fact]
	public void StartsWithTwelveEachOnDarkSquaresAndSevenOpeningMoves()
	{
		var game = new CheckersGame();
		Assert.Equal(12, game.Count(Colour.Dark));
		Assert.Equal(12, game.Count(Colour.Light));
		Assert.Equal(7, game.LegalMoves().Count);
	}

	[Fact]
	public void JumpingIsCompulsory()
	{
		var game = Setup((Sq(5, 2), DarkMan), (Sq(4, 3), LightMan), (Sq(6, 7), DarkMan));
		var moves = game.LegalMoves();
		Assert.Single(moves);
		Assert.Equal(new[] { Sq(4, 3) }, moves[0].Captured);
		Assert.Equal(Sq(3, 4), moves[0].To);
	}

	[Fact]
	public void MultiJumpsContinueToTheEnd()
	{
		var game = Setup((Sq(7, 0), DarkMan), (Sq(6, 1), LightMan), (Sq(4, 3), LightMan), (Sq(0, 7), LightMan));
		var move = Assert.Single(game.LegalMoves());
		Assert.Equal(new[] { Sq(5, 2), Sq(3, 4) }, move.Path);
		var result = game.Play(move);
		Assert.Equal(2, result.CapturedPieces.Count);
		Assert.Equal(1, game.Count(Colour.Light));
	}

	[Fact]
	public void ReachingTheFarRowCrownsAndEndsTheMove()
	{
		var game = Setup((Sq(1, 2), DarkMan), (Sq(7, 7), LightMan));
		var move = game.LegalMoves().First(m => m.To == Sq(0, 1));
		var result = game.Play(move);
		Assert.True(result.Crowned);
		Assert.True(game[Sq(0, 1)]!.Value.King);
	}

	[Fact]
	public void KingsMoveBackwards()
	{
		var game = Setup((Sq(3, 2), new Piece(Colour.Dark, true)), (Sq(0, 7), LightMan));
		Assert.Contains(game.LegalMoves(), m => m.To == Sq(4, 1));
	}

	[Fact]
	public void NoMovesLeftLoses()
	{
		var game = Setup((Sq(5, 2), DarkMan), (Sq(4, 3), LightMan));
		game.Play(game.LegalMoves().Single());
		Assert.True(game.IsOver);
		Assert.Equal(Colour.Dark, game.Winner);
	}

	[Fact]
	public void UndoRestoresTheBoard()
	{
		var game = new CheckersGame();
		game.Play(game.LegalMoves()[0]);
		Assert.True(game.Undo());
		Assert.Equal(12, game.Count(Colour.Dark));
		Assert.Equal(Colour.Dark, game.Turn);
	}

	[Fact]
	public void HardBeatsEasyAndThinksQuickly()
	{
		int hardWins = 0;
		long worst = 0;
		for (int i = 0; i < 4; i++)
		{
			var game = new CheckersGame();
			var hard = new ComputerPlayer(Difficulty.Hard, seed: i);
			var easy = new ComputerPlayer(Difficulty.Easy, seed: 40 + i);
			bool hardIsDark = i % 2 == 0;
			for (int turn = 0; turn < 300 && !game.IsOver; turn++)
			{
				bool hardToMove = (game.Turn == Colour.Dark) == hardIsDark;
				var watch = System.Diagnostics.Stopwatch.StartNew();
				var move = (hardToMove ? hard : easy).ChooseMove(game);
				if (hardToMove)
					worst = System.Math.Max(worst, watch.ElapsedMilliseconds);
				game.Play(move);
			}
			if (game.Winner == (hardIsDark ? Colour.Dark : Colour.Light))
				hardWins++;
		}
		Assert.True(hardWins >= 3, $"Hard won only {hardWins} of 4.");
		Assert.True(worst < 800, $"Slowest move took {worst} ms.");
	}
}
