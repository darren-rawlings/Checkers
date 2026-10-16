using System.Collections.Generic;
using Renegade.Ui;

namespace Checkers.Game;

/// <summary>The guided tour. Clips are recorded from the game with --clip (see Main.PlayClip).</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Moving",
			"Pieces move one square diagonally forward, always on the dark squares. Tap one of your pieces (the ones that can move have a glowing ring), then tap a glowing square.",
			"res://tutorial/move.ogv"),
		new TutorialPage("Jumping",
			"Jump over an opponent's piece to the empty square beyond to capture it. If you can jump, you must, and if the same piece can jump again it keeps going: one tap takes the whole chain.",
			"res://tutorial/jump.ogv"),
		new TutorialPage("Kings",
			"Reach the far row and your piece is crowned with a second disc. A king can move and jump backwards as well as forwards.",
			"res://tutorial/king.ogv"),
		new TutorialPage("Winning",
			"Capture all your opponent's pieces, or leave them with no move, and you win. The winner's pieces catch fire.",
			"res://tutorial/end.ogv"),
		new TutorialPage("Tips",
			"Drag sideways to spin the board, or freeze it in Settings. Against the computer, Undo (ad) takes a move back, and Start over counts as resigning unless you watch an ad.",
			"res://tutorial/move.ogv"),
	};
}
