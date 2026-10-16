# Checkers: design

Store title: **Checkers**. Package `com.renegadegames.checkers`.

## The game
American checkers (English draughts), 8×8. Men move one square diagonally forward; kings move
either way. Jumping is compulsory and a jump must continue while it can; a man reaching the far
row is crowned, which ends the move. You lose with no pieces or no moves; 50 moves in a row with
no capture and no man moving is a draw.

## Feel
- Ridged discs on a checkerboard in the chosen material (the dark squares are a shade over the
  board material, so every Style reads as a checkerboard). Default: Red & Yellow on oak.
- Tap a piece (it gets a glowing ring and its landing squares glow), then tap where to go; a whole
  jump chain is one tap. With nothing selected, the pieces that can move glow; "you must jump"
  when a jump is compulsory.
- **Signature effect, knock-outs:** every piece you jump is knocked clean off the board with real
  physics, spinning, and clatters onto the table, where the captured pieces pile up.
- Crowning: a second disc drops onto the piece with a burst of sparks ("Crowned!").
- Drag sideways to spin the board.

## Modes
Player vs player on one phone; vs computer Easy (1-ply, a third random), Medium (4-ply), Hard
(iterative-deepening alpha-beta with a 350 ms budget per move, the same on any phone; jumps never
cut off mid-exchange).

## Ads
Between-games interstitial every 5th game; Undo (ad) vs the computer takes back your last move.

## Later
International (10×10) draughts with flying kings; a "huffing" casual mode without forced jumps.
